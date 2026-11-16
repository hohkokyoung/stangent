"""D361 free check, part 3: evidence lines against layer 3's flows and the parked inventory (D326-D329) together.
Per evidence line, the best of: flow (count.py's step or method), inventory-line (an inventory line's own `at`),
inventory-part (the file holds inventory lines, so its screen or shared part is known), graph (inside a graph node only).
usage: python3 -I inventory.py <review.json> <clone> <result.json from count.py>"""
import json, sys
from collections import Counter, defaultdict
from pathlib import Path
review, clone, res = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
inv = json.loads((clone / "specs/inventory.json").read_text())
lines, files = set(), defaultdict(set)
def walk(n, group):
    if isinstance(n, dict):
        a = n.get("at")
        if isinstance(a, str) and a.rpartition(":")[2].isdigit():
            f, _, l = a.rpartition(":"); lines.add((f, int(l))); files[f].add(group)
        for v in n.values(): walk(v, group)
    elif isinstance(n, list):
        for v in n: walk(v, group)
for key in ("screens", "sharedParts", "notPlaced"):
    for g in inv.get(key) or []: walk(g, f"{key}:{g.get('id') or g.get('title') or '?'}")
# count.py's per-line flow levels, recomputed cheaply: a finding's flow anchor is all-or-nothing there, so redo per line
sys.argv = ["count.py", str(review), str(clone)]
import io, contextlib
ns = {}
with contextlib.redirect_stdout(io.StringIO()): exec(Path(__file__).with_name("count.py").read_text(), ns)
RANK = ["flow", "inventory-line", "inventory-part", "graph", "none"]
per_line, per_finding = Counter(), defaultdict(Counter)
for fd in json.loads(review.read_text())["findings"]:
    best = 4
    for ev in fd.get("evidence") or []:
        f, _, l = ev.rpartition(":"); l = int(l)
        m = ns["method_at"](f, l)
        inflow = (f, l) in ns["at"] or (m and (ns["points"].get(m[0]) or any(m[1] <= sl <= m[2] for sl, _ in ns["step_lines"].get(f, ()))))
        if inflow: lv = 0
        elif (f, l) in lines: lv = 1
        elif f in files and not files[f] <= {g for g in files[f] if g.startswith("notPlaced")}: lv = 2
        else: lv = 3 if ns["db"].execute("select 1 from nodes where file=? and line<=? and end_line>=? and kind!='file'", (f, l, l)).fetchone() else 4
        per_line[RANK[lv]] += 1; best = min(best, lv)
    per_finding[fd["state"]][RANK[best]] += 1
print("per evidence line:", dict(per_line))
for s, c in sorted(per_finding.items()): print(f"  {s:13} " + "  ".join(f"{k} {c[k]}" for k in RANK))
tot = sum(per_finding.values(), Counter()); print("  all           " + "  ".join(f"{k} {tot[k]}" for k in RANK))
