#!/usr/bin/env python3
"""Phase 2 (D64): the pages generated for the Sonarr proof. Reads `stangent spec pages` and `stangent fsd candidates`
output (JSON files) and writes the page set: every screen inside the FSD's scope (the undocumented check only sees
recorded screens), every page a candidate section uses, plus 10 handlers sampled with seed 42.
usage: sonarr-page-set.py pages.json candidates.json answer-key.json out.json"""
import json, random, sys

pages = json.load(open(sys.argv[1]))["result"]["pages"]
cands = json.load(open(sys.argv[2]))["result"]["result"]
candidate_pages = sorted({c["page"] for s in cands for c in s["candidates"]})
scope = json.load(open(sys.argv[3]))["scope"]
in_scope = lambda e, r: r == e or (e != "/" and r.startswith(e.rstrip("/") + "/"))
scope_screens = sorted(p["id"] for p in pages if p["kind"] == "screen" and any(in_scope(e, p["id"][len("page:"):]) for e in scope))
handlers = sorted(p["id"] for p in pages if p["kind"] == "backend" and not p["id"].startswith("endpoint:"))
sample = random.Random(42).sample(handlers, 10)
json.dump({
    "method": "scope screens: every screen whose route is inside answer-key.json's scope (fsd.scope). candidates: every page any FSD section's candidate list names (stangent fsd candidates). "
              "handlers: backend pages whose id does not start with endpoint:, sorted; sample = random.Random(42).sample(handlers, 10).",
    "pages": len(pages), "overCap": sum(1 for p in pages if p["overCap"]),
    "scopeScreens": scope_screens, "candidatePages": candidate_pages, "handlers": len(handlers), "handlerSample": sample,
    "set": sorted(set(scope_screens) | set(candidate_pages)) + [h for h in sample if h not in candidate_pages],
}, open(sys.argv[4], "w"), indent=1)
