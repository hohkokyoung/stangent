#!/usr/bin/env bash
# Runs Phase 4's fix loop for bugs through headless Claude Code (D343–D350):
#   stangent fix start → fix prepare → stangent-fixer (multi-turn, in the ticket's worktree, under limits) → fix check
#   (→ one retry) → fix report
# The logic lives in `stangent fix`; this only runs the agent each step names and hands its result back. Each ticket
# needs a reproduced bug proof first (`ticket-run.sh --prove`); without one `fix start` refuses and the report says
# "no failing test" (D346). Every fix is on its own branch, stangent/fix-<KEY>, in .stangent/fix/<KEY>/worktree:
# nothing is ever pushed and no pull request is opened (D347).
#
# usage: fix-run.sh [--jobs N] [--max-cost USD|none] [--stop-at-usage PCT] [--estimate] [--model M] [--effort E] [--context files|ticket|bundle] [--base REF] [--replace] <KEY>...
# --context (D345, D357): what the fixer gets: files (the ticket, the code, the failing test and the finder's files, the
# default), ticket (without the files) or bundle (the whole ticket bundle). --base: the commit the branch starts from (default HEAD). --replace: start again,
# removing the ticket's earlier fix worktree and branch.
# --model, --effort (D348, D351): Sonnet 5.5 at medium effort unless given; they win over stangent.yaml, as on every driver.
# Caps (D348): each fixer call 30 turns and $1 and never more than what's left of the ticket's $3, the proof's cost
# included (stangent.yaml `fix:`); --max-cost is the run's hard stop (cost-guard.sh). A call starts only if all its cap is left.
# Exit: 0 ok, 1 something refused or failed (the rest went on), 2 no cap chosen, 5 stopped on the cap or a limit, 125 the batch stopped on anything else.
# env:   STANGENT (default: stangent), PLUGIN_DIR (default: this plugin), CLAUDE (default: claude), STANGENT_MAX_COST, STANGENT_STOP_AT_USAGE
# out:   one line per ticket and step: <status>\t<key>\t<step>; the last per ticket is <why it stopped>\t<key>\tfix
set -euo pipefail

source "$(dirname "$0")/cost-guard.sh"
source "$(dirname "$0")/ai-run.sh"
TYPED="$(printf '%q ' "$0" "$@")"

usage() { sed -n 's/^# usage: //p' "$0" >&2; exit 2; }

JOBS=1
MODEL=""
EFFORT=""
CONTEXT=files
BASE=""
REPLACE=""
MAXCOST=""
STOPUSAGE=""
ESTIMATE=""
KEYS=()
while [ $# -gt 0 ]; do
  case "$1" in --jobs|--model|--effort|--context|--base|--max-cost|--stop-at-usage) [ $# -ge 2 ] || usage ;; esac
  case "$1" in
    --jobs) JOBS="$2"; shift 2 ;;
    --max-cost) MAXCOST="$2"; shift 2 ;;
    --stop-at-usage) STOPUSAGE="$2"; shift 2 ;;
    --estimate) ESTIMATE=1; shift ;;
    --model) MODEL="$2"; shift 2 ;;
    --effort) EFFORT="$2"; shift 2 ;;
    --context) CONTEXT="$2"; shift 2 ;;
    --base) BASE="$2"; shift 2 ;;
    --replace) REPLACE=1; shift ;;
    -*) usage ;;
    *) KEYS+=("$1"); shift ;;
  esac
done
[ ${#KEYS[@]} -gt 0 ] || usage
case "$JOBS" in ''|0|*[!0-9]*) usage ;; esac
case "$CONTEXT" in files|ticket|bundle) ;; *) usage ;; esac
# D348: the fixer runs on Sonnet 5.5 at medium effort; a flag changes it for one run (D351).
MODEL="${MODEL:-claude-sonnet-5-5}"
EFFORT="${EFFORT:-medium}"

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL EFFORT CONTEXT BASE REPLACE
export PLUGIN_DIR="${PLUGIN_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

# The fixer: the plugin's agent, started in the ticket's worktree with its input on stdin (it may not read stangent's
# folder), allowed only the tools and commands `stangent fix` named, with its caps; its plan comes back as structured
# output and is written where the step wants it. The whole conversation is kept beside the run.
fixer_call() {
  local step="$1" usage="$2" key="$3" limits cwd input schema out transcript allowed=() denied=()
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  cwd="$(jq -r '.cwd' <<<"$limits")"
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  input="$(cat "$(jq -r '.result.next.input' <<<"$step")")"
  while IFS= read -r t; do allowed+=("$t"); done < <(jq -r '.allowedTools[]' <<<"$limits")
  while IFS= read -r t; do denied+=("$t"); done < <(jq -r '.disallowedTools[]?' <<<"$limits")
  mkdir -p ".stangent/fix/$key/transcripts"
  transcript="$PWD/.stangent/fix/$key/transcripts/fixer-$(date +%Y%m%d-%H%M%S).jsonl"
  rm -f "$out"
  # No user or project settings (their allow rules would widen the fixer's), no MCP, no auto memory. Each command starts
  # back in the worktree (D89). Not `--agent stangent:stangent-fixer`: an agent's `tools:` list drops the StructuredOutput
  # tool `--json-schema` needs, and the plugin's read-only hook fires on its name (D354). Called as the file finder is;
  # the instructions are in the input.
  printf '%s' "$input" | (cd "$cwd" && CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 $CLAUDE -p \
    --model "$AI_MODEL" --effort "$AI_EFFORT" --tools "$(jq -r '.tools' <<<"$limits")" --allowedTools "${allowed[@]}" \
    ${denied[@]+--disallowedTools "${denied[@]}"} --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" \
    --setting-sources "" --settings '{"autoMemoryEnabled":false}' --strict-mcp-config --disable-slash-commands --no-session-persistence \
    --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
    --json-schema "$schema" --output-format stream-json --verbose \
    --append-system-prompt "You are one of stangent's AI steps: the stangent-fixer. Your prompt holds your instructions, the bug, the failing test, your limits and your output schema, and on a retry the checks that failed. The worktree is your current folder. Return your plan as your structured output.") \
    >"$transcript" 2>"$usage.err" || true
  cp "$transcript" "$usage.raw" 2>/dev/null || true
  result_with_rounds "$transcript" "$usage"
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

# stangent crashed: no envelope, so no next step to read. Without this the loop below would call Claude on nothing, forever.
stop_if_no_envelope() {
  jq -e 'type == "object" and has("ok")' <<<"$1" >/dev/null 2>&1 && return 0
  printf 'error\t%s\t%s\tstangent gave no result\n' "$2" "$3"
  if [ -n "${4:-}" ]; then rm -f "$4"; fi
  exit 255
}

# One ticket to the end: start, prepare, the fixer and its check (again on the one retry), the report.
run_fix() {
  local key="$1" out step usage action reason
  out="$($STANGENT fix start "$key" ${BASE:+--base "$BASE"} ${REPLACE:+--replace} || true)"
  stop_if_no_envelope "$out" "$key" start ""
  printf '%s\t%s\tstart\n' "$(jq -r 'if .ok then "started" else .error.code end' <<<"$out")" "$key"
  if ! jq -e '.ok' <<<"$out" >/dev/null 2>&1; then
    printf 'error\t%s\tstart\t%s\n' "$key" "$(jq -r '.error.message // "no result"' <<<"$out" | head -c 300)" >&2
    # No failing test (D346): recorded, so the report says why the ticket goes back with no fix tried.
    if [ "$(jq -r '.error.code // ""' <<<"$out")" = no_failing_test ]; then $STANGENT fix report "$key" >/dev/null || true; fi
    guard_mark_refused "$out" "$key start"
    return 0
  fi
  jq -r '.result.warnings[]? | "note: '"$key"': " + .' <<<"$out" >&2 || true
  step="$($STANGENT fix prepare "$key" --context "$CONTEXT" || true)"
  stop_if_no_envelope "$step" "$key" prepare ""
  usage="$(mktemp)"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" = run ]; do
    if { [ -z "${AI_ALL_CHECKED:-}" ] && ! ai_settings_all; } || ! ai_settings stangent-fixer; then
      printf 'error\t%s\tfix\t%s\n' "$key" "$AI_SETTINGS_ERROR"
      rm -f "$usage"
      exit 255
    fi
    AI_ALL_CHECKED=1
    # The hard stop (T.2): the call may spend all of its own cap, so it starts only if that much is left.
    if ! guard_before_call "$(jq -r '.result.next.limits.maxBudgetUsd' <<<"$step")"; then
      guard_stopped_line "$key" | sed 's/$/\tfix/'
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    fixer_call "$step" "$usage" "$key"
    if ! guard_after_call "$usage.raw" "$usage" "$usage.err"; then
      guard_stopped_line "$key" | sed 's/$/\tfix/'
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    # Claude Code itself failed (an outage, no login): not the fixer's doing, so nothing is recorded; `--replace` starts
    # again. Only a call that hit its own turn or dollar cap goes on to `check`, which stops the ticket there (D348).
    if ! jq -e '.type == "result" and ((.is_error | not) or ((.subtype // "") | test("max_turns|budget")))' "$usage" >/dev/null 2>&1; then
      printf 'error\t%s\tfix\t%s\n' "$key" "$(jq -r '.result // .error // empty' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ' || true)"
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    stamp_usage "$usage" stangent-fixer agentic
    step="$($STANGENT fix check "$key" --usage "$usage" || true)"
    stop_if_no_envelope "$step" "$key" check "$usage"
    printf '%s\t%s\tcheck\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$key"
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
  rm -f "$usage" "$usage.raw" "$usage.err"
  out="$($STANGENT fix report "$key" || true)"
  reason="$(jq -r '.result.stop.reason // .error.code // "error"' <<<"$out")"
  printf '%s\t%s\tfix\n' "$reason" "$key"
  [ "$reason" = fixed ] || guard_mark_refused '{"ok":false}' "$key $reason"
}
export -f run_fix fixer_call stop_if_no_envelope
export -f guard_before_call guard_after_call guard_stop guard_stopped_line guard_lock guard_unlock guard_mark_refused

guard_init "$MAXCOST" "$STOPUSAGE" "$JOBS" "$TYPED"
# At least one fixer call a ticket; a retry adds one.
guard_estimate "fix check" "${#KEYS[@]}"
[ -z "$ESTIMATE" ] || exit 0
[ ! -f "$STANGENT_COST_DIR/stopped" ] || guard_finish 0
# The fix works in its own worktree: the developer's checkout must come out as it went in (T.5).
guard_app_before
rc=0
printf '%s\0' "${KEYS[@]}" | xargs -0 -n 1 -P "$JOBS" bash -c 'run_fix "$1"' _ || rc=$?
guard_app_after
guard_finish "$rc"
