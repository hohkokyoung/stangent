#!/usr/bin/env bash
# D345: Phase 4's first measurement, a two-sided fix test through the minimal fix loop (D350). Does the ticket bundle
# help an agent fix a bug? Each of Sonarr's bugs whose saved failing test fails before the developer's fix and passes
# after it runs twice from the fix's parent:
#   side A  fix-run.sh --context ticket   the ticket, the code and the failing test
#   side B  fix-run.sh --context bundle   the same plus the ticket bundle, rebuilt at the parent (no fix leaks in)
# Phases (each resumes: a side already recorded is skipped):
#   recheck  free, no AI: for every reproduced proof whose saved --verify passed, run --verify again at the parent (it
#            must not pass) and at the fix (it must pass); writes fix-test/recheck.jsonl. The run takes the bugs that
#            still qualify, so the 17 is re-derived, not assumed (the saved tests predate the finder).
#   run      paid: both sides per bug. About $8–34 for 34 runs (D345); always under --max-cost.
#   score    free: benchmark/tickets/fix_score.py (and --judge-prep: the diffs a fresh agent marks clean or symptom).
#
# usage: fix_test.sh <clone> <tickets dir> <parent graphs dir> <phase>... [--max-cost USD|none] [--only KEY,...] [--sides A,B]
# Needs the clone's stangent.yaml to list the SDK pin as a local file (D351): `fix: { localFiles: [global.json] }`.
# <parent graphs dir> holds <issue>.sqlite from parent_graphs.sh (D339): the graph at each fix's parent.
# env:   STANGENT (default: the benchmark's stangent build), PLUGIN_DIR, BENCH_DOTNET, as run.sh
# out:   <tickets dir>/fix-test/<A|B>/<KEY>/ agent-run.json, report.md, task-plan.json, checks/, fix.patch; run.log
# Run 2026-11-16 on the 12 unit-test bugs (D356, D357); --only picks bugs, the run phase is paid.
set -euo pipefail

usage() { sed -n 's/^# usage: //p' "$0" >&2; exit 2; }
[ $# -ge 4 ] || usage
CLONE="$(cd "$1" && pwd)"; TICKETS="$(cd "$2" && pwd)"; GRAPHS="$(cd "$3" && pwd)"; shift 3
PHASES=(); ONLY=""; SIDES="A,B"
while [ $# -gt 0 ]; do
  case "$1" in
    --max-cost) [ $# -ge 2 ] || usage; export STANGENT_MAX_COST="$2"; shift 2 ;;
    --only) [ $# -ge 2 ] || usage; ONLY="$2"; shift 2 ;;
    --sides) [ $# -ge 2 ] || usage; SIDES="$2"; shift 2 ;;
    -*) usage ;;
    *) PHASES+=("$1"); shift ;;
  esac
done
[ ${#PHASES[@]} -gt 0 ] || usage
HERE="$(cd "$(dirname "$0")/../.." && pwd)"
export STANGENT_COST_DIR="${STANGENT_COST_DIR:-$(mktemp -d)}"
export STANGENT="${STANGENT:-dotnet $HOME/stangent-bench/stangent-bin/stangent.dll}"
export PLUGIN_DIR="${PLUGIN_DIR:-$HERE/plugin}"
BENCH_DOTNET="${BENCH_DOTNET:-$HOME/stangent-bench/dotnet}"
if [ -x "$BENCH_DOTNET/dotnet" ]; then export DOTNET_ROOT="$BENCH_DOTNET" PATH="$BENCH_DOTNET:$PATH"; fi
SDK="$HERE/benchmark/configs/$(basename "$TICKETS").global.json"
OUT="$TICKETS/fix-test"
mkdir -p "$OUT"
cd "$CLONE"
PINNED="$(jq -r .pinned "$TICKETS/selection.json")"
st() { $STANGENT "$@"; }
log() { printf '%s\t%s\n' "$(date -u +%H:%M:%S)" "$*" | tee -a "$OUT/run.log" >&2; }

# The SDK pin is a local file to the fix loop (D351), or every worktree builds with the commit's own pin.
if [ -f "$SDK" ] && ! grep -Eq 'localFiles:.*global\.json|- global\.json' stangent.yaml; then
  log "stangent.yaml has no fix.localFiles with global.json: add 'fix: { localFiles: [global.json] }' first (D351)"; exit 2
fi

# Back to a commit with the SDK pin and a clean tree, as run.sh's `at` (Sonarr's shared build folders go too).
at() {
  git checkout -q -f "$1"
  git clean -fdq -- src frontend
  rm -rf _tests _output
  if [ -f "$SDK" ]; then cp "$SDK" global.json; fi
}

# Bugs with a reproduced proof whose saved test passed on the fix: "number key parent fix testFile".
candidates() {
  jq -r --slurpfile k "$TICKETS/jira-keys.json" '.issues | to_entries[] | "\(.key) \($k[0][.key] // "") \(.value.parent) \(.value.fixCommit)"' "$TICKETS/answer-key.json" \
    | while read -r n key parent fix; do
        [ -n "$key" ] || continue
        if [ -n "$ONLY" ] && [[ ",$ONLY," != *",$key,"* ]]; then continue; fi
        proof=".stangent/tickets/$key/bug-proof.json"
        [ -f "$proof" ] || continue
        [ "$(jq -r '.outcome' "$proof")" = reproduced ] || continue
        file="$(jq -r '[.attempts[] | select(.outcome == "reproduced")][-1].testFile' "$proof")"
        [ -f ".stangent/tickets/$key/prove/kept/$(basename "$file")" ] || continue
        echo "$n $key $parent $fix $file"
      done
}

# The kept test, back where the proof wrote it (untracked, as `ticket prove` leaves it).
put_test() { mkdir -p "$(dirname "$2")"; cp ".stangent/tickets/$1/prove/kept/$(basename "$2")" "$2"; }

phase_recheck() {
  : >"$OUT/recheck.jsonl"
  candidates | while read -r n key parent fix file; do
    local proof=".stangent/tickets/$key/bug-proof.json" before after
    cp "$proof" "$proof.before-recheck"
    at "$parent"; put_test "$key" "$file"
    st ticket prove "$key" --verify </dev/null >/dev/null || true
    before="$(jq -r '.verification.result // "none"' "$proof")"
    at "$fix"; put_test "$key" "$file"
    st ticket prove "$key" --verify </dev/null >/dev/null || true
    after="$(jq -r '.verification.result // "none"' "$proof")"
    jq -nc --arg n "$n" --arg key "$key" --arg before "$before" --arg after "$after" \
      '{issue: $n, key: $key, atParent: $before, atFix: $after, qualifies: (($before == "same-failure" or $before == "different-failure") and $after == "passed")}' >>"$OUT/recheck.jsonl"
    log "recheck $key: at the parent $before, at the fix $after"
  done
  at "$PINNED"
  log "recheck: $(jq -s 'map(select(.qualifies)) | length' "$OUT/recheck.jsonl") bugs qualify"
}

# However the run ends: side B's pinned bundles and the clone's graph put back, and the clone back on its pin with the SDK
# pin and no leftover test (`at` cleans src/ and frontend/ and copies global.json).
restore() {
  local p
  for p in "$CLONE"/.stangent/tickets/*/bundle.json.pinned; do [ -f "$p" ] && mv "$p" "${p%.pinned}"; done
  [ -z "${SAVED_GRAPH:-}" ] || mv "$SAVED_GRAPH" "$CLONE/.stangent/graph.sqlite"
  (cd "$CLONE" && at "$PINNED") 2>/dev/null || true
}

phase_run() {
  [ -s "$OUT/recheck.jsonl" ] || { log "run: no recheck.jsonl; run the recheck phase first (free)"; exit 2; }
  local graph=".stangent/graph.sqlite"
  # The clone's own graph comes back however the run ends (a stop, an error, Ctrl+C).
  SAVED_GRAPH=""
  if [ -f "$graph" ]; then SAVED_GRAPH="$(mktemp)"; cp "$graph" "$SAVED_GRAPH"; fi
  # The loop below runs in a pipeline's subshell, so its variables don't reach this trap: a side B bundle cut short is
  # found by its .pinned copy.
  trap restore EXIT
  candidates | while read -r n key parent fix file; do
    jq -e --arg key "$key" 'select(.key == $key and .qualifies)' "$OUT/recheck.jsonl" >/dev/null 2>&1 || continue
    for side in ${SIDES//,/ }; do
      local ctx dest bundle=".stangent/tickets/$key/bundle.json"
      case "$side" in A) ctx=ticket ;; B) ctx=bundle ;; *) log "unknown side $side"; exit 2 ;; esac
      dest="$OUT/$side/$key"
      if [ -f "$dest/agent-run.json" ]; then log "$side $key: already recorded"; continue; fi
      at "$parent"; put_test "$key" "$file"
      [ -f "$GRAPHS/$n.sqlite" ] || { log "$side $key: no parent graph $GRAPHS/$n.sqlite (parent_graphs.sh)"; exit 2; }
      cp "$GRAPHS/$n.sqlite" "$graph"
      # Side B's bundle at the parent's graph, so it can't name code from after the fix; the pinned one is put back after.
      if [ "$side" = B ]; then cp "$bundle" "$bundle.pinned"; st ticket bundle "$key" </dev/null >/dev/null; fi
      log "$side $key (#$n) from ${parent:0:10}, --context $ctx"
      rc=0; "$HERE/plugin/scripts/fix-run.sh" --base "$parent" --context "$ctx" --replace "$key" </dev/null >>"$OUT/run.log" 2>&1 || rc=$?
      if [ "$side" = B ]; then mv "$bundle.pinned" "$bundle"; fi
      # 0 fixed, 1 not fixed: a result. 5 the cap or a usage limit, 2 no cap, anything else (125: Claude Code itself
      # failed, nothing recorded): stop and keep what's recorded; the same command resumes.
      if [ "$rc" -ne 0 ] && [ "$rc" -ne 1 ]; then log "stopped ($rc) at $side $key: see run.log"; exit "$rc"; fi
      mkdir -p "$dest"
      cp .stangent/fix/"$key"/{agent-run.json,report.md} "$dest/" 2>/dev/null || true
      cp .stangent/fix/"$key"/task-plan.json "$dest/" 2>/dev/null || true
      cp -R .stangent/fix/"$key"/checks "$dest/" 2>/dev/null || true
      git diff "$parent" "stangent/fix-$key" -- . ":(exclude)$file" >"$dest/fix.patch" 2>/dev/null || true
      log "$side $key: $(jq -r '.stop.reason' "$dest/agent-run.json" 2>/dev/null || echo 'no record')"
    done
  done
}

for phase in "${PHASES[@]}"; do
  case "$phase" in
    recheck) phase_recheck ;;
    run) phase_run ;;
    score) python3 -I "$HERE/benchmark/tickets/fix_score.py" "$CLONE" "$TICKETS" ;;
    *) usage ;;
  esac
done
