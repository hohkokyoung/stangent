#!/usr/bin/env bash
# D416: the flows the PM's questions link to, in plain words, through headless Claude Code.
#   stangent fsd view <doc> --max-cost <cap> → code picks each linked flow's start, who may start it, what it changes and
#   how it stops early; stangent-view-writer (no tools: every step is in its prompt, capped) words each pick →
#   stangent fsd view-check <doc> (one retry for the flows with problems; then a failing line keeps the code's own label).
#   The words land in specs/fsd/<doc>/flow-words.json and the PM's view is rewritten; commit both. The logic lives in
#   `stangent fsd view*`; this only runs the agent.
#
# usage: fsd-view-run.sh [--max-cost USD|none] [--stop-at-usage PCT] [--estimate] [--model M] [--effort E] <doc>
# --max-cost (T.2): the hard stop, see cost-guard.sh; `stangent fsd view` caps the writer's call at it too.
# Exit: 0 ok, 1 the view or its check was refused, 2 no cap chosen, 5 stopped on the cap or a limit, 125 anything else.
# env:   STANGENT (default: stangent), CLAUDE (default: claude), STANGENT_MAX_COST, STANGENT_STOP_AT_USAGE
# out:   one line: <status>\t<doc>\tview\t<summary>
set -euo pipefail

source "$(dirname "$0")/cost-guard.sh"
source "$(dirname "$0")/ai-run.sh"
TYPED="$(printf '%q ' "$0" "$@")"

usage() { sed -n 's/^# usage: //p' "$0" >&2; exit 2; }

MODEL=""
EFFORT=""
MAXCOST=""
STOPUSAGE=""
ESTIMATE=""
DOC=""
while [ $# -gt 0 ]; do
  case "$1" in --model|--effort|--max-cost|--stop-at-usage) [ $# -ge 2 ] || usage ;; esac
  case "$1" in
    --max-cost) MAXCOST="$2"; shift 2 ;;
    --stop-at-usage) STOPUSAGE="$2"; shift 2 ;;
    --estimate) ESTIMATE=1; shift ;;
    --model) MODEL="$2"; shift 2 ;;
    --effort) EFFORT="$2"; shift 2 ;;
    -*) usage ;;
    *) [ -z "$DOC" ] || usage; DOC="$1"; shift ;;
  esac
done
[ -n "$DOC" ] || usage

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL EFFORT DOC
AGENT=stangent-view-writer

stop_if_no_envelope() {
  jq -e 'type == "object" and has("ok")' <<<"$1" >/dev/null 2>&1 && return 0
  printf 'error\t%s\tview\tstangent gave no result\n' "$DOC"
  exit 125
}

word_flows() {
  local step="$1" usage="$2" out schema input limits transcript
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  input=""
  while IFS=$'\t' read -r label f; do
    input+="# $label"$'\n\n'"$(cat "$f")"$'\n\n'
  done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | sed -E 's/^(.*): (\.stangent\/.*)$/\1\t\2/')
  mkdir -p ".stangent/fsd/$DOC/view-transcripts"
  transcript="$PWD/.stangent/fsd/$DOC/view-transcripts/view-$(date +%Y%m%d-%H%M%S).jsonl"
  rm -f "$out"
  # No tools: every step it words is in the prompt, so it reads nothing (D416).
  printf '%s' "$input" | $CLAUDE -p --model "$AI_MODEL" --effort "$AI_EFFORT" --tools "" \
    --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" \
    --setting-sources "" --settings "$VIEW_SETTINGS" --strict-mcp-config --disable-slash-commands --no-session-persistence \
    --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
    --json-schema "$schema" --output-format stream-json --verbose \
    --append-system-prompt "You are one of stangent's AI steps: the stangent-view-writer. Your prompt holds your instructions, each flow's picked steps and your output schema, and on a retry your previous attempt and what to fix. Return the JSON as your structured output." \
    >"$transcript" 2>"$usage.err" || true
  cp "$transcript" "$usage.raw" 2>/dev/null || true
  jq -c 'select(.type == "result")' "$transcript" 2>/dev/null | tail -n 1 >"$usage" || true
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

guard_init "$MAXCOST" "$STOPUSAGE" 1 "$TYPED"
guard_estimate "fsd view-check" 1
[ -z "$ESTIMATE" ] || exit 0
[ ! -f "$STANGENT_COST_DIR/stopped" ] || guard_finish 0
if [ -z "$COST_CAP" ]; then
  guard_stop no_cap "an AI puts the linked flows into plain words for the PM's view, which costs money, and no cap was chosen. Pass --max-cost <dollars> (or STANGENT_MAX_COST), or --max-cost none."
  guard_finish 0
fi
if ! ai_settings "$AGENT"; then
  printf 'error\t%s\tsettings\t%s\n' "$DOC" "$AI_SETTINGS_ERROR"
  exit 125
fi
export AI_MODEL AI_EFFORT
export VIEW_SETTINGS
VIEW_SETTINGS="$(guard_read_only_settings '{"autoMemoryEnabled":false}')"

step="$($STANGENT fsd view "$DOC" --max-cost "$COST_CAP" || true)"
stop_if_no_envelope "$step"
guard_mark_refused "$step" "$DOC view"
usage="$(mktemp)"
action="$(jq -r '.result.next.action // "none"' <<<"$step" 2>/dev/null || echo none)"
guard_app_before
while [ "$action" = view ]; do
  own="$(jq -r '.result.next.limits.maxBudgetUsd // empty' <<<"$step")"
  if ! guard_before_call "$own"; then guard_stopped_line "$DOC" | sed 's/$/\tview/'; break; fi
  word_flows "$step" "$usage"
  if ! guard_after_call "$usage.raw" "$usage" "$usage.err"; then guard_stopped_line "$DOC" | sed 's/$/\tview/'; break; fi
  if ! jq -e '.type == "result" and ((.is_error | not) or ((.subtype // "") | test("max_turns|budget")))' "$usage" >/dev/null 2>&1; then
    why="$(jq -r '.result // .error // empty' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
    printf 'error\t%s\tview\t%s\n' "$DOC" "${why:-claude exited without a result}"
    rm -f "$usage" "$usage.raw" "$usage.err"
    guard_app_after
    exit 125
  fi
  stamp_usage "$usage" "$AGENT" agentic
  step="$($STANGENT fsd view-check "$DOC" --usage "$usage" || true)"
  stop_if_no_envelope "$step"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
done
guard_app_after
rm -f "$usage" "$usage.raw" "$usage.err"
printf '%s\t%s\tview\t%s\n' "$(jq -r 'if .ok then "worded" else .error.code end' <<<"$step")" "$DOC" "$(jq -r '.result.summary // empty' <<<"$step")"
guard_mark_refused "$step" "$DOC view-check"
guard_finish 0
