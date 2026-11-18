#!/usr/bin/env bash
# Layer 5's FSD review through headless Claude Code (D341): for one imported document,
#   stangent fsd review <doc> --run N → stangent-fsd-reviewer (read-only search of a worktree of HEAD, capped)
#   → stangent fsd review-check <doc> --run N (one retry with the errors) — every run side by side —
#   then stangent fsd review-merge <doc> → specs/fsd/<doc>/review.json and review.md.
# The logic lives in `stangent fsd review*`; this only runs the agent each step names and hands its result back.
#
# usage: fsd-review-run.sh [--max-cost USD|none] [--stop-at-usage PCT] [--estimate] [--model M] [--effort E] [--again] <doc>
# --again runs every review again although the FSD and the commit are unchanged (a run otherwise stays as checked).
# The reviewer runs as the file finder does (D101): Read, Grep and Glob in a throwaway git worktree of the commit checked
# out (no .stangent/, no untracked or ignored files), never specs/ or stangent.yaml, its answer a file it writes in a throwaway folder (D401),
# under the turn and dollar cap `stangent fsd review` returns (fsd.reviewBudget). Outside git it searches the repo
# folder under the same rules. The conversation is kept under .stangent/fsd/<doc>/review/transcripts/.
# --max-cost (T.2): the hard stop, see cost-guard.sh. A run starts only if all of its own cap is left.
# Exit: 0 ok, 1 a run failed or was refused (the rest went on), 2 no cap chosen, 5 stopped on the cap or a limit, 125 anything else.
# env:   STANGENT (default: stangent), CLAUDE (default: claude), STANGENT_MAX_COST, STANGENT_STOP_AT_USAGE
# out:   one line per run: <status>\t<doc>\trun-<N>; then <status>\t<doc>\tmerge
set -euo pipefail

source "$(dirname "$0")/cost-guard.sh"
source "$(dirname "$0")/ai-run.sh"
TYPED="$(printf '%q ' "$0" "$@")"

usage() { sed -n 's/^# usage: //p' "$0" >&2; exit 2; }

MODEL=""
EFFORT=""
AGAIN=""
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
    --again) AGAIN=1; shift ;;
    -*) usage ;;
    *) [ -z "$DOC" ] || usage; DOC="$1"; shift ;;
  esac
done
[ -n "$DOC" ] || usage

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL EFFORT DOC
AGENT=stangent-fsd-reviewer

stop_if_no_envelope() {
  jq -e 'type == "object" and has("ok")' <<<"$1" >/dev/null 2>&1 && return 0
  printf 'error\t%s\trun-%s\tstangent gave no result\n' "$DOC" "$2"
  exit 255
}

# One reviewer call: the input file goes in on stdin (it may not read stangent's folder), it searches a throwaway git
# worktree of HEAD under stangent's limits with the repo folder swapped for the same folder in the worktree; it reads the
# FSD and writes its answer in a throwaway folder (D401), and the answer is copied where the step wants it.
review_search() {
  local step="$1" usage="$2" run="$3" out input limits transcript root prefix dir tmp="" io allowed=() denied=() f label rundir
  out="$(jq -r '.result.next.output' <<<"$step")"
  # D404: the reviewer, or the lister after it in the same run.
  local agent
  agent="$(jq -r '.result.next.agent // "stangent-fsd-reviewer"' <<<"$step")"
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  root="$(jq -r '.root' <<<"$limits")"
  dir="$PWD"
  if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    prefix="$(git rev-parse --show-prefix)"
    tmp="$(cd "$(mktemp -d)" && pwd -P)"
    # At the commit the run was prepared on, which the checks hold its evidence to.
    if ! git worktree add --detach -q "$tmp/repo" "$(jq -r '.commit // "HEAD"' <<<"$limits")" 2>"$tmp/error"; then
      printf 'error\t%s\trun-%s\tno worktree for the reviewer: %s\n' "$DOC" "$run" "$(head -c 300 "$tmp/error" | tr '\n' ' ')" >&2
      rm -rf "$tmp"; : >"$usage"; return 0
    fi
    trap 'git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"; trap - INT TERM' INT TERM
    dir="$tmp/repo/${prefix%/}"; dir="${dir%/}"
    if [ ! -d "$dir" ]; then
      printf 'error\t%s\trun-%s\tthe repo folder %s is not in what git tracks, so the reviewer has nothing to read\n' "$DOC" "$run" "${prefix:-.}" >&2
      git worktree remove --force "$tmp/repo" >/dev/null 2>&1; rm -rf "$tmp"; trap - INT TERM
      : >"$usage"; return 0
    fi
    # Rules name the folder as //<absolute path> (D89): the worktree's, from here on.
    limits="$(jq -c --arg root "$root" --arg wt "/$dir" 'walk(if type == "string" then (if startswith("Read(" + $root) then "Read(" + $wt + ltrimstr("Read(" + $root) else . end) else . end)' <<<"$limits")"
  fi
  while IFS= read -r f; do allowed+=("$f"); done < <(jq -r '.allowedTools[]' <<<"$limits")
  while IFS= read -r f; do denied+=("$f"); done < <(jq -r '.disallowedTools[]?' <<<"$limits")
  # D401: the FSD is a file the reviewer reads and its answer a file it writes, both in a throwaway folder outside the
  # repo it searches; it may read that folder and write only there.
  local fsdsrc
  fsdsrc="$(jq -r '.result.next.prompt' <<<"$step" | sed -n 's/^FSD: //p')"
  if [ -z "$fsdsrc" ] || [ ! -f "$fsdsrc" ]; then
    printf 'error\t%s\trun-%s\tno FSD file for the reviewer (%s); prepare the run again with --again\n' "$DOC" "$run" "${fsdsrc:-none named}" >&2
    [ -z "$tmp" ] || { git worktree remove --force "$tmp/repo" >/dev/null 2>&1; rm -rf "$tmp"; trap - INT TERM; }
    : >"$usage"; return 0
  fi
  io="$(cd "$(mktemp -d)" && pwd -P)"
  cp "$fsdsrc" "$io/fsd.md"
  # D403: code's checklist of screens and controls in scope, a file beside the FSD when the run has one.
  local listsrc
  listsrc="$(jq -r '.result.next.prompt' <<<"$step" | sed -n 's/^Checklist: //p')"
  if [ -n "$listsrc" ]; then
    if [ ! -f "$listsrc" ]; then
      printf 'error\t%s\trun-%s\tno checklist file for the reviewer (%s); prepare the run again with --again\n' "$DOC" "$run" "$listsrc" >&2
      [ -z "$tmp" ] || { git worktree remove --force "$tmp/repo" >/dev/null 2>&1; rm -rf "$tmp"; trap - INT TERM; }
      rm -rf "$io"; : >"$usage"; return 0
    fi
    cp "$listsrc" "$io/checklist.md"
  fi
  # It may read the folder and write only its answer.
  allowed+=("Read(/$io/**)" "Edit(/$io/output.json)")
  input=""
  while IFS=$'\t' read -r label f; do
    # The input is the prompt itself; a retry's previous attempt and what to fix follow under their own headings.
    if [ "$label" = "Input" ]; then input+="$(cat "$f")"$'\n\n'; else input+="# $label"$'\n\n'"$(cat "$f")"$'\n\n'; fi
  done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | grep -v '^FSD:' | grep -v '^Checklist:' | sed -E 's/^(.*): (\.stangent\/.*)$/\1\t\2/')
  input="${input//\{\{FSD_FILE\}\}/$io/fsd.md}"
  input="${input//\{\{OUTPUT_FILE\}\}/$io/output.json}"
  input="${input//\{\{CHECKLIST_FILE\}\}/$io/checklist.md}"
  mkdir -p ".stangent/fsd/$DOC/review/transcripts"
  transcript="$PWD/.stangent/fsd/$DOC/review/transcripts/run-$run-$(date +%Y%m%d-%H%M%S).jsonl"
  rm -f "$out"
  (
    # The worktree goes when this call ends; the answer folder stays for the copy below, and goes too on an interrupt.
    if [ -n "$tmp" ]; then trap 'git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"' EXIT; fi
    trap '[ -z "'"$tmp"'" ] || { git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"; }; rm -rf "'"$io"'"' INT TERM
    printf '%s' "$input" | (cd "$dir" && STANGENT_WRITE_DIR="$io" $CLAUDE -p --model "$AI_MODEL" --effort "$AI_EFFORT" --tools "$(jq -r '.tools' <<<"$limits")" --allowedTools "${allowed[@]}" \
      ${denied[@]+--disallowedTools "${denied[@]}"} --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" \
      --setting-sources "" --settings "$REVIEW_SETTINGS" --strict-mcp-config --disable-slash-commands --no-session-persistence \
      --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
      --output-format stream-json --verbose \
      --append-system-prompt "You are one of stangent's AI steps: the $agent. Your prompt holds your instructions, where the FSD (and for the lister, the checklist) is and where to write your answer, and on a retry your previous attempt and what to fix. The repo is your current folder. Write the JSON to the file your instructions name.") \
      >"$transcript" 2>"$usage.err" || true
  )
  cp "$transcript" "$usage.raw" 2>/dev/null || true
  [ -n "$tmp" ] && trap - INT TERM
  jq -c 'select(.type == "result")' "$transcript" 2>/dev/null | tail -n 1 >"$usage" || true
  # The answer is the file it wrote, also when it hit its own cap after writing it.
  [ -s "$io/output.json" ] && cp "$io/output.json" "$out"
  rm -rf "$io"
}

# One run to the end: the reviewer, then `review-check`, again on a retry. A reviewer that hit its own cap still goes to
# check, which asks for an answer once more and then records what passed.
run_review() {
  local run="$1" step usage action own why
  step="$($STANGENT fsd review "$DOC" --run "$run" ${AGAIN:+--again} || true)"
  stop_if_no_envelope "$step" "$run"
  usage="$(mktemp)"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" != "none" ]; do
    own="$(jq -r '.result.next.limits.maxBudgetUsd // empty' <<<"$step")"
    if ! guard_before_call "$own"; then
      guard_stopped_line "$DOC" | sed "s/\$/\trun-$run/"
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    review_search "$step" "$usage" "$run"
    if ! guard_after_call "$usage.raw" "$usage" "$usage.err"; then
      guard_stopped_line "$DOC" | sed "s/\$/\trun-$run/"
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    # Claude Code itself failed (an outage, no login): not the AI's fault, so nothing is recorded and the next run
    # starts again. Only a reviewer that hit its own turn or dollar cap goes on to `review-check`.
    if ! jq -e '.type == "result" and ((.is_error | not) or ((.subtype // "") | test("max_turns|budget")))' "$usage" >/dev/null 2>&1; then
      why="$(jq -r '.result // .error // empty' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
      printf 'error\t%s\trun-%s\t%s\n' "$DOC" "$run" "${why:-claude exited without a result}"
      rm -f "$usage" "$usage.raw" "$usage.err"
      exit 255
    fi
    stamp_usage "$usage" "$(jq -r '.result.next.agent // empty' <<<"$step" | grep . || echo "$AGENT")" agentic
    step="$($STANGENT fsd review-check "$DOC" --run "$run" --usage "$usage" || true)"
    stop_if_no_envelope "$step" "$run"
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
  rm -f "$usage" "$usage.raw" "$usage.err"
  printf '%s\t%s\trun-%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$DOC" "$run"
  guard_mark_refused "$step" "$DOC run-$run"
}
export -f run_review review_search stop_if_no_envelope
export -f guard_before_call guard_after_call guard_stop guard_stopped_line guard_lock guard_unlock guard_mark_refused stamp_usage claude_version
export AGAIN AGENT

# Reviewer worktrees an interrupted run left behind (their folders are gone): forget them.
git worktree prune 2>/dev/null || true

# How many runs, from stangent (fsd.reviewRuns); a document that isn't imported stops here, before any cost.
first="$($STANGENT fsd outline "$DOC" || true)"
if ! jq -e '.ok' <<<"$first" >/dev/null 2>&1; then
  printf 'error\t%s\toutline\t%s\n' "$DOC" "$(jq -r '.error.message // "no result"' <<<"$first" 2>/dev/null | head -c 300)" >&2
  exit 1
fi
# How many runs (fsd.reviewRuns), read only: nothing is prepared or cleared before the cost guard and --estimate.
status="$($STANGENT fsd review-status "$DOC" || true)"
RUNS="$(jq -r '.result.runs // empty' <<<"$status" 2>/dev/null || true)"
case "$RUNS" in ''|0|*[!0-9]*) echo "stangent fsd review-status gave no run count" >&2; exit 125 ;; esac
# D404: with an inventory each run is two calls, the review and the list.
PER="$(jq -r '.result.callsPerRun // 1' <<<"$status" 2>/dev/null || echo 1)"
case "$PER" in ''|*[!0-9]*) PER=1 ;; esac

guard_init "$MAXCOST" "$STOPUSAGE" "$RUNS" "$TYPED"
guard_estimate "fsd review-check" "$((RUNS * PER))"
[ -z "$ESTIMATE" ] || exit 0
[ ! -f "$STANGENT_COST_DIR/stopped" ] || guard_finish 0
# The model and effort (D161): none pinned for the reviewer is a stop, never a default.
if ! ai_settings "$AGENT"; then
  printf 'error\t%s\tsettings\t%s\n' "$DOC" "$AI_SETTINGS_ERROR"
  exit 125
fi
export AI_MODEL AI_EFFORT

export REVIEW_SETTINGS
REVIEW_SETTINGS="$(guard_read_only_settings '{"autoMemoryEnabled":false}')"
guard_app_before
rc=0
# Every run side by side (D341), each in its own worktree.
seq 1 "$RUNS" | xargs -n 1 -P "$RUNS" bash -c 'run_review "$1"' _ || rc=$?
guard_app_after
if [ "$rc" -eq 0 ]; then
  out="$($STANGENT fsd review-merge "$DOC" || true)"
  printf '%s\t%s\tmerge\n' "$(jq -r 'if .ok then "merged" else .error.code end' <<<"$out")" "$DOC"
  guard_mark_refused "$out" "$DOC merge"
fi
guard_finish "$rc"
