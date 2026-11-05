#!/usr/bin/env bash
# Runs stangent's AI steps through headless Claude Code (D60), for CI and long batch runs:
#   pages:    stangent spec prepare → stangent-page-writer → stangent spec check → stangent-page-verifier → …
#   sections: stangent fsd prepare → candidate finder / link judge / agree checker → stangent fsd check → …
#             then, when fsd.disprove is on: stangent fsd disprove prepare → finding disprover → stangent fsd disprove check → …
#   items:    stangent req prepare → requirement writer → stangent req check → requirement checker → …
# The logic lives in `stangent spec`, `stangent fsd` and `stangent req`; this only runs the subagent each step names,
# then hands its result back.
#
# usage: spec-run.sh [--jobs N] [--max-cost USD|none] [--stop-at-usage PCT] [--estimate] [--model M] [--retry-failed] [--agents] [--chapter N] (<page id>... | --for-fsd <doc> | --for-ticket <key> | --all | --stale | --new | --fsd <doc> | --disprove <doc> | --req <doc> | --ask <question>)
# --for-fsd <doc> (with --chapter N) and --for-ticket <key>: the normal way to make pages (I.1): only the ones that document's sections or that ticket's bundle need, and aren't written.
# --stale: pages already written whose code changed; --new adds every page never written; --all is every page the graph calls for.
# --ask <question>: one question from the reference pages (spec.ask, E.2); prints its status line, then the result as one JSON line.
# --chapter N, with --fsd or --disprove: only that top-level heading, to try a chapter first (`stangent fsd disprove estimate <doc> --chapter N` shows its cost).
# Default (D66): one turn per call. The step's input files go into the prompt, Claude Code runs with no tools and none of
# the user's settings, skills or MCP servers, and the answer comes back as structured output (--json-schema) that this
# script writes to the step's output file; `check` is unchanged. Far fewer tokens than a session that reads files.
# --agents: the plugin's subagents instead, each limited to reading and writing under .stangent/ (D60).
# --max-cost (T.2): the hard stop, see cost-guard.sh for what it can and can't see. A paid step won't start without a
# choice: dollars, or `none`. --estimate prints the estimate and stops. --stop-at-usage: stop once a call reports a plan
# window this percent used. Exit: 0 ok, 1 something refused or failed, 2 no cap chosen, 5 stopped on the cap or a limit, 125 the batch stopped on anything else.
# env:   STANGENT (default: stangent), PLUGIN_DIR (default: this plugin), CLAUDE (default: claude), STANGENT_MAX_COST, STANGENT_STOP_AT_USAGE
# out:   one line per page or section: <status>\t<id>; and when the disprove stage ran, disprove:<status>\t<id>; stopped\t<id>\t<why> at a hard stop
set -euo pipefail

source "$(dirname "$0")/cost-guard.sh"
TYPED="$(printf '%q ' "$0" "$@")"

usage() { sed -n 's/^# usage: //p' "$0" >&2; exit 2; }

# Adds the ids a list command gives. A list that fails stops here: an empty list would look like nothing to do.
add_ids() {
  local filter="$1" out p; shift
  if ! out="$(${STANGENT:-stangent} "$@")"; then
    printf 'error\tstangent %s\t%s\n' "$*" "$(jq -r '.error.code // "no result"' <<<"$out" 2>/dev/null | head -c 200)" >&2
    exit 1
  fi
  while IFS= read -r p; do PAGES+=("$p"); done < <(jq -r "$filter" <<<"$out")
  NAMED=1
}

JOBS=1
MODE=spec
MODEL=""
AGENTS=""
RETRY=""
NAMED=""
PAGES=()
ONLY=()
CHAPTER=""
ARGS=("$@")
for ((i = 0; i < $#; i++)); do
  if [ "${ARGS[$i]}" = --chapter ]; then
    [ $((i + 1)) -lt $# ] || usage
    CHAPTER="${ARGS[$((i + 1))]}"
  fi
done
CHAPTERED=""
MAXCOST=""
STOPUSAGE=""
ESTIMATE=""
DOC=""
while [ $# -gt 0 ]; do
  case "$1" in --jobs|--model|--fsd|--for-fsd|--for-ticket|--disprove|--req|--only|--chapter|--ask|--max-cost|--stop-at-usage) [ $# -ge 2 ] || usage ;; esac
  case "$1" in
    --jobs) JOBS="$2"; shift 2 ;;
    --max-cost) MAXCOST="$2"; shift 2 ;;
    --stop-at-usage) STOPUSAGE="$2"; shift 2 ;;
    --estimate) ESTIMATE=1; shift ;;
    --model) MODEL="$2"; shift 2 ;;
    --retry-failed) RETRY="--retry-failed"; shift ;;
    --agents) AGENTS=1; shift ;;
    --all) add_ids '.result.pages[].id' spec pages; shift ;;
    --fsd) MODE=fsd; CHAPTERED=1; add_ids '.result[] | .doc + "\t" + .section' fsd sections --doc "$2" ${CHAPTER:+--chapter "$CHAPTER"}; shift 2 ;;
    --disprove) MODE=disprove; DOC="$2"; CHAPTERED=1; add_ids '.result[] | .doc + "\t" + .section' fsd sections --doc "$2" ${CHAPTER:+--chapter "$CHAPTER"}; shift 2 ;;
    --chapter) shift 2 ;;
    --req) MODE=req; add_ids '.result[] | .doc + "\t" + .section' req sections --doc "$2"; shift 2 ;;
    --stale) add_ids '.result.stale[].id' spec stale; shift ;;
    --new) add_ids '.result.new[].id' spec stale; shift ;;
    --for-fsd) CHAPTERED=1; add_ids '.result.pages[].id' spec needed --doc "$2" ${CHAPTER:+--chapter "$CHAPTER"}; shift 2 ;;
    --for-ticket) add_ids '.result.pages[].id' spec needed --ticket "$2"; shift 2 ;;
    --only) ONLY+=("$2"); shift 2 ;;
    --ask) MODE=ask; PAGES+=("$2"); NAMED=1; shift 2 ;;
    -*) usage ;;
    *) PAGES+=("$1"); NAMED=1; shift ;;
  esac
done
[ -n "$NAMED" ] || usage
[ -z "$CHAPTER" ] || [ -n "$CHAPTERED" ] || usage
case "$JOBS" in ''|0|*[!0-9]*) usage ;; esac

# --only <section> (repeatable), with --fsd, --disprove or --req: just these sections.
if [ ${#ONLY[@]} -gt 0 ]; then
  KEPT=()
  for p in ${PAGES[@]+"${PAGES[@]}"}; do
    for o in "${ONLY[@]}"; do [ "${p#*$'\t'}" = "$o" ] && KEPT+=("$p"); done
  done
  PAGES=(${KEPT[@]+"${KEPT[@]}"})
fi

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL RETRY MODE AGENTS
export PLUGIN_DIR="${PLUGIN_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

# One call with no tools: every .stangent/ file the prompt names goes into the input, Claude Code enforces the output
# schema, and the structured answer is written where the step wants it. The input hash is the same in both modes.
single_turn() {
  local step="$1" usage="$2" out schema input label f text raw="$2.raw"
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  input=""
  while IFS=$'\t' read -r label f; do
    # A stored bundle holds only what's unique to it; code puts the shared text back just before the call (D130).
    if ! text="$($STANGENT spec assemble --for-batch "$f")"; then
      printf '{"type":"result","is_error":true,"result":"stangent spec assemble failed on %s"}\n' "$f" >"$usage"
      : >"$raw"; : >"$2.err"
      return
    fi
    input+="# $label"$'\n\n'"$text"$'\n\n'
  done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | sed -E 's/^(.*): (\.stangent\/.*)$/\1\t\2/')
  rm -f "$out"
  # stream-json for the same result line plus any usage-limit event (T.2); the result is what's read.
  printf '%s' "$input" | $CLAUDE -p ${MODEL:+--model "$MODEL"} ${CALL_BUDGET:+--max-budget-usd "$CALL_BUDGET"} --tools "" --setting-sources "" --disable-slash-commands \
    --strict-mcp-config --exclude-dynamic-system-prompt-sections --no-session-persistence --output-format stream-json --verbose \
    --json-schema "$schema" \
    --system-prompt "You are one of stangent's AI steps. The input holds your instructions, the only evidence you may use, and on a retry your previous attempt and what to fix. Where the instructions say to write a result file, return that JSON as your structured output instead. Reply with nothing else." \
    >"$raw" 2>"$2.err" || true
  jq -Rc 'fromjson? | select(type == "object" and .type == "result")' "$raw" 2>/dev/null | tail -n 1 >"$usage" || true
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

# stangent crashed: no envelope, so no next step to read. Without this the loop below would call Claude on nothing, forever.
no_envelope() { ! jq -e 'type == "object" and has("ok")' <<<"$1" >/dev/null 2>&1; }

# Runs the steps until none is left. $step holds the first; it holds the last when this returns. "$@" is the check command.
run_steps() {
  local page="$1" usage="$2" action agent prompt f; shift 2
  if no_envelope "$step"; then
    printf 'error\t%s\tstangent prepare gave no result\n' "$page"
    rm -f "$usage"
    exit 255
  fi
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" != "none" ]; do
    agent="$(jq -r '.result.next.agent' <<<"$step")"
    prompt="$(jq -r '.result.next.prompt' <<<"$step")"
    if ! guard_before_call; then
      guard_stopped_line "$page"
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    if [ -n "$AGENTS" ]; then
      # A subagent reads files itself, so each file its prompt names goes back to its full text first (D130).
      while IFS= read -r f; do $STANGENT spec assemble --in-place "$f" >/dev/null || true; done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | sed -E 's/^.*: (\.stangent\/.*)$/\1/')
      # Absolute rules and each command back in the repo (D89): a `cd` mustn't change what the rules allow.
      CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 $CLAUDE -p --plugin-dir "$PLUGIN_DIR" --agent "stangent:$agent" ${MODEL:+--model "$MODEL"} ${CALL_BUDGET:+--max-budget-usd "$CALL_BUDGET"} \
        --settings "$READ_ONLY_SETTINGS" --permission-mode dontAsk --allowedTools "Read(/$(pwd -P)/.stangent/**)" "Edit(/$(pwd -P)/.stangent/**)" --output-format json "$prompt" >"$usage" 2>"$usage.err" || true
      cp "$usage" "$usage.raw" 2>/dev/null || : >"$usage.raw"
    else
      single_turn "$step" "$usage"
    fi
    # A limit or the cap: stop cleanly, say what was done and how to resume (T.2).
    if ! guard_after_call "$usage.raw" "$usage" "$usage.err"; then
      guard_stopped_line "$page"
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    # Claude Code itself failed (an outage, no login): not the page's fault. Leave the page's run where
    # it is, report it, and stop the batch (exit 255 stops xargs); `prepare` starts it again on the next run.
    if ! jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
      local why
      why="$(jq -r '.result // .error // empty' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
      printf 'error\t%s\t%s\n' "$page" "${why:-claude exited without a result}"
      rm -f "$usage"
      exit 255
    fi
    step="$($STANGENT "$@" --usage "$usage" || true)"
    if no_envelope "$step"; then
      printf 'error\t%s\tstangent check gave no result\n' "$page"
      rm -f "$usage"
      exit 255
    fi
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
}

# The disprove stage. Says nothing when it's off, so a saved run prints what it always did.
disprove() {
  local page="$1" usage="$2" status
  step="$($STANGENT fsd disprove prepare "${args[@]}" || true)"
  run_steps "$page" "$usage" fsd disprove check "${args[@]}"
  status="$(jq -r '.result.status // .error.code' <<<"$step")"
  guard_mark_refused "$step" "$page"
  [ "$status" = off ] || printf 'disprove:%s\t%s\n' "$status" "$page"
}

run_page() {
  local page="$1" step usage args
  usage="$(mktemp)"
  if [ "$MODE" = spec ]; then args=("$page"); else args=("${page%%$'\t'*}" "${page#*$'\t'}"); fi
  if [ "$MODE" = disprove ]; then
    disprove "$page" "$usage"
    rm -f "$usage"
    return
  fi
  if [ "$MODE" = ask ]; then
    step="$($STANGENT spec ask "$page" || true)"
    run_steps "$page" "$usage" spec ask-check "$page"
    printf '%s\t%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$page"
    jq -c '.result' <<<"$step"
    guard_mark_refused "$step" "$page"
    rm -f "$usage"
    return
  fi
  # prepare/check exit 1 when a check fails; the next step is still in the envelope.
  case "$MODE" in
    fsd) step="$($STANGENT fsd prepare "${args[@]}" || true)" ;;
    req) step="$($STANGENT req prepare "${args[@]}" $RETRY || true)" ;;
    *) step="$($STANGENT spec prepare "${args[@]}" $RETRY || true)" ;;
  esac
  run_steps "$page" "$usage" "$MODE" check "${args[@]}"
  printf '%s\t%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$page"
  guard_mark_refused "$step" "$page"
  [ "$MODE" != fsd ] || disprove "$page" "$usage"
  rm -f "$usage"
}
export -f run_page run_steps disprove single_turn no_envelope
export -f guard_before_call guard_after_call guard_stop guard_stopped_line guard_lock guard_unlock guard_mark_refused

[ ${#PAGES[@]} -gt 0 ] || exit 0

guard_init "$MAXCOST" "$STOPUSAGE" "$JOBS" "$TYPED"
# An estimate before anything runs (T.2). "At least": each kind's fewest calls per item; a retry adds more.
case "$MODE" in
  spec) guard_estimate "spec check" $((${#PAGES[@]} * 2)) ;;
  req) guard_estimate "req check" $((${#PAGES[@]} * 2)) ;;
  fsd) guard_estimate "fsd check" "${#PAGES[@]}" ;;
  ask) guard_estimate "spec ask-check" 1 ;;
  disprove)
    # The bundles are built by this free command, so its count is exact for what's pending.
    calls="$($STANGENT fsd disprove estimate "$DOC" ${CHAPTER:+--chapter "$CHAPTER"} 2>/dev/null | jq -r '.result.calls // empty' 2>/dev/null || true)"
    guard_estimate "fsd disprove check" "${calls:-${#PAGES[@]}}" ;;
esac
[ -z "$ESTIMATE" ] || exit 0
[ ! -f "$STANGENT_COST_DIR/stopped" ] || guard_finish 0

export READ_ONLY_SETTINGS
READ_ONLY_SETTINGS="$(guard_read_only_settings)"
guard_app_before
rc=0
printf '%s\0' "${PAGES[@]}" | xargs -0 -n 1 -P "$JOBS" bash -c 'run_page "$1"' _ || rc=$?
guard_app_after
guard_finish "$rc"
