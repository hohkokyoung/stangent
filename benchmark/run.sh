#!/usr/bin/env bash
# Benchmark harness (decisions.md D6). For each pinned repo in repos.tsv: reset a clean
# checkout, run `stangent init`, run the given stangent command, and record exit code, wall
# time and telemetry totals in results/<UTC date>-<label>.json.
#
#   benchmark/run.sh <label> <stangent args...>     e.g.  benchmark/run.sh phase0 status
#
# STANGENT    stangent binary to run (default: stangent on PATH)
# BENCH_CACHE where clones live (default: benchmark/.cache, git-ignored)
# BENCH_ONLY  run only this repo name
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
label=${1:?usage: run.sh <label> <stangent args...>}; shift
[ $# -gt 0 ] || { echo "usage: run.sh <label> <stangent args...>" >&2; exit 2; }
stangent=${STANGENT:-stangent}
cache=${BENCH_CACHE:-$here/.cache}
out="$here/results/$(date -u +%Y-%m-%d)-$label.json"
mkdir -p "$cache" "$here/results"

now_ms() { perl -MTime::HiRes=time -e 'printf "%d\n", time*1000'; }
# grep exits 1 on no match; with pipefail that would end the run, so count through awk alone.
sum_attr() { cat "$2"/*.jsonl | awk -v key="\"$1\":" '{ while ((i = index($0, key)) > 0) { $0 = substr($0, i + length(key)); s += $0 + 0 } } END { print s + 0 }'; }

entries=()
while IFS=$'\t' read -r name url commit; do
  [[ -z "$name" || "$name" == \#* ]] && continue
  [[ -n "${BENCH_ONLY:-}" && "$name" != "$BENCH_ONLY" ]] && continue
  dir="$cache/$name"
  if [ ! -d "$dir/.git" ]; then git init -q "$dir"; git -C "$dir" remote add origin "$url"; fi
  if [ "$(git -C "$dir" rev-parse -q --verify HEAD 2>/dev/null)" != "$commit" ]; then
    git -C "$dir" fetch -q --depth 1 origin "$commit"
    git -C "$dir" checkout -q --force FETCH_HEAD
  fi
  git -C "$dir" reset -q --hard "$commit"
  git -C "$dir" clean -q -fdx

  telemetry=$(mktemp -d)
  STANGENT_TELEMETRY_DIR="$telemetry" "$stangent" init --repo "$dir" >/dev/null
  # A repo-specific stangent.yaml (detector settings) replaces the default when there is one.
  [ -f "$here/configs/$name.yaml" ] && cp "$here/configs/$name.yaml" "$dir/stangent.yaml"
  # Some repos pin a newer SDK than this machine or CI has; an override is recorded in the phase report.
  [ -f "$here/configs/$name.global.json" ] && cp "$here/configs/$name.global.json" "$dir/global.json"
  start=$(now_ms)
  set +e
  (cd "$dir" && STANGENT_TELEMETRY_DIR="$telemetry" "$stangent" "$@" >"$telemetry/output.json")
  code=$?
  set -e
  ms=$(( $(now_ms) - start ))
  events=$(cat "$telemetry"/*.jsonl | wc -l | tr -d ' ')
  errors=$(cat "$telemetry"/*.jsonl | awk '/"STATUS_CODE_ERROR"/ { n++ } END { print n + 0 }')
  entries+=("$(printf '{"repo":"%s","commit":"%s","exitCode":%d,"wallMs":%d,"telemetryEvents":%d,"errorEvents":%d,"inputTokens":%d,"outputTokens":%d}' \
    "$name" "$commit" "$code" "$ms" "$events" "$errors" \
    "$(sum_attr gen_ai.usage.input_tokens "$telemetry")" "$(sum_attr gen_ai.usage.output_tokens "$telemetry")")")
  echo "$name: exit $code, ${ms} ms" >&2
  rm -rf "$telemetry"
done < "$here/repos.tsv"

joined=$(IFS=,; echo "${entries[*]}")
printf '{"label":"%s","command":"%s","stangentVersion":"%s","results":[%s]}\n' \
  "$label" "$*" "$("$stangent" --version 2>/dev/null | head -1)" "$joined" > "$out"
echo "$out"
