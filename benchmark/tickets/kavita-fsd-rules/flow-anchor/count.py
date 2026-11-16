"""D361 free check: do the FSD review's evidence lines land inside layer 3's flows?

For each finding of a merged review (review.json, contract fsd-review v1) and each evidence `path:line`, the best of:
  step     the line is a flow step's, condition's or start's own line (`at`)
  method   the line is inside a method (graph node span) that a flow walks: a step's `point`, or a step's line inside it
  file     some flow has a step in the same file, not in this method
  none     no flow touches the file
A finding's level is the best of its lines. Prints counts by finding state, areas reached, and the files of
`none` lines. Never prints FSD quotes or notes (they quote the planted FSD).

usage: python3 -I count.py <review.json> <clone> [--json out.json]
"""
import json, sqlite3, sys
from collections import Counter, defaultdict
from pathlib import Path

review, clone = Path(sys.argv[1]), Path(sys.argv[2])
out = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
index = json.loads((clone / "specs/flows/index.json").read_text())

at = defaultdict(set)          # (file, line) -> flow ids
points = defaultdict(set)      # symbol id -> flow ids
files = defaultdict(set)       # file -> flow ids
step_lines = defaultdict(set)  # file -> {(line, flow)}
area_of = {}

def walk(node, fid):
    if isinstance(node, dict):
        a = node.get("at")
        if isinstance(a, str) and ":" in a:
            f, _, l = a.rpartition(":")
            if l.isdigit():
                at[(f, int(l))].add(fid); files[f].add(fid); step_lines[f].add((int(l), fid))
        p = node.get("point")
        if isinstance(p, str): points[p].add(fid)
        for v in node.values(): walk(v, fid)
    elif isinstance(node, list):
        for v in node: walk(v, fid)

for fl in index["flows"]:
    fid = fl["id"]
    parts = Path(fl["file"]).parts
    area_of[fid] = parts[2] if len(parts) > 3 and parts[1] == "areas" else "?"
    walk(json.loads((clone / fl["file"]).read_text()), fid)

db = sqlite3.connect(clone / ".stangent/graph.sqlite")
def method_at(f, l):
    r = db.execute("select id, line, end_line from nodes where file=? and kind in ('method','endpoint') and line<=? and end_line>=? "
                   "order by end_line-line limit 1", (f, l, l)).fetchone()
    return r

RANK = {"step": 0, "method": 1, "file": 2, "none": 3}
rows, none_files = [], Counter()
for fd in json.loads(review.read_text())["findings"]:
    best, flows = "none", set()
    for ev in fd.get("evidence") or []:
        f, _, l = ev.rpartition(":")
        if not l.isdigit(): continue
        l = int(l)
        if (f, l) in at: lvl, fs = "step", at[(f, l)]
        else:
            m = method_at(f, l)
            fs = set()
            if m:
                fs = set(points.get(m[0], set())) | {fid for (sl, fid) in step_lines.get(f, ()) if m[1] <= sl <= m[2]}
            if fs: lvl = "method"
            elif f in files: lvl, fs = "file", files[f]
            else: lvl = "none"; none_files[f] += 1
        if RANK[lvl] < RANK[best]: best = lvl
        if lvl in ("step", "method"): flows |= fs
    rows.append({"id": fd["id"], "state": fd["state"], "level": best, "flows": len(flows),
                 "areas": sorted({area_of[x] for x in flows})})

by = defaultdict(Counter)
for r in rows: by[r["state"]][r["level"]] += 1
print(f"{len(rows)} findings; level = best evidence line (step / method / file / none)")
for s, c in sorted(by.items()):
    print(f"  {s:13} " + "  ".join(f"{k} {c[k]}" for k in RANK))
tot = Counter(r["level"] for r in rows)
print("  all           " + "  ".join(f"{k} {tot[k]}" for k in RANK))
print("flows per anchored finding:", sorted(r["flows"] for r in rows if r["flows"]))
print("areas per anchored finding:", Counter(len(r["areas"]) for r in rows if r["flows"]))
print("files of lines no flow touches:")
for f, n in none_files.most_common(): print(f"  {n}  {f}")
if out: Path(out).write_text(json.dumps({"rows": rows, "noneFiles": none_files}, indent=1))
