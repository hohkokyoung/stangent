#!/usr/bin/env bash
# Runs Phase 3 for tickets through headless Claude Code (D60), for CI and batch runs such as the benchmark:
#   pull → points (→ stangent-point-picker when code finds none) → bundle
#   [--prove]      bugs: stangent ticket prove → stangent-bug-reproducer (multi-turn, under limits) → ticket check --step prove
#   [--questions]  stangent ticket draft --kind questions → stangent-question-writer → ticket check --step questions
#   [--resolution <base..head>]  stangent ticket draft --kind resolution → stangent-summary-writer → check
# The logic lives in `stangent ticket`; this only runs the subagent each step names and hands its result back.
#
# usage: ticket-run.sh [--jobs N] [--model M] [--prove] [--hint TEXT] [--questions] [--resolution RANGE] [--tone T] <KEY>...
# One turn per call for the picker and the writers (D66, as spec-run.sh). The reproducer is multi-turn (it builds and runs
# tests): the plugin's agent with the permission rules, turn and dollar caps `stangent ticket prove` returns (D81).
# env:   STANGENT (default: stangent), PLUGIN_DIR (default: this plugin), CLAUDE (default: claude)
# out:   one line per ticket and step: <status>\t<key>\t<step>
set -euo pipefail

JOBS=1
MODEL=""
PROVE=""
HINT=""
QUESTIONS=""
RESOLUTION=""
TONE=""
KEYS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --jobs) JOBS="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --prove) PROVE=1; shift ;;
    --hint) HINT="$2"; shift 2 ;;
    --questions) QUESTIONS=1; shift ;;
    --resolution) RESOLUTION="$2"; shift 2 ;;
    --tone) TONE="$2"; shift 2 ;;
    *) KEYS+=("$1"); shift ;;
  esac
done

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL PROVE HINT QUESTIONS RESOLUTION TONE
export PLUGIN_DIR="${PLUGIN_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

# One call with no tools (D66): the step's input files go into the prompt, the output schema is enforced, and the
# structured answer is written where the step wants it.
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

# The reproducer: the plugin's agent, allowed only the tools and commands stangent named, with its caps.
multi_turn() {
  local step="$1" usage="$2" key="$3" limits prompt agent transcript tools=()
  # The whole conversation is kept beside the attempt, to see where its turns went; the result line is the usage.
  transcript=".stangent/tickets/$key/prove/transcript-$(date +%Y%m%d-%H%M%S).jsonl"
  mkdir -p "$(dirname "$transcript")"
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  prompt="$(jq -r '.result.next.prompt' <<<"$step")"
  agent="$(jq -r '.result.next.agent' <<<"$step")"
  while IFS= read -r t; do tools+=("$t"); done < <(jq -r '.allowedTools[]' <<<"$limits")
  $CLAUDE -p --plugin-dir "$PLUGIN_DIR" --agent "stangent:$agent" ${MODEL:+--model "$MODEL"} \
    --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" --allowedTools "${tools[@]}" \
    --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
    --no-session-persistence --output-format stream-json --verbose "$prompt" >"$transcript" 2>/dev/null || true
  jq -c 'select(.type == "result")' "$transcript" 2>/dev/null | tail -n 1 >"$usage" || true
}

# Runs one AI step to the end: the subagent, then `ticket check`, again on a retry. A capped reproducer still has a
# result: `check` records it as not reproduced (D81).
run_step() {
  local key="$1" name="$2" step="$3" usage action first
  usage="$(mktemp)"
  first="$step"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" != "none" ]; do
    if [ "$name" = prove ]; then multi_turn "$first" "$usage" "$key"; else single_turn "$step" "$usage"; fi
    # Claude Code itself failed (a usage limit, an outage, no login): not the AI's fault, so nothing is recorded and
    # the next run starts the step again. Only a reproducer that hit its own turn or dollar cap goes on to `check`.
    if ! jq -e '.type == "result" and ((.is_error | not) or ((.subtype // "") | test("max_turns|budget")))' "$usage" >/dev/null 2>&1; then
      printf 'error\t%s\t%s\t%s\n' "$key" "$name" "$(jq -r '.result // .error // "claude exited without a result"' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
      rm -f "$usage"
      exit 255
    fi
    step="$($STANGENT ticket check "$key" --step "$name" --usage "$usage" || true)"
    # A reproducer's retry runs under what's left of the attempt's cap: the check returns the new limits and prompt.
    [ "$name" = prove ] && first="$step"
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
  rm -f "$usage"
  printf '%s\t%s\t%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$key" "$name"
}

run_ticket() {
  local key="$1" out
  out="$($STANGENT ticket pull "$key" || true)"
  printf '%s\t%s\tpull\n' "$(jq -r 'if .ok then "pulled" else .error.code end' <<<"$out")" "$key"
  jq -e '.ok' <<<"$out" >/dev/null || return 0
  out="$($STANGENT ticket points "$key" || true)"
  if [ "$(jq -r '.result.ai.status // ""' <<<"$out")" = needed ]; then
    run_step "$key" points "$($STANGENT ticket prepare "$key" --step points || true)"
  fi
  out="$($STANGENT ticket bundle "$key" || true)"
  printf '%s\t%s\tbundle\n' "$(jq -r 'if .ok then "built" else .error.code end' <<<"$out")" "$key"
  if [ -n "$PROVE" ] && [ "$(jq -r '.isBug' ".stangent/tickets/$key/ticket.json" 2>/dev/null)" = true ]; then
    run_step "$key" prove "$($STANGENT ticket prove "$key" ${HINT:+--hint "$HINT"} || true)"
  fi
  if [ -n "$QUESTIONS" ]; then
    run_step "$key" questions "$($STANGENT ticket draft "$key" --kind questions ${TONE:+--tone "$TONE"} || true)"
  fi
  if [ -n "$RESOLUTION" ]; then
    run_step "$key" resolution "$($STANGENT ticket draft "$key" --kind resolution --pr "$RESOLUTION" ${TONE:+--tone "$TONE"} || true)"
  fi
}
export -f run_ticket run_step single_turn multi_turn

[ ${#KEYS[@]} -gt 0 ] || exit 0
printf '%s\0' "${KEYS[@]}" | xargs -0 -n 1 -P "$JOBS" bash -c 'run_ticket "$1"' _
