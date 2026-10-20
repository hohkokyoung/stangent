#!/usr/bin/env bash
# Runs stangent's page steps through headless Claude Code (D60), for CI and long batch runs:
#   stangent spec prepare → stangent-page-writer → stangent spec check → stangent-page-verifier → stangent spec check …
# The logic lives in `stangent spec`; this only runs the subagent each step names, then hands its result back.
#
# usage: spec-run.sh [--jobs N] [--model M] [--retry-failed] (<page id>... | --all | --stale)
# env:   STANGENT (default: stangent), PLUGIN_DIR (default: this plugin), CLAUDE (default: claude)
# out:   one line per page: <status>\t<page id>
set -euo pipefail

JOBS=1
MODEL=""
RETRY=""
PAGES=()
while [ $# -gt 0 ]; do
  case "$1" in
    --jobs) JOBS="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --retry-failed) RETRY="--retry-failed"; shift ;;
    --all) while IFS= read -r p; do PAGES+=("$p"); done < <(${STANGENT:-stangent} spec pages | jq -r '.result.pages[].id'); shift ;;
    --stale) while IFS= read -r p; do PAGES+=("$p"); done < <(${STANGENT:-stangent} spec stale | jq -r '.result.stale[].id, .result.new[].id'); shift ;;
    *) PAGES+=("$1"); shift ;;
  esac
done

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL RETRY
export PLUGIN_DIR="${PLUGIN_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

run_page() {
  local page="$1" step action agent prompt usage
  usage="$(mktemp)"
  # prepare/check exit 1 when a check fails; the next step is still in the envelope.
  step="$($STANGENT spec prepare "$page" $RETRY || true)"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" != "none" ]; do
    agent="$(jq -r '.result.next.agent' <<<"$step")"
    prompt="$(jq -r '.result.next.prompt' <<<"$step")"
    $CLAUDE -p --plugin-dir "$PLUGIN_DIR" --agent "stangent:$agent" ${MODEL:+--model "$MODEL"} \
      --permission-mode acceptEdits --allowedTools Read Write --output-format json "$prompt" >"$usage" 2>/dev/null || true
    step="$($STANGENT spec check "$page" --usage "$usage" || true)"
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
  rm -f "$usage"
  printf '%s\t%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$page"
}
export -f run_page

[ ${#PAGES[@]} -gt 0 ] || exit 0
printf '%s\0' "${PAGES[@]}" | xargs -0 -n 1 -P "$JOBS" bash -c 'run_page "$1"' _
