#!/usr/bin/env python3
"""D339's score, by code: does `stangent graph impact` help an agent name every file a fix changes?

    python3 -I benchmark/tickets/score_d339.py <clone> <sideA.json> <sideB.json> [--old <2026-11-02 baseline.json>...]

Both inputs are `agent_baseline.py` outputs (side B run with IMPACT_GRAPHS). The key is each case's `fix` (the developer's
own fix commit). Per ticket: the fix files that exist at the fix's parent (`git cat-file -e parent:path`) are the ones an
agent could name; files the fix creates are counted apart. share = named ∩ existing / existing; wrong = named files not
in the fix at all. Tickets with no existing fix file are left out of the share (reported). "Multi-file" = the fix
changes two or more files (the key's own count, 21 on Sonarr). Tokens = input + cache creation + cache read + output.

Reading (D339, frozen 2026-11-15 before the run): B passes if, on the multi-file fixes, its mean share is at least 5
points above A's with mean wrong files no more than 1 above A's; or the same share or better (within 1 point) for 10%
fewer tokens. Otherwise it fails. Only tickets both sides ran are compared.
"""
import json
import subprocess
import sys


def exists(clone, commit, path):
    return subprocess.run(["git", "-C", clone, "cat-file", "-e", f"{commit}:{path}"], capture_output=True).returncode == 0


def per_ticket(clone, parents, row):
    fix = row["fix"]
    have = [f for f in fix if exists(clone, parents[str(row["issue"])], f)]
    named = row.get("named") or []
    hit = [f for f in have if f in named]
    return {"key": row["key"], "fixFiles": len(fix), "existing": len(have), "created": len(fix) - len(have),
            "share": (len(hit) / len(have)) if have else None, "named": len(named),
            "wrong": len([f for f in named if f not in fix]), "tokens": row.get("tokens") or 0,
            "costUsd": row.get("costUsd") or 0, "impactCalls": row.get("impactCalls"), "anyHit": bool(hit)}


def mean(xs):
    xs = [x for x in xs if x is not None]
    return round(sum(xs) / len(xs), 4) if xs else None


def group(rows):
    scored = [r for r in rows if r["share"] is not None]
    return {"tickets": len(rows), "scored": len(scored), "meanShare": mean(r["share"] for r in scored),
            "wholeFixNamed": sum(1 for r in scored if r["share"] == 1), "anyFixFile": sum(r["anyHit"] for r in rows),
            "meanWrong": mean(r["wrong"] for r in rows), "meanNamed": mean(r["named"] for r in rows),
            "tokens": sum(r["tokens"] for r in rows), "costUsd": round(sum(r["costUsd"] for r in rows), 4)}


def main(argv):
    clone, a_path, b_path = argv[:3]
    olds = argv[argv.index("--old") + 1:] if "--old" in argv else []
    key = json.load(open("benchmark/tickets/sonarr/answer-key.json"))["issues"]
    parents = {n: v["parent"] for n, v in key.items()}
    a = {r["key"]: r for r in json.load(open(a_path))["cases"]}
    b = {r["key"]: r for r in json.load(open(b_path))["cases"]}
    both = sorted(set(a) & set(b), key=lambda k: int(k.split("-")[1]))
    out = {"what": "D339: share of each fix's files named, side A (agent alone) vs side B (plus graph impact)",
           "comparedTickets": len(both), "onlyA": sorted(set(a) - set(b)), "onlyB": sorted(set(b) - set(a)), "sides": {}}
    rows = {}
    for side, data in (("A", a), ("B", b)):
        rows[side] = [per_ticket(clone, parents, data[k]) for k in both]
        multi = [r for r in rows[side] if r["fixFiles"] >= 2]
        out["sides"][side] = {"all": group(rows[side]), "multiFile": group(multi)}
    if olds:
        old = {r["key"]: r for f in olds for r in json.load(open(f))["cases"]}
        o = [per_ticket(clone, parents, old[k]) for k in both if k in old]
        out["sides"]["A_2026-11-02"] = {"all": group(o), "multiFile": group([r for r in o if r["fixFiles"] >= 2])}
    ma, mb = out["sides"]["A"]["multiFile"], out["sides"]["B"]["multiFile"]
    ta, tb = out["sides"]["A"]["all"]["tokens"], out["sides"]["B"]["all"]["tokens"]
    gain = (mb["meanShare"] - ma["meanShare"]) * 100
    more_wrong = mb["meanWrong"] - ma["meanWrong"]
    by_share = gain >= 5 and more_wrong <= 1
    by_tokens = ta > 0 and tb > 0 and gain >= -1 and tb <= 0.9 * ta
    out["reading"] = {"multiFileShareGainPoints": round(gain, 2), "multiFileWrongDelta": round(more_wrong, 3),
                      "tokensBvsA": round(tb / ta, 3) if ta else None, "passByShare": by_share, "passByTokens": by_tokens,
                      "verdict": "pass" if by_share or by_tokens else "fail"}
    out["perTicket"] = [{"key": k, "A": ra, "B": rb} for k, ra, rb in zip(both, rows["A"], rows["B"])]
    print(json.dumps(out, indent=1))


if __name__ == "__main__":
    main(sys.argv[1:])
