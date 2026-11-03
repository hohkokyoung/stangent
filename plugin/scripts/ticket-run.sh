#!/usr/bin/env bash
# Runs Phase 3 for tickets through headless Claude Code (D60), for CI and batch runs such as the benchmark:
#   pull → points (→ stangent-file-finder, D101: searches the repo read-only) → bundle
#   [--match]      stangent ticket match → stangent-requirement-matcher → stangent-match-checker → ticket check --step match
#   [--prove]      bugs: stangent ticket prove → stangent-bug-reproducer (multi-turn, under limits) → ticket check --step prove
#   [--questions]  stangent ticket draft --kind questions → stangent-question-writer → ticket check --step questions
#   [--resolution <base..head>]  stangent ticket draft --kind resolution → stangent-summary-writer → check
# The logic lives in `stangent ticket`; this only runs the subagent each step names and hands its result back.
#
# usage: ticket-run.sh [--jobs N] [--model M] [--no-pull] [--refind] [--no-find] [--match] [--prove] [--hint TEXT] [--questions] [--resolution RANGE] [--tone T] <KEY>...
# --no-pull works from the ticket already pulled (a benchmark re-run, no Jira call). --refind searches for the ticket's
# files again although the ticket is unchanged (its answer is otherwise kept, marked stale once the code moves on).
# --no-find skips the file finder (a benchmark searches at each ticket's own commit later, in its `files` phase).
# One turn per call for the writers (D66, as spec-run.sh). The file finder is multi-turn and read-only (D101): Read, Grep
# and Glob in the repo only, its answer as structured output, under the limits `stangent ticket prepare` returns. The
# reproducer is multi-turn too (it builds and runs tests): the plugin's agent with the permission rules, turn and dollar
# caps `stangent ticket prove` returns (D81).
# env:   STANGENT (default: stangent), PLUGIN_DIR (default: this plugin), CLAUDE (default: claude)
# out:   one line per ticket and step: <status>\t<key>\t<step>
set -euo pipefail

usage() { sed -n 's/^# usage: //p' "$0" >&2; exit 2; }

JOBS=1
MODEL=""
PROVE=""
MATCH=""
HINT=""
QUESTIONS=""
NOPULL=""
REFIND=""
NOFIND=""
RESOLUTION=""
TONE=""
KEYS=()
while [ $# -gt 0 ]; do
  case "$1" in --jobs|--model|--hint|--resolution|--tone) [ $# -ge 2 ] || usage ;; esac
  case "$1" in
    --jobs) JOBS="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --prove) PROVE=1; shift ;;
    --match) MATCH=1; shift ;;
    --no-pull) NOPULL=1; shift ;;
    --refind) REFIND=1; shift ;;
    --no-find) NOFIND=1; shift ;;
    --hint) HINT="$2"; shift 2 ;;
    --questions) QUESTIONS=1; shift ;;
    --resolution) RESOLUTION="$2"; shift 2 ;;
    --tone) TONE="$2"; shift 2 ;;
    -*) usage ;;
    *) KEYS+=("$1"); shift ;;
  esac
done
[ ${#KEYS[@]} -gt 0 ] || usage
case "$JOBS" in ''|0|*[!0-9]*) usage ;; esac

export STANGENT="${STANGENT:-stangent}" CLAUDE="${CLAUDE:-claude}" MODEL PROVE MATCH HINT QUESTIONS RESOLUTION TONE NOPULL REFIND NOFIND
export PLUGIN_DIR="${PLUGIN_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"

# One call with no tools (D66): the step's input files go into the prompt, the output schema is enforced, and the
# structured answer is written where the step wants it. Every call here has auto memory off: no AI step may see notes an
# earlier session left about this repo (a benchmark clone's could hold its fixes).
single_turn() {
  local step="$1" usage="$2" out schema input label f
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  input=""
  while IFS=$'\t' read -r label f; do
    input+="# $label"$'\n\n'"$(cat "$f")"$'\n\n'
  done < <(jq -r '.result.next.prompt' <<<"$step" | grep -E ': \.stangent/' | grep -v '^Write' | sed -E 's/^(.*): (\.stangent\/.*)$/\1\t\2/')
  rm -f "$out"
  printf '%s' "$input" | $CLAUDE -p ${MODEL:+--model "$MODEL"} --tools "" --setting-sources "" --settings '{"autoMemoryEnabled":false}' --disable-slash-commands \
    --strict-mcp-config --exclude-dynamic-system-prompt-sections --no-session-persistence --output-format json \
    --json-schema "$schema" \
    --system-prompt "You are one of stangent's AI steps. The input holds your instructions, the only evidence you may use, and on a retry your previous attempt and what to fix. Where the instructions say to write a result file, return that JSON as your structured output instead. Reply with nothing else." \
    >"$usage" 2>/dev/null || true
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

# The file finder (D101): the step's input files go in on stdin (it may not read stangent's folder), it searches a
# throwaway git worktree of the commit checked out (no .stangent/, specs, build output or untracked files) under
# stangent's limits with the repo folder swapped for the same folder in the worktree, and its structured answer is
# written where the step wants it. In a git repo whose worktree can't be made it stops (exit 255) rather than search
# the repo itself; outside git it searches the repo under the same rules. The conversation is kept.
agent_search() {
  local step="$1" usage="$2" key="$3" out schema input label f limits transcript root prefix dir tmp="" allowed=() denied=()
  out="$(jq -r '.result.next.output' <<<"$step")"
  schema="$($STANGENT spec schema "$(jq -r '.result.next.schema' <<<"$step")")"
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  root="$(jq -r '.root' <<<"$limits")"
  dir="$PWD"
  if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    prefix="$(git rev-parse --show-prefix)"
    tmp="$(cd "$(mktemp -d)" && pwd -P)"
    if ! git worktree add --detach -q "$tmp/repo" HEAD 2>"$tmp/error"; then
      printf 'error\t%s\tpoints\tno worktree for the file finder: %s\n' "$key" "$(head -c 300 "$tmp/error" | tr '\n' ' ')" >&2
      rm -rf "$tmp"
      : >"$usage"
      return 0
    fi
    # From here on, however this ends, the worktree goes (the claude call below has its own trap too).
    trap 'git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"; trap - INT TERM' INT TERM
    # Stangent's folder may be a subfolder of the git repo: the same subfolder in the worktree.
    dir="$tmp/repo/${prefix%/}"; dir="${dir%/}"
    if [ ! -d "$dir" ]; then
      printf 'error\t%s\tpoints\tthe repo folder %s is not in what git tracks, so the file finder has nothing to search\n' "$key" "${prefix:-.}" >&2
      git worktree remove --force "$tmp/repo" >/dev/null 2>&1; rm -rf "$tmp"; trap - INT TERM
      : >"$usage"
      return 0
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
  # Beside the step's work folder, which a new run clears: every attempt's conversation is kept.
  mkdir -p ".stangent/tickets/$key/transcripts"
  transcript="$PWD/.stangent/tickets/$key/transcripts/points-$(date +%Y%m%d-%H%M%S).jsonl"
  rm -f "$out"
  # In a subshell with its own trap: the worktree goes however this ends (Ctrl+C, a timeout, a kill).
  (
    if [ -n "$tmp" ]; then trap 'git worktree remove --force "'"$tmp"'/repo" >/dev/null 2>&1; rm -rf "'"$tmp"'"' EXIT INT TERM; fi
    printf '%s' "$input" | (cd "$dir" && $CLAUDE -p ${MODEL:+--model "$MODEL"} --tools "$(jq -r '.tools' <<<"$limits")" --allowedTools "${allowed[@]}" \
      ${denied[@]+--disallowedTools "${denied[@]}"} --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" \
      --setting-sources "" --settings '{"autoMemoryEnabled":false}' --strict-mcp-config --disable-slash-commands --no-session-persistence \
      --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
      --json-schema "$schema" --output-format stream-json --verbose \
      --append-system-prompt "You are one of stangent's AI steps: the stangent-file-finder. Your prompt holds your instructions, the ticket and your output schema, and on a retry your previous attempt and what to fix. The repo is your current folder. Return the JSON as your structured output.") \
      >"$transcript" 2>/dev/null || true
  )
  [ -n "$tmp" ] && trap - INT TERM
  jq -c 'select(.type == "result")' "$transcript" 2>/dev/null | tail -n 1 >"$usage" || true
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

# The reproducer: the plugin's agent, allowed only the tools and commands stangent named, with its caps.
multi_turn() {
  local step="$1" usage="$2" key="$3" limits prompt agent transcript serve_pid="" mcp=() tools=()
  # The whole conversation is kept beside the attempt, to see where its turns went; the result line is the usage.
  transcript=".stangent/tickets/$key/prove/transcript-$(date +%Y%m%d-%H%M%S).jsonl"
  mkdir -p "$(dirname "$transcript")"
  limits="$(jq -c '.result.next.limits' <<<"$step")"
  prompt="$(jq -r '.result.next.prompt' <<<"$step")"
  agent="$(jq -r '.result.next.agent' <<<"$step")"
  while IFS= read -r t; do tools+=("$t"); done < <(jq -r '.allowedTools[]' <<<"$limits")
  # Playwright (D86): the app runs while the reproducer works; `ticket serve` also runs the related specs once.
  if [ "$(jq -r '.app.serve // false' <<<"$limits")" = true ]; then
    local ready=".stangent/tickets/$key/tools/app-ready.json" waited=0
    rm -f "$ready"
    $STANGENT ticket serve "$key" >".stangent/tickets/$key/tools/serve.log" 2>&1 &
    serve_pid=$!
    # However this ends (Ctrl+C, an error, the batch stopping), the app doesn't outlive the attempt.
    trap 'kill -TERM "$serve_pid" 2>/dev/null; wait "$serve_pid" 2>/dev/null' EXIT INT TERM
    while [ ! -f "$ready" ] && kill -0 "$serve_pid" 2>/dev/null && [ "$waited" -lt 1800 ]; do sleep 2; waited=$((waited + 2)); done
    if [ ! -f "$ready" ]; then
      # Not the AI's fault: no attempt is made (its usage file stays empty, which stops the batch like a Claude failure).
      printf 'error\t%s\tprove\tthe app did not start for the attempt: %s\n' "$key" "$(tail -c 300 ".stangent/tickets/$key/tools/serve.log" | tr '\n' ' ')" >&2
      kill -TERM "$serve_pid" 2>/dev/null; wait "$serve_pid" 2>/dev/null; trap - EXIT INT TERM
      : >"$usage"
      return 0
    fi
    if [ "$(jq -r '.app.browser // false' <<<"$limits")" = true ]; then
      local cfg=".stangent/tickets/$key/tools/mcp.json" exe out
      # The Chromium the repo's own Playwright installed, so the browser server needs no download of its own.
      exe="$(cd "$(jq -r '.app.playwrightDir' <<<"$limits")" && node -e "console.log(require('@playwright/test').chromium.executablePath())" 2>/dev/null || true)"
      out="$PWD/.stangent/tickets/$key/tools/browser"
      jq -n --arg url "$(jq -r '.app.url' <<<"$limits")" --arg exe "$exe" --arg out "$out" \
        '{mcpServers: {playwright: {command: "npx", args: (["-y", "@playwright/mcp@0.0.83", "--headless", "--isolated", "--allowed-origins", $url, "--output-dir", $out]
          + (if $exe == "" then [] else ["--executable-path", $exe] end))}}}' >"$cfg"
      mcp=(--mcp-config "$cfg")
    fi
  fi
  # No user or project settings (their allow rules would widen the reproducer's), no MCP but the browser given here.
  # Each command starts back in the repo (D89): a `cd src` mustn't make the repo-relative commands it may run miss.
  CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 $CLAUDE -p --plugin-dir "$PLUGIN_DIR" --agent "stangent:$agent" ${MODEL:+--model "$MODEL"} ${mcp[@]+"${mcp[@]}"} \
    --setting-sources "" --settings '{"autoMemoryEnabled":false}' --strict-mcp-config \
    --permission-mode "$(jq -r '.permissionMode' <<<"$limits")" --allowedTools "${tools[@]}" \
    --max-turns "$(jq -r '.maxTurns' <<<"$limits")" --max-budget-usd "$(jq -r '.maxBudgetUsd' <<<"$limits")" \
    --no-session-persistence --output-format stream-json --verbose "$prompt" >"$transcript" 2>/dev/null || true
  # D104: the rounds `--max-turns` limits, one per model message. The result's `num_turns` also counts parallel tool calls.
  rounds="$(jq -r 'select(.type == "assistant") | .message.id' "$transcript" 2>/dev/null | sort -u | wc -l | tr -d ' ')" || true
  jq -c --argjson rounds "${rounds:-0}" 'select(.type == "result") | . + {rounds: $rounds}' "$transcript" 2>/dev/null | tail -n 1 >"$usage" || true
  # Stop the app before stangent's own check starts it again on the same port.
  if [ -n "$serve_pid" ]; then kill -TERM "$serve_pid" 2>/dev/null || true; wait "$serve_pid" 2>/dev/null || true; trap - EXIT INT TERM; fi
}

# stangent crashed: no envelope, so no next step to read. Without this the loop below would call Claude on nothing, forever.
stop_if_no_envelope() {
  jq -e 'type == "object" and has("ok")' <<<"$1" >/dev/null 2>&1 && return 0
  printf 'error\t%s\t%s\tstangent gave no result\n' "$2" "$3"
  rm -f "$4"
  exit 255
}

# Runs one AI step to the end: the subagent, then `ticket check`, again on a retry. A capped reproducer still has a
# result: `check` records it as not reproduced (D81).
run_step() {
  local key="$1" name="$2" step="$3" usage action first
  usage="$(mktemp)"
  first="$step"
  stop_if_no_envelope "$step" "$key" "$name" "$usage"
  action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  while [ "$action" != "none" ]; do
    if [ "$name" = prove ]; then multi_turn "$first" "$usage" "$key"
    elif [ "$name" = points ]; then agent_search "$step" "$usage" "$key"
    else single_turn "$step" "$usage"; fi
    # Claude Code itself failed (a usage limit, an outage, no login): not the AI's fault, so nothing is recorded and
    # the next run starts the step again. Only a reproducer or file finder that hit its own turn or dollar cap goes on to `check`.
    if ! jq -e '.type == "result" and ((.is_error | not) or ((.subtype // "") | test("max_turns|budget")))' "$usage" >/dev/null 2>&1; then
      local why
      why="$(jq -r '.result // .error // empty' "$usage" 2>/dev/null | head -c 200 | tr '\n' ' ')"
      printf 'error\t%s\t%s\t%s\n' "$key" "$name" "${why:-claude exited without a result}"
      rm -f "$usage"
      exit 255
    fi
    step="$($STANGENT ticket check "$key" --step "$name" --usage "$usage" || true)"
    stop_if_no_envelope "$step" "$key" "$name" "$usage"
    # A reproducer's retry runs under what's left of the attempt's cap: the check returns the new limits and prompt.
    [ "$name" = prove ] && first="$step"
    action="$(jq -r '.result.next.action // "none"' <<<"$step")"
  done
  rm -f "$usage"
  printf '%s\t%s\t%s\n' "$(jq -r '.result.status // .error.code' <<<"$step")" "$key" "$name"
  # D89: stangent's own permission rules refused what they allow. Every later attempt would hit the same wall: stop.
  if [ "$(jq -r '.error.code // ""' <<<"$step")" = setup_blocked ]; then
    printf 'error\t%s\t%s\t%s\n' "$key" "$name" "$(jq -r '.error.message // ""' <<<"$step" | head -c 300)" >&2
    exit 255
  fi
}

run_ticket() {
  local key="$1" out
  if [ -z "$NOPULL" ]; then
    out="$($STANGENT ticket pull "$key" || true)"
    printf '%s\t%s\tpull\n' "$(jq -r 'if .ok then "pulled" else .error.code end' <<<"$out")" "$key"
    # A failed pull leaves nothing to work on; say why and fail, so a batch sees it (D92: a draft was lost silently).
    if ! jq -e '.ok' <<<"$out" >/dev/null 2>&1; then
      printf 'error\t%s\tpull\t%s\n' "$key" "$(jq -r '.error.message // "no result"' <<<"$out" 2>/dev/null | head -c 300)" >&2
      return 1
    fi
  fi
  out="$($STANGENT ticket points "$key" ${REFIND:+--refind} || true)"
  if [ -z "$NOFIND" ] && [ "$(jq -r '.result.ai.status // ""' <<<"$out")" = needed ]; then
    run_step "$key" points "$($STANGENT ticket prepare "$key" --step points || true)"
  fi
  out="$($STANGENT ticket bundle "$key" || true)"
  printf '%s\t%s\tbundle\n' "$(jq -r 'if .ok then "built" else .error.code end' <<<"$out")" "$key"
  # No bundle, nothing for the paid steps below to work from: fail, as a failed pull does (D92).
  if ! jq -e '.ok' <<<"$out" >/dev/null 2>&1; then
    printf 'error\t%s\tbundle\t%s\n' "$key" "$(jq -r '.error.message // "no result"' <<<"$out" 2>/dev/null | head -c 300)" >&2
    return 1
  fi
  if [ -n "$MATCH" ]; then
    run_step "$key" match "$($STANGENT ticket match "$key" || true)"
  fi
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
export -f run_ticket run_step single_turn multi_turn agent_search stop_if_no_envelope

# File finder worktrees an interrupted run left behind (their folders are gone): forget them.
git worktree prune 2>/dev/null || true
# A Playwright proof keeps the app on its one port for the whole attempt: proofs run one at a time (D86).
if [ -n "$PROVE" ] && [ "$JOBS" -gt 1 ]; then echo "note: --prove runs one ticket at a time (the app's port)" >&2; JOBS=1; fi
printf '%s\0' "${KEYS[@]}" | xargs -0 -n 1 -P "$JOBS" bash -c 'run_ticket "$1"' _
