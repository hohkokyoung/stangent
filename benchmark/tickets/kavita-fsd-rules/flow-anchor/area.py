"""D361 free check, part 2: can each finding be placed in an area by folder, when its lines aren't flow steps?
A line's area: the area of the flow whose `home` folder is the longest prefix of the line's file (layer 3's homes).
usage: python3 -I area.py <review.json> <clone> <result.json from count.py>"""
import json, sys
from collections import Counter
from pathlib import Path
review, clone, res = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
index = json.loads((clone / "specs/flows/index.json").read_text())
homes = {}
for fl in index["flows"]:
    parts = Path(fl["file"]).parts
    if fl.get("home") and parts[1] == "areas" and parts[2] not in ("shared", "unplaced"):
        homes.setdefault(fl["home"].rstrip("/"), set()).add(parts[2])
rows = {r["id"]: r for r in json.loads(res.read_text())["rows"]}
out = Counter(); unplaced = Counter(); multi = 0
for fd in json.loads(review.read_text())["findings"]:
    areas = set(rows[fd["id"]]["areas"])
    for ev in fd.get("evidence") or []:
        f = ev.rpartition(":")[0]
        best = max((h for h in homes if f.startswith(h + "/")), key=len, default=None)
        if best: areas |= homes[best]
        else: unplaced[f.rsplit("/", 1)[0]] += 1
    out[(fd["state"], "area" if areas else "no area")] += 1
    multi += len(areas) > 1
for k in sorted(out): print(f"  {k[0]:13} {k[1]:8} {out[k]}")
print(f"findings in more than one area: {multi}")
print("folders of lines with no area:"); [print(f"  {n}  {d}") for d, n in unplaced.most_common()]
