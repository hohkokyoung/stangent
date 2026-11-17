#!/usr/bin/env bash
# Phase fs layer 4 (D379, D387): the FSD's new words for a document's rulings, through headless Claude Code.
#   stangent fsd write <doc> --max-cost <cap> → the PM's own words written by code; for rulings that the code is right,
#   stangent-fsd-writer (read-only search of a worktree of HEAD, capped) → stangent fsd write-check <doc> (one retry for
#   the drafts that failed, then they are listed). Every edit is uncommitted: run `stangent fsd check <doc>`, read the
#   chapter diff with the PM, and commit (D380). The logic lives in `stangent fsd write*`; this only runs the agent.
#
# usage: fsd-write-run.sh [--max-cost USD|none] [--stop-at-usage PCT] [--estimate] [--model M] [--effort E] [--section ID [--item N --map M]] <doc>
# --section, --item and --map go to `stangent fsd write` as they are (where the PM's words about code no section covers go).
# --max-cost (T.2): the hard stop, see cost-guard.sh; `stangent fsd write` caps the drafter's call at it too.
# Exit: 0 ok, 1 the write or its check was refused (or drafts listed), 2 no cap chosen, 5 stopped on the cap or a limit, 125 anything else.
# env:   STANGENT (default: stangent), CLAUDE (default: claude), STANGENT_MAX_COST, STANGENT_STOP_AT_USAGE
# out:   one line: <status>\t<doc>\twrite\t<summary>
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
PASS=()
while [ $# -gt 0 ]; do
  case "$1" in --model|--effort|--max-cost|--stop-at-usage|--section|--item|--map) [ $# -ge 2 ] || usage ;; esac
  case "$1" in
    --max-cost) MAXCOST="$2"; shift 2 ;;
    --stop-at-usage) STOPUSAGE="$2"; shift 2 ;;
    --estimate) ESTIMATE=1; shift ;;
    --model) MODEL="$2"; shift 2 ;;
    --effort) EFFORT="$2"; shift 2 ;;
    --section|--item|--map) PASS+=("$1" "$2"); shift 2 ;;
    -*) usage ;;
    *) [ -z "$DOC" ] || usage; DOC="$1"; shift ;;
  esac
done
[ -n "$DOC" ] || usage

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL EFFORT DOC
AGENT=stangent-fsd-writer

stop_if_no_envelope() {
  jq -e 'type == "object" and has("ok")' <<<"$1" >/dev/null 2>&1 && return 0
  printf 'error\t%s\twrite\tstangent gave no result\n' "$DOC"
  exit 125
}

draft_search() {
  local step="$1" usage="$2" run=write out schema input limits transcript root prefix dir tmp="" allowed=() denied=() f
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  root="$(jq -r '.root' <<<"$limits")"
  dir="$PWD"
  if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    prefix="$(git rev-parse --show-prefix)"
    tmp="$(cd "$(mktemp -d)" && pwd -P)"
    # At the commit the run was prepared on, which the checks hold its evidence to.
    if ! git worktree add --detach -q "$tmp/repo" "$(jq -r '.commit // "HEAD"' <<<"$limits")" 2>"$tmp/error"; then
      printf 'error\t%s\t%s\tno worktree for the drafter: %s\n' "$DOC" "$run" "$(head -c 300 "$tmp/error" | tr '\n' ' ')" >&2
      rm -rf "$tmp"; : >"$usage"; return 0
    fi
    trap 'git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"; trap - INT TERM' INT TERM
    dir="$tmp/repo/${prefix%/}"; dir="${dir%/}"
    if [ ! -d "$dir" ]; then
      printf 'error\t%s\t%s\tthe repo folder %s is not in what git tracks, so the drafter has nothing to read\n' "$DOC" "$run" "${prefix:-.}" >&2
      git worktree remove --force "$tmp/repo" >/dev/null 2>&1; rm -rf "$tmp"; trap - INT TERM
      : >"$usage"; return 0
    fi
    # Rules name the folder as //<absolute path> (D89): the worktree's, from here on.
    limits="$(jq -c --arg root "$root" --arg wt "/$dir" 'walk(if type == "string" then (if startswith("Read(" + $root) then "Read(" + $wt + ltrimstr("Read(" + $root) else . end) else . end)' <<<"$limits")"
  fi
  while IFS= read -r f; do allowed+=("$f"); done < <(jq -r '.allowedTools[]' <<<"$limits")
  while IFS= read -r f; do denied+=("$f"); done < <(jq -r '.disallowedTools[]?' <<<"$limits")
  input=""
  while IFS=$'\t' read -r label f; do
    input+="# $label"$'\n\n'"$(cat "$f")"$'\n\n'
  done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | sed -E 's/^(.*): (\.stangent\/.*)$/\1\t\2/')
  mkdir -p ".stangent/fsd/$DOC/write/transcripts"
  transcript="$PWD/.stangent/fsd/$DOC/write/transcripts/write-$(date +%Y%m%d-%H%M%S).jsonl"
  rm -f "$out"
  (
    if [ -n "$tmp" ]; then trap 'git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"' EXIT INT TERM; fi
    printf '%s' "$input" | (cd "$dir" && $CLAUDE -p --model "$AI_MODEL" --effort "$AI_EFFORT" --tools "$(jq -r '.tools' <<<"$limits")" --allowedTools "${allowed[@]}" \
      ${denied[@]+--disallowedTools "${denied[@]}"} --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" \
      --setting-sources "" --settings "$WRITE_SETTINGS" --strict-mcp-config --disable-slash-commands --no-session-persistence \
      --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
      --json-schema "$schema" --output-format stream-json --verbose \
      --append-system-prompt "You are one of stangent's AI steps: the stangent-fsd-writer. Your prompt holds your instructions, the rulings to write with their code and your output schema, and on a retry your previous attempt and what to fix. The repo is your current folder. Return the JSON as your structured output.") \
      >"$transcript" 2>"$usage.err" || true
  )
  cp "$transcript" "$usage.raw" 2>/dev/null || true
  [ -n "$tmp" ] && trap - INT TERM
  jq -c 'select(.type == "result")' "$transcript" 2>/dev/null | tail -n 1 >"$usage" || true
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

git worktree prune 2>/dev/null || true

guard_init "$MAXCOST" "$STOPUSAGE" 1 "$TYPED"
guard_estimate "fsd write-check" 1
[ -z "$ESTIMATE" ] || exit 0
[ ! -f "$STANGENT_COST_DIR/stopped" ] || guard_finish 0
if [ -z "$COST_CAP" ]; then
  guard_stop no_cap "an AI drafts the FSD's words for rulings that the code is right, which costs money, and no cap was chosen. Pass --max-cost <dollars> (or STANGENT_MAX_COST), or --max-cost none."
  guard_finish 0
fi
if ! ai_settings "$AGENT"; then
  printf 'error\t%s\tsettings\t%s\n' "$DOC" "$AI_SETTINGS_ERROR"
  exit 125
fi
export AI_MODEL AI_EFFORT
export WRITE_SETTINGS
WRITE_SETTINGS="$(guard_read_only_settings '{"autoMemoryEnabled":false}')"

step="$($STANGENT fsd write "$DOC" --max-cost "$COST_CAP" ${PASS[@]+"${PASS[@]}"} || true)"
stop_if_no_envelope "$step"
guard_mark_refused "$step" "$DOC write"
usage="$(mktemp)"
action="$(jq -r '.result.next.action // "none"' <<<"$step" 2>/dev/null || echo none)"
guard_app_before
while [ "$action" = write ]; do
  own="$(jq -r '.result.next.limits.maxBudgetUsd // empty' <<<"$step")"
  if ! guard_before_call "$own"; then guard_stopped_line "$DOC" | sed 's/$/\twrite/'; break; fi
  draft_search "$step" "$usage"
  if ! guard_after_call "$usage.raw" "$usage" "$usage.err"; then guard_stopped_line "$DOC" | sed 's/$/\twrite/'; break; fi
  if ! jq -e '.type == "result" and ((.is_error | not) or ((.subtype // "") | test("max_turns|budget")))' "$usage" >/dev/null 2>&1; then
    why="$(jq -r '.result // .error // empty' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
    printf 'error\t%s\twrite\t%s\n' "$DOC" "${why:-claude exited without a result}"
    rm -f "$usage" "$usage.raw" "$usage.err"
    guard_app_after
    exit 125
  fi
  stamp_usage "$usage" "$AGENT" agentic
  step="$($STANGENT fsd write-check "$DOC" --usage "$usage" || true)"
  stop_if_no_envelope "$step"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
done
guard_app_after
rm -f "$usage" "$usage.raw" "$usage.err"
printf '%s\t%s\twrite\t%s\n' "$(jq -r 'if .ok then "written" else .error.code end' <<<"$step")" "$DOC" "$(jq -r '.result.summary // empty' <<<"$step")"
guard_mark_refused "$step" "$DOC write-check"
if jq -e '(.result.listed // []) | length > 0' <<<"$step" >/dev/null 2>&1; then printf '%s\n' "$DOC listed" >>"$GUARD_REFUSED"; fi
guard_finish 0
