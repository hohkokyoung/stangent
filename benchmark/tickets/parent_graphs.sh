#!/usr/bin/env bash
# D339: a code graph at each bug's fix parent, so `graph impact` sees the code as it was before the fix (no fix leaks in).
# For each issue in the answer key: a detached git worktree of the clone at the parent, the clone's SDK override and
# stangent.yaml copied in, `stangent graph build --full`, then only `.stangent/graph.sqlite` is kept as <out>/<issue>.sqlite
# and the worktree is removed. A graph already in <out> is skipped, so the same command resumes. No AI.
#
#   benchmark/tickets/parent_graphs.sh <clone> <tickets dir> <stangent.dll> <out dir>
# Writes <out>/builds.jsonl: one line per issue with its parent, seconds, nodes, edges, load errors, or the failure.
set -uo pipefail
clone=$1 tickets=$2 dll=$3 out=$4
mkdir -p "$out/work"
python3 -I -c '
import json,sys
for n,v in json.load(open(sys.argv[1]))["issues"].items(): print(n, v["parent"])
' "$tickets/answer-key.json" | while read -r n parent; do
  [ -f "$out/$n.sqlite" ] && continue
  wt="$out/work/$n"
  git -C "$clone" worktree remove --force "$wt" 2>/dev/null; rm -rf "$wt"
  if ! git -C "$clone" worktree add --detach -q "$wt" "$parent"; then
    echo "{\"issue\":\"$n\",\"parent\":\"$parent\",\"failure\":\"worktree add failed\"}" >> "$out/builds.jsonl"; continue
  fi
  cp "$clone/global.json" "$clone/stangent.yaml" "$wt/"
  start=$(date +%s)
  (cd "$wt" && dotnet "$dll" graph build --full > "$out/work/$n.json" 2> "$out/work/$n.err")
  code=$?
  secs=$(( $(date +%s) - start ))
  if [ $code -eq 0 ] && [ -f "$wt/.stangent/graph.sqlite" ]; then
    cp "$wt/.stangent/graph.sqlite" "$out/$n.sqlite"
  fi
  python3 -I -c '
import json,sys
n,parent,code,secs,path=sys.argv[1:6]
row={"issue":n,"parent":parent,"exit":int(code),"seconds":int(secs)}
try:
    r=json.load(open(path)).get("result",{})
    row.update({k:r.get(k) for k in ("commit","nodes","edges","projects","loadErrors","loadWarnings","historyStatus","frontends")})
    row["loadErrors"]=len(row["loadErrors"]) if isinstance(row["loadErrors"],list) else row["loadErrors"]
except Exception as e:
    row["failure"]=str(e)
print(json.dumps(row))
' "$n" "$parent" "$code" "$secs" "$out/work/$n.json" >> "$out/builds.jsonl"
  git -C "$clone" worktree remove --force "$wt" 2>/dev/null; rm -rf "$wt"
  echo "$n exit=$code ${secs}s" >&2
done
git -C "$clone" worktree prune
