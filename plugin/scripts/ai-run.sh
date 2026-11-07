# Shared by spec-run.sh and ticket-run.sh (D161): every AI call runs with the model and effort code worked out for its step,
# says so in the result stangent records, and a one-turn call is kept exactly as sent so it can be repeated.
#
# Needs $STANGENT and $CLAUDE; reads the run's $MODEL and $EFFORT (--model, --effort), which win over stangent.yaml.

CLAUDE_VERSION=""

# Every step's settings, once, before the run's first call: a config pinning only some steps stops here, not halfway
# through a page with its first call paid for. Prints why and fails.
ai_settings_all() {
  local out
  out="$($STANGENT ai settings --all ${MODEL:+--model "$MODEL"} ${EFFORT:+--effort "$EFFORT"} 2>/dev/null || true)"
  if ! jq -e '.ok == true' <<<"$out" >/dev/null 2>&1; then
    AI_SETTINGS_ERROR="$(jq -r '.error.message // empty' <<<"$out" 2>/dev/null)"
    AI_SETTINGS_ERROR="${AI_SETTINGS_ERROR:-stangent ai settings --all gave no result}"
    return 1
  fi
}

# Sets AI_MODEL and AI_EFFORT for one step's agent. No settings pinned and no flag: prints why and fails; never a default.
ai_settings() {
  local agent="$1" out
  out="$($STANGENT ai settings "$agent" ${MODEL:+--model "$MODEL"} ${EFFORT:+--effort "$EFFORT"} 2>/dev/null || true)"
  if ! jq -e '.ok == true' <<<"$out" >/dev/null 2>&1; then
    AI_SETTINGS_ERROR="$(jq -r '.error.message // empty' <<<"$out" 2>/dev/null)"
    AI_SETTINGS_ERROR="${AI_SETTINGS_ERROR:-stangent ai settings $agent gave no result}"
    return 1
  fi
  AI_MODEL="$(jq -r '.result.model' <<<"$out")"
  AI_EFFORT="$(jq -r '.result.effort' <<<"$out")"
}

claude_version() {
  [ -n "$CLAUDE_VERSION" ] || CLAUDE_VERSION="$($CLAUDE --version </dev/null 2>/dev/null | head -n 1 | tr -d '\r')"
  printf '%s' "${CLAUDE_VERSION:-unknown}"
}

# Adds what the call ran with to Claude Code's result, so `check --usage` records it (D161). A result that isn't JSON is left.
stamp_usage() {
  local usage="$1" agent="$2" mode="$3" tmp
  [ -s "$usage" ] || return 0
  tmp="$usage.stamp"
  if jq -c --arg agent "$agent" --arg model "$AI_MODEL" --arg effort "$AI_EFFORT" --arg mode "$mode" --arg version "$(claude_version)" \
      'if type == "object" then .stangent_run = {agent: $agent, model: $model, effort: $effort, mode: $mode, claudeVersion: $version} else . end' \
      "$usage" >"$tmp" 2>/dev/null; then
    mv "$tmp" "$usage"
  else
    rm -f "$tmp"
  fi
}

call_hash() {
  if command -v shasum >/dev/null 2>&1; then shasum -a 256; else sha256sum; fi | cut -c1-16
}

# Keeps a one-turn call as sent: .stangent/calls/<id>/ holds input.txt (the exact bytes on stdin), schema.json and call.json
# (agent, schema name, target, model, effort, the driver's extra claude options). The id is the hash of all of it but the
# time and target, so the same call twice is one folder. $6 is the extra options as a JSON array of strings. Prints the id.
record_call() {
  local agent="$1" schema_name="$2" schema="$3" input="$4" target="$5" extra="$6" id dir
  id="$(printf '%s\n%s\n%s\n%s\n%s\n%s\n%s' "$agent" "$schema_name" "$AI_MODEL" "$AI_EFFORT" "$extra" "$schema" "$input" | call_hash)"
  dir=".stangent/calls/$id"
  mkdir -p "$dir"
  printf '%s' "$input" >"$dir/input.txt"
  printf '%s' "$schema" >"$dir/schema.json"
  jq -n --arg agent "$agent" --arg schema "$schema_name" --arg target "$target" --arg model "$AI_MODEL" --arg effort "$AI_EFFORT" \
    --argjson extra "$extra" --arg at "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    '{agent: $agent, schema: $schema, target: $target, model: $model, effort: $effort, extraArgs: $extra, recordedAt: $at}' >"$dir/call.json"
  printf '%s' "$id"
}

ONE_TURN_SYSTEM="You are one of stangent's AI steps. The input holds your instructions, the only evidence you may use, and on a retry your previous attempt and what to fix. Where the instructions say to write a result file, return that JSON as your structured output instead. Reply with nothing else."

# One call with no tools, the schema enforced, its input read from a file so the bytes sent are the bytes kept.
# $1 input file, $2 schema, $3 raw stream file, $4 stderr file, $5 usage (the result line), $6 the structured answer when it
# succeeded; then extra claude options.
one_turn_call() {
  local input="$1" schema="$2" raw="$3" err="$4" usage="$5" out="$6"; shift 6
  $CLAUDE -p --model "$AI_MODEL" --effort "$AI_EFFORT" ${CALL_BUDGET:+--max-budget-usd "$CALL_BUDGET"} --tools "" --setting-sources "" "$@" --disable-slash-commands \
    --strict-mcp-config --exclude-dynamic-system-prompt-sections --no-session-persistence --output-format stream-json --verbose \
    --json-schema "$schema" --system-prompt "$ONE_TURN_SYSTEM" <"$input" >"$raw" 2>"$err" || true
  jq -Rc 'fromjson? | select(type == "object" and .type == "result")' "$raw" 2>/dev/null | tail -n 1 >"$usage" || true
  rm -f "$out"
  if jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1; then
    jq -c '.structured_output // empty' "$usage" >"$out" || true
  fi
}

# A step's one-turn call, kept as sent (record_call) and then made. $1 step agent, $2 schema name, $3 schema, $4 input text,
# $5 target, $6 extra options (JSON array), $7 raw, $8 stderr, $9 usage, $10 answer. Leaves the answer beside the call too.
recorded_one_turn() {
  local id extra=() dir
  id="$(record_call "$1" "$2" "$3" "$4" "$5" "$6")"
  dir=".stangent/calls/$id"
  while IFS= read -r a; do extra+=("$a"); done < <(jq -r '.[]' <<<"$6")
  one_turn_call "$dir/input.txt" "$3" "$7" "$8" "$9" "${10}" ${extra[@]+"${extra[@]}"}
  [ ! -s "${10}" ] || cp "${10}" "$dir/answer.json"
}

# --repeat N --call <id>: the recorded call N more times, with exactly the input file, model, effort and extra options it was
# recorded with, its answers saved beside it, compared by code (`stangent ai compare`). Nothing in the project is recorded.
# One line per call:  repeat:steady|unsteady|failed <tab> <id> <tab> <agreed>/<items> | why
# Returns 0 for steady or unsteady, 1 when a call or the compare failed, 255 at the hard stop.
repeat_call() {
  local id="$1" n="$2" dir run i usage answers=() extra=() line failed=0 agent
  dir=".stangent/calls/$id"
  if [ ! -f "$dir/call.json" ] || [ ! -f "$dir/input.txt" ] || [ ! -f "$dir/schema.json" ]; then
    printf 'repeat:failed\t%s\tno recorded call\n' "$id"
    return 1
  fi
  AI_MODEL="$(jq -r '.model' "$dir/call.json")"
  AI_EFFORT="$(jq -r '.effort' "$dir/call.json")"
  agent="$(jq -r '.agent' "$dir/call.json")"
  while IFS= read -r a; do extra+=("$a"); done < <(jq -r '.extraArgs // [] | .[]' "$dir/call.json")
  run="$dir/repeat-$(date -u +%Y%m%d-%H%M%S)"
  mkdir -p "$run"
  for i in $(seq 1 "$n"); do
    usage="$run/$i.usage.json"
    if ! guard_before_call; then guard_stopped_line "repeat $id"; return 255; fi
    one_turn_call "$dir/input.txt" "$(cat "$dir/schema.json")" "$run/$i.raw" "$run/$i.err" "$usage" "$run/$i.json" ${extra[@]+"${extra[@]}"}
    if ! guard_after_call "$run/$i.raw" "$usage" "$run/$i.err"; then guard_stopped_line "repeat $id"; return 255; fi
    stamp_usage "$usage" "$agent" batch
    [ -s "$run/$i.json" ] || { : >"$run/$i.json"; failed=$((failed + 1)); }
    answers+=("$run/$i.json")
  done
  if [ "$failed" -gt 0 ]; then
    printf 'repeat:failed\t%s\t%s of %s calls gave no answer\n' "$id" "$failed" "$n"
    return 1
  fi
  line="$($STANGENT ai compare "${answers[@]}" --schema "$dir/schema.json" 2>/dev/null || true)"
  printf '%s\n' "$line" >"$run/compare.json"
  if ! jq -e '.ok == true' <<<"$line" >/dev/null 2>&1; then
    printf 'repeat:failed\t%s\tcompare gave no result\n' "$id"
    return 1
  fi
  printf 'repeat:%s\t%s\t%s/%s\n' "$(jq -r 'if .result.steady then "steady" else "unsteady" end' <<<"$line")" "$id" \
    "$(jq -r '.result.agreed' <<<"$line")" "$(jq -r '.result.decisions' <<<"$line")"
}

export -f ai_settings_all ai_settings claude_version stamp_usage call_hash record_call one_turn_call recorded_one_turn repeat_call
export ONE_TURN_SYSTEM
