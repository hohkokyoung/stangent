#!/usr/bin/env bash
# Phase 3's run on the frozen set (D77–D81), in the order D79 fixes. Each phase can run on its own and resumes:
#   points    pull (live, through the MCP), starting points (point picker when code finds none) and a first bundle, all 50
#   pages     generate the picked pages that have no written version (spec-run.sh), reported apart
#   bundles   build all 50 bundles again, now with those pages
#   drafts    a questions draft for all 50; a resolution draft for each bug, from its fix commit (standing in for the PR)
#   prove     the 30 newest bugs and the 10 not-bugs, each on its own commit (bug: the fix's parent; not-bug: the commit
#             before it was closed); a reproduction is then run on the fix commit (`ticket prove --verify`): "proof is real"
#   replay    pull, points and bundle again from the saved MCP results: byte-identical bundles, and no AI call
# AI cost (D81, worst case): prove ≈ $40 (40 attempts × $1 cap), points + drafts ≈ $5, pages ≈ $10–20.
#
# usage: run.sh <clone> <tickets dir> <phase>... [--model M] [--jobs N] [--only KEY,...]
# env:   STANGENT (default: the stangent CLI), PLUGIN_DIR (default: this repo's plugin)
# out:   <tickets dir>/run/<KEY>.json per ticket (times, replay checks) and run.log lines
set -euo pipefail

CLONE="$(cd "$1" && pwd)"; TICKETS="$(cd "$2" && pwd)"; shift 2
PHASES=(); MODEL=""; JOBS=1; ONLY=""
while [ $# -gt 0 ]; do
  case "$1" in
    --model) MODEL="$2"; shift 2 ;;
    --jobs) JOBS="$2"; shift 2 ;;
    --only) ONLY="$2"; shift 2 ;;
    *) PHASES+=("$1"); shift ;;
  esac
done
HERE="$(cd "$(dirname "$0")/../.." && pwd)"
export STANGENT="${STANGENT:-dotnet $HOME/stangent-bench/stangent-bin/stangent.dll}"
export PLUGIN_DIR="${PLUGIN_DIR:-$HERE/plugin}"
SDK="$HERE/benchmark/configs/sonarr.global.json"
mkdir -p "$TICKETS/run"
cd "$CLONE"
PINNED="$(jq -r .pinned "$TICKETS/selection.json")"
st() { $STANGENT "$@"; }
log() { printf '%s\t%s\n' "$(date -u +%H:%M:%S)" "$*" | tee -a "$TICKETS/run/run.log" >&2; }

# Issues as "number kind runCommit key", newest first (selection order); --only limits to some keys.
issues() {
  jq -r --slurpfile k "$TICKETS/jira-keys.json" '.issues[] | "\(.issue) \(.kind) \(.runCommit) \($k[0][(.issue|tostring)] // "")"' "$TICKETS/selection.json" \
    | while read -r n kind run key; do
        [ -n "$key" ] || { log "no Jira key for #$n: run --copy-to-jira first"; exit 1; }
        if [ -z "$ONLY" ] || [[ ",$ONLY," == *",$key,"* ]]; then echo "$n $kind $run $key"; fi
      done
}

# A per-ticket record: <phase>.ms and other fields merged into run/<KEY>.json.
record() {
  local key="$1" filter="$2" f="$TICKETS/run/$1.json"
  [ -f "$f" ] || echo '{}' >"$f"
  jq "$filter" "$f" >"$f.tmp" && mv "$f.tmp" "$f"
}
now_ms() { python3 -c 'import time; print(int(time.time() * 1000))'; }   # macOS date has no milliseconds
timed() { local key="$1" step="$2"; shift 2; local t0 t1; t0=$(now_ms); "$@" || true; t1=$(now_ms); record "$key" ".steps.\"$step\".ms = $((t1 - t0))"; }

# Back to the pinned commit with the SDK pin (D28's approach: past issues are judged against today's graph and pages).
at() {
  git checkout -q -f "$1"
  # Each attempt starts clean: an earlier attempt's test (untracked) would be read and copied by the next one.
  # Ignored files (bin/, obj/, node_modules/) stay. A test to keep is copied back after this (the fix commit's check).
  git clean -fdq -- src frontend
  find e2e/stangent -type f -delete 2>/dev/null || true
  cp "$SDK" global.json
}

phase_points() {
  at "$PINNED"
  issues | while read -r n kind run key; do
    log "points $key (#$n)"
    timed "$key" points "$HERE/plugin/scripts/ticket-run.sh" ${MODEL:+--model "$MODEL"} "$key" >>"$TICKETS/run/run.log"
  done
}

phase_pages() {
  at "$PINNED"
  local pages
  pages="$(issues | while read -r n kind run key; do jq -r '.missingPages[]?' ".stangent/tickets/$key/bundle.json" 2>/dev/null; done | sort -u)"
  log "pages: $(echo "$pages" | grep -c . || true) to generate"
  [ -z "$pages" ] && return 0
  # shellcheck disable=SC2086
  STANGENT="$STANGENT" "$HERE/plugin/scripts/spec-run.sh" --jobs "$JOBS" ${MODEL:+--model "$MODEL"} $pages | tee -a "$TICKETS/run/pages.log"
}

phase_bundles() {
  at "$PINNED"
  issues | while read -r n kind run key; do
    log "bundle $key"
    timed "$key" bundle st ticket bundle "$key" >/dev/null
  done
}

phase_drafts() {
  at "$PINNED"
  issues | while read -r n kind run key; do
    log "questions $key"
    timed "$key" questions "$HERE/plugin/scripts/ticket-run.sh" ${MODEL:+--model "$MODEL"} --questions "$key" >>"$TICKETS/run/run.log"
    if [ "$kind" = bug ]; then
      local fix parent
      fix="$(jq -r --arg n "$n" '.issues[$n].fixCommit' "$TICKETS/answer-key.json")"
      parent="$(jq -r --arg n "$n" '.issues[$n].parent' "$TICKETS/answer-key.json")"
      log "resolution $key ($parent..$fix)"
      timed "$key" resolution "$HERE/plugin/scripts/ticket-run.sh" ${MODEL:+--model "$MODEL"} --resolution "$parent..$fix" "$key" >>"$TICKETS/run/run.log"
    fi
  done
}

phase_prove() {
  # The 30 newest bugs and every not-bug (D81).
  issues | awk '$2 == "bug" { if (++b <= 30) print; next } { print }' | while read -r n kind run key; do
    if [ -f ".stangent/tickets/$key/bug-proof.json" ] && [ "$(jq -r '.outcome' ".stangent/tickets/$key/bug-proof.json")" != failed ]; then
      # Its bundle may still be the one built at the bug's commit by an earlier run: rebuild it at the pinned one.
      at "$PINNED"; st ticket points "$key" >/dev/null || true; st ticket bundle "$key" >/dev/null || true
      log "prove $key: already recorded"; continue
    fi
    log "prove $key (#$n, $kind) at ${run:0:10}"
    at "$run"
    timed "$key" prove "$HERE/plugin/scripts/ticket-run.sh" ${MODEL:+--model "$MODEL"} --prove "$key" >>"$TICKETS/run/run.log"
    local proof outcome file
    proof=".stangent/tickets/$key/bug-proof.json"
    outcome="$(jq -r '.outcome // "none"' "$proof" 2>/dev/null || echo none)"
    record "$key" ".proofOutcome = \"$outcome\""
    if [ "$outcome" = reproduced ] && [ "$kind" = bug ]; then
      # "Proof is real" (D79): the same test on the fix commit must pass.
      file="$(jq -r '.attempts[-1].testFile' "$proof")"
      mkdir -p ".stangent/tickets/$key/prove/kept"; cp "$file" ".stangent/tickets/$key/prove/kept/" 2>/dev/null || true
      at "$(jq -r --arg n "$n" '.issues[$n].fixCommit' "$TICKETS/answer-key.json")"
      mkdir -p "$(dirname "$file")"; cp ".stangent/tickets/$key/prove/kept/$(basename "$file")" "$file"
      st ticket prove "$key" --verify >/dev/null || true
      record "$key" ".proofOnFix = ($(jq '.verification.runs[0].result // "error"' "$proof"))"
      log "  on the fix: $(jq -r '.verification.runs[0].result // "error"' "$proof")"
    fi
    at "$PINNED"
    # ticket-run.sh rebuilt points and the bundle at the bug's own commit; build them again at the pinned one, so the
    # replay compares bundles made at the same commit (no AI call: a recorded pick is reused).
    st ticket points "$key" >/dev/null || true
    st ticket bundle "$key" >/dev/null || true
  done
}

phase_replay() {
  at "$PINNED"
  local saved="$TICKETS/run/replay"
  rm -rf "$saved"; mkdir -p "$saved"
  issues | while read -r n kind run key; do
    mkdir -p "$saved/$key"; cp -R ".stangent/tickets/$key/raw" ".stangent/tickets/$key/attachments" "$saved/$key/" 2>/dev/null || true
  done
  cp stangent.yaml "$TICKETS/run/stangent.yaml.live"
  # Replay: tickets come from the saved results, nothing from Jira (D77).
  python3 - "$saved" <<'PY'
import sys, re
p = "stangent.yaml"; s = open(p).read()
s = re.sub(r"(?m)^  replay:.*\n", "", s)
s = s.replace("tickets:\n", f"tickets:\n  replay: {sys.argv[1]}\n", 1)
open(p, "w").write(s)
PY
  issues | while read -r n kind run key; do
    local before after calls=0
    before="$(shasum -a 256 ".stangent/tickets/$key/bundle.json" | cut -d' ' -f1)"
    st ticket pull "$key" >/dev/null
    [ "$(st ticket points "$key" | jq -r '.result.ai.status')" = needed ] && calls=$((calls + 1))
    [ "$(st ticket prepare "$key" --step points 2>/dev/null | jq -r '.result.status // "current"')" = prepared ] && calls=$((calls + 1))
    [ "$(st ticket draft "$key" --kind questions 2>/dev/null | jq -r '.result.status // "current"')" = prepared ] && calls=$((calls + 1))
    st ticket bundle "$key" >/dev/null
    after="$(shasum -a 256 ".stangent/tickets/$key/bundle.json" | cut -d' ' -f1)"
    record "$key" ".replayIdentical = $([ "$before" = "$after" ] && echo true || echo false) | .rerunAiCalls = $calls"
    log "replay $key: identical=$([ "$before" = "$after" ] && echo yes || echo NO) aiCalls=$calls"
  done
  cp "$TICKETS/run/stangent.yaml.live" stangent.yaml
}

for p in "${PHASES[@]}"; do
  log "== phase $p"
  "phase_$p"
done
at "$PINNED"
