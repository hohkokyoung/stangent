#!/usr/bin/env bash
# Runs stangent's AI steps through headless Claude Code (D60), for CI and long batch runs:
#   pages:    stangent spec prepare → stangent-page-writer → stangent spec check → stangent-page-verifier → …
#   sections: stangent fsd prepare → candidate finder / link judge / agree checker → stangent fsd check → …
# The logic lives in `stangent spec` and `stangent fsd`; this only runs the subagent each step names, then hands its
# result back.
#
# usage: spec-run.sh [--jobs N] [--model M] [--retry-failed] [--agents] (<page id>... | --all | --stale | --fsd <doc>)
# Default (D66): one turn per call. The step's input files go into the prompt, Claude Code runs with no tools and none of
# the user's settings, skills or MCP servers, and the answer comes back as structured output (--json-schema) that this
# script writes to the step's output file; `check` is unchanged. Far fewer tokens than a session that reads files.
# --agents: the plugin's subagents instead, each limited to reading and writing under .stangent/ (D60).
# env:   STANGENT (default: stangent), PLUGIN_DIR (default: this plugin), CLAUDE (default: claude)
# out:   one line per page or section: <status>\t<id>
set -euo pipefail

JOBS=1
MODE=spec
MODEL=""
AGENTS=""
RETRY=""
PAGES=()
while [ $# -gt 0 ]; do
  case "$1" in
    --jobs) JOBS="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --retry-failed) RETRY="--retry-failed"; shift ;;
    --agents) AGENTS=1; shift ;;
    --all) while IFS= read -r p; do PAGES+=("$p"); done < <(${STANGENT:-stangent} spec pages | jq -r '.result.pages[].id'); shift ;;
    --fsd) MODE=fsd; while IFS= read -r p; do PAGES+=("$p"); done < <(${STANGENT:-stangent} fsd sections --doc "$2" | jq -r '.result[] | .doc + "\t" + .section'); shift 2 ;;
    --stale) while IFS= read -r p; do PAGES+=("$p"); done < <(${STANGENT:-stangent} spec stale | jq -r '.result.stale[].id, .result.new[].id'); shift ;;
    *) PAGES+=("$1"); shift ;;
  esac
done

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL RETRY MODE AGENTS
export PLUGIN_DIR="${PLUGIN_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

# One call with no tools: every .stangent/ file the prompt names goes into the input, Claude Code enforces the output
# schema, and the structured answer is written where the step wants it. The input hash is the same in both modes.
single_turn() {
  local step="$1" usage="$2" out schema input label f
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  input=""
  while IFS=$'\t' read -r label f; do
    input+="# $label"$'\n\n'"$(cat "$f")"$'\n\n'
  done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | sed -E 's/^(.*): (\.stangent\/.*)$/\1\t\2/')
  rm -f "$out"
  printf '%s' "$input" | $CLAUDE -p ${MODEL:+--model "$MODEL"} --tools "" --setting-sources "" --disable-slash-commands \
    --strict-mcp-config --exclude-dynamic-system-prompt-sections --no-session-persistence --output-format json \
    --json-schema "$schema" \
    --system-prompt "You are one of stangent's AI steps. The input holds your instructions, the only evidence you may use, and on a retry your previous attempt and what to fix. Where the instructions say to write a result file, return that JSON as your structured output instead. Reply with nothing else." \
    >"$usage" 2>/dev/null || true
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

run_page() {
  local page="$1" step action agent prompt usage args
  usage="$(mktemp)"
  if [ "$MODE" = fsd ]; then args=("${page%%$'\t'*}" "${page#*$'\t'}"); else args=("$page"); fi
  # prepare/check exit 1 when a check fails; the next step is still in the envelope.
  if [ "$MODE" = fsd ]; then step="$($STANGENT fsd prepare "${args[@]}" || true)"; else step="$($STANGENT spec prepare "${args[@]}" $RETRY || true)"; fi
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" != "none" ]; do
    agent="$(jq -r '.result.next.agent' <<<"$step")"
    prompt="$(jq -r '.result.next.prompt' <<<"$step")"
    if [ -n "$AGENTS" ]; then
      $CLAUDE -p --plugin-dir "$PLUGIN_DIR" --agent "stangent:$agent" ${MODEL:+--model "$MODEL"} \
        --permission-mode dontAsk --allowedTools "Read(.stangent/**)" "Edit(.stangent/**)" --output-format json "$prompt" >"$usage" 2>/dev/null || true
    else
      single_turn "$step" "$usage"
    fi
    # Claude Code itself failed (a usage limit, an outage, no login): not the page's fault. Leave the page's run where
    # it is, report it, and stop the batch (exit 255 stops xargs); `prepare` starts it again on the next run.
    if ! jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
      printf 'error\t%s\t%s\n' "$page" "$(jq -r '.result // .error // "claude exited without a result"' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
      rm -f "$usage"
      exit 255
    fi
    step="$($STANGENT "$MODE" check "${args[@]}" --usage "$usage" || true)"
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
  rm -f "$usage"
  printf '%s\t%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$page"
}
export -f run_page single_turn

[ ${#PAGES[@]} -gt 0 ] || exit 0
printf '%s\0' "${PAGES[@]}" | xargs -0 -n 1 -P "$JOBS" bash -c 'run_page "$1"' _
