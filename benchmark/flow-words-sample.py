#!/usr/bin/env python3
"""D416 (4): sample worded lines from a document's flow-words.json for the frozen measurement.

usage: flow-words-sample.py <clone> <doc> <out.json> [--n 100] [--seed 416]
       flow-words-sample.py spot <out.md> <sample.json>... [--seed 417] [--k 10]

Population: every worded line in specs/fsd/<doc>/flow-words.json (a line in `failed` keeps the code's own label, so it is
counted apart, never sampled). Code samples min(n, population) lines with random.Random(seed); when the population is
smaller than n every line is judged. Each line carries its flow, its pick id, and the pick's code, file:line and conditions,
re-picked here from the flow file by FlowWords.Pick's rules (src/Stangent.Spec/Fsd/FlowWords.cs) and checked word for word
against the writer's input (.stangent/fsd/<doc>/view/input.md), plus a few lines of the source around the pick's line.
`spot` draws the owner's k lines across every given sample with random.Random(seed), before any judge has run, and
writes them with their code for the owner to answer blind (<out.md>, and <out.md>'s .json beside it).
Reads only; writes the named outputs.
"""
import argparse
import json
import os
import random
import re
import sys

MAX_CHANGES = 8
MAX_STOPS = 8
CONTEXT = 6  # source lines shown on each side of a pick's line


def reading(kind, code, case):
    """FlowFiles.ConditionText (src/Stangent.Flows/FlowFiles.cs): a condition as the flow's Markdown reads it."""
    when = {
        "mutation": f"only when the mutation is started (`{code}` is called)",
        "timer": f"after `{code}`'s delay",
        "stream": f"only when the stream sends a value (`{code}`)" + (f" that passes `{case}`" if case is not None else ""),
        "listener": f"only when the event comes (`{code}`)",
        "drawn": f"only while `{code}` ({case or ''})",
    }
    if kind in when:
        return when[kind]
    return f"only if `{code}`" + {"else": " is false", "unless": " is false (an earlier guard)",
                                  "cache-miss": " finds nothing stored for the key", "set": " is set"}.get(kind, "")


def conds(node):
    return [reading(c.get("kind") or "if", c["code"].replace("`", "'"), c.get("case"))
            for c in (node or []) if isinstance(c, dict) and (c.get("code") or "")]


def is_change(kind, what):
    return kind in ("queue", "file", "push", "outside-call", "navigate") or (kind == "database" and what.startswith("saves"))


def pick(clone, entry):
    """FlowWords.Pick, item by item."""
    with open(os.path.join(clone, entry["file"]), encoding="utf-8") as f:
        flow = json.load(f)
    name = flow.get("name") or entry.get("name") or entry["id"]
    start = flow.get("start") or {}
    start_at = start.get("at") or ""
    items = [{"id": "S", "kind": "start", "code": f"{name}: {start.get('code') or ''}".strip().rstrip(":"), "at": start_at, "conditions": []}]
    actor = flow.get("actor")
    if isinstance(actor, dict) and actor.get("who"):
        who, why = actor["who"], actor.get("why") or ""
        items.append({"id": "A", "kind": "actor", "code": f"{who} ({why})" if why else who, "at": start_at, "conditions": []})
    changes, seen = [], set()
    for s in flow.get("steps") or []:
        what = s.get("what") or ""
        if not is_change(s.get("kind") or "", what) or what in seen:
            continue
        seen.add(what)
        changes.append({"id": f"C{len(changes) + 1}", "kind": "change", "code": f"{what} — {(s.get('code') or '').strip()}",
                        "at": s.get("at") or "", "conditions": conds(s.get("conditions"))})
    stops, keys = [], set()
    for x in flow.get("exits") or []:
        c = conds(x.get("conditions"))
        kind = x.get("kind") or "stop"
        key = (kind, tuple(c))
        if key in keys:
            continue
        keys.add(key)
        stops.append({"id": f"X{len(stops) + 1}", "kind": "stop", "code": f"{kind}: {(x.get('code') or '').strip()}",
                      "at": x.get("at") or "", "conditions": c})
    return name, items + changes[:MAX_CHANGES] + stops[:MAX_STOPS]


def input_items(clone, doc):
    """The writer's input: {flow name: {item id: line as shown}} (the code with ` → ')."""
    path = os.path.join(clone, ".stangent", "fsd", doc, "view", "input.md")
    out, cur = {}, None
    if not os.path.exists(path):
        return out
    for line in open(path, encoding="utf-8"):
        m = re.match(r"^## V\d+ · (.*)$", line.rstrip("\n"))
        if m:
            cur = out.setdefault(m.group(1), {})
            continue
        m = re.match(r"^- \*\*(\w+)\*\* \((\w+)\): (.*)$", line.rstrip("\n"))
        if m and cur is not None:
            cur[m.group(1)] = m.group(3)
    return out


def shown(item):
    s = f"`{item['code'].replace('`', chr(39))}`"
    if item["conditions"]:
        s += " — " + "; ".join(item["conditions"])
    return s


def snippet(clone, at):
    m = re.match(r"^(.*):(\d+)$", at or "")
    if not m:
        return None
    path, n = os.path.join(clone, m.group(1)), int(m.group(2))
    try:
        lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
    except OSError:
        return None
    lo, hi = max(n - CONTEXT, 1), min(n + CONTEXT, len(lines))
    return "\n".join(f"{'>' if i == n else ' '}{i:5d}  {lines[i - 1]}" for i in range(lo, hi + 1))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clone")
    ap.add_argument("doc")
    ap.add_argument("out")
    ap.add_argument("--n", type=int, default=100)
    ap.add_argument("--seed", type=int, default=416)
    a = ap.parse_args()

    words_path = os.path.join(a.clone, "specs", "fsd", a.doc, "flow-words.json")
    words = json.load(open(words_path, encoding="utf-8"))["flows"]
    index = {f["id"]: f for f in json.load(open(os.path.join(a.clone, "specs", "flows", "index.json"), encoding="utf-8"))["flows"]}
    given = input_items(a.clone, a.doc)

    population, failed, mismatches = [], [], []
    for flow_id in sorted(words):
        w = words[flow_id]
        if flow_id not in index:
            mismatches.append(f"{flow_id}: not in specs/flows/index.json")
            continue
        name, items = pick(a.clone, index[flow_id])
        by_id = {i["id"]: i for i in items}
        lines = w.get("lines") or {}
        fails = set(w.get("failed") or [])
        if set(lines) != set(by_id):
            mismatches.append(f"{flow_id}: lines {sorted(lines)} vs picks {sorted(by_id)}")
        for item_id in sorted(lines):
            item = by_id.get(item_id)
            if item is None:
                continue
            if name in given and given[name].get(item_id) not in (None, shown(item)):
                mismatches.append(f"{flow_id} {item_id}: re-picked code differs from the writer's input")
            row = {"flow": flow_id, "flowName": name, "flowTitle": w.get("title") or "", "flowFile": index[flow_id]["file"],
                   "item": item_id, "kind": item["kind"], "code": item["code"], "at": item["at"],
                   "conditions": item["conditions"], "text": lines[item_id]}
            (failed if item_id in fails else population).append(row)

    rng = random.Random(a.seed)
    n = min(a.n, len(population))
    sample = sorted(rng.sample(population, n), key=lambda r: (r["flow"], r["item"]))
    for k, r in enumerate(sample, 1):
        r["n"] = k
        r["source"] = snippet(a.clone, r["at"])

    out = {"doc": a.doc, "clone": os.path.basename(os.path.normpath(a.clone)), "seed": a.seed,
           "asked": a.n, "population": len(population), "failedLines": len(failed), "sampled": n,
           "flows": len(words), "pickMismatches": mismatches, "lines": sample}
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{a.doc}: {len(population)} worded lines ({len(failed)} failed, kept the code's label), sampled {n} with seed {a.seed}; "
          f"pick mismatches {len(mismatches)}")
    return 1 if mismatches else 0


def spot():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("samples", nargs="+")
    ap.add_argument("--seed", type=int, default=417)
    ap.add_argument("--k", type=int, default=10)
    a = ap.parse_args(sys.argv[2:])
    by = {}
    for path in a.samples:
        d = json.load(open(path, encoding="utf-8"))
        for r in d["lines"]:
            by[(d["doc"], r["n"])] = r
    picked = sorted(random.Random(a.seed).sample(sorted(by), min(a.k, len(by))))
    md = ["# The owner's lines (D416 (4))", "",
          f"Drawn by code before any judge ran: `random.Random({a.seed}).sample` of {len(picked)} over the {len(by)} judged lines, "
          "sorted by (doc, n). For each, answer **true**, **false** or **can't tell**: is the line a true plain-words statement "
          "of what the code at `at` does, under its conditions? (Several conditions all hold together.)", ""]
    for k, (doc, n) in enumerate(picked, 1):
        r = by[(doc, n)]
        md += [f"## {k}. {doc} #{n} · {r['flowName']} · {r['item']} ({r['kind']})", "", f"**Line:** {r['text']}", "",
               f"**Code's label:** `{r['code']}`" + (f"  \n**Conditions:** {'; '.join(r['conditions'])}" if r["conditions"] else ""), "",
               f"**At:** `{r['at']}`", "", "```", r["source"] or "(no source)", "```", ""]
    with open(a.out, "w", encoding="utf-8") as f:
        f.write("\n".join(md))
    with open(os.path.splitext(a.out)[0] + ".json", "w", encoding="utf-8") as f:
        json.dump({"seed": a.seed, "population": len(by), "picked": [{"doc": d, "n": n} for d, n in picked]}, f, indent=2)
        f.write("\n")
    print(f"spot: {[f'{d}#{n}' for d, n in picked]}")
    return 0


if __name__ == "__main__":
    sys.exit(spot() if sys.argv[1:2] == ["spot"] else main())
