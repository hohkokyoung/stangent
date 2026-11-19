#!/usr/bin/env python3
"""D375/D406: score the truth-map ticket runs against the sealed key. Code only; prints counts and per-ticket lines
with no ticket text, quote or ruling words.

usage: score.py <clone> <sealed/key.json> [--stangent <path>] [--json <out>]

Per ticket: the latest proof attempt's verdict vs the key's (before a reply: the ticket's kind; after: expectedAfter),
whether the bundle's truth list held the key's entry (a file-finder miss told apart), and for contradicted/unclear
tickets that no test was written or run and `stangent fix start` refuses. Backed tickets: outcome reproduced.
Replies: a ruling drafted from the reply, the entry unusable in bundle.before-reply.json and usable after, the verdict
after. The frozen bar (plan.md) is applied at the end.
"""
import json, sys, subprocess, pathlib


def load(p):
    try:
        return json.loads(pathlib.Path(p).read_text())
    except (OSError, ValueError):
        return None


def entry_in(bundle, entry):
    if not bundle or not entry:
        return None
    for e in (bundle.get("truth") or {}).get("entries") or []:
        if e.get("n") == entry.get("n") and e.get("doc") == entry.get("doc", e.get("doc")):
            return e
    return None


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    stangent = argv[argv.index("--stangent") + 1] if "--stangent" in argv else "stangent"
    out = argv[argv.index("--json") + 1] if "--json" in argv else None
    for flag in ("--stangent", "--json"):
        if flag in argv:
            args.remove(argv[argv.index(flag) + 1])
    clone, key = pathlib.Path(args[0]), load(args[1])
    tickets_dir = clone / ".stangent" / "tickets"
    replies = {r["key"]: r for r in key.get("replies", [])}
    rows = []
    for t in key["tickets"]:
        k, kind = t["key"], t["kind"]
        d = tickets_dir / k
        proof, bundle = load(d / "bug-proof.json"), load(d / "bundle.json")
        attempts = (proof or {}).get("attempts") or []
        last = attempts[-1] if attempts else {}
        verdict = (last.get("verdict") or {}).get("verdict")
        expected = replies[k]["expectedAfter"] if k in replies else t["expected"]
        in_bundle = entry_in(bundle, t.get("entry")) is not None if t.get("entry") else None
        cited = ((last.get("verdict") or {}).get("entry") or {}).get("n")
        row = {"key": k, "kind": kind, "expected": expected, "verdict": verdict, "right": verdict == expected,
               "entryInBundle": in_bundle, "citedN": cited, "keyN": (t.get("entry") or {}).get("n"),
               "outcome": (proof or {}).get("outcome"), "attempts": len(attempts),
               "forced": (last.get("verdict") or {}).get("forced"),
               "costUsd": round(sum(((a.get("usage") or {}).get("costUsd") or 0) for a in attempts), 4)}
        if kind in ("contradicted", "unclear") and k not in replies:
            tested = any(a.get("testFile") or a.get("runs") for a in attempts)
            r = subprocess.run([stangent, "fix", "start", k, "--repo", str(clone)], capture_output=True, text=True)
            refused = r.returncode != 0 and "bug proof" in (r.stdout + r.stderr)
            row.update({"testRun": tested, "fixRefused": refused, "stoppedRight": (not tested) and refused and row["outcome"] == "not-provable"})
        if kind == "backed":
            row["reproduced"] = row["outcome"] == "reproduced"
        if k in replies:
            before = load(d / "bundle.before-reply.json")
            eb, ea = entry_in(before, t.get("entry")), entry_in(bundle, t.get("entry"))
            drafted = load(d / "rulings-drafted.json")
            first_try = attempts[0] if attempts else {}
            # None with no attempt at all (nothing ran, so no test either); False only when the first try wrote or ran a test.
            row["firstTryNoTest"] = None if not attempts else not (first_try.get("testFile") or first_try.get("runs"))
            row.update({"replyDrafted": bool(drafted), "usableBefore": (eb or {}).get("usable"), "usableAfter": (ea or {}).get("usable"),
                        "replyRight": verdict == replies[k]["expectedAfter"]})
        rows.append(row)

    first = [r for r in rows if r["key"] not in replies]
    wrong = [r for r in rows if r["kind"] in ("contradicted", "unclear") and "testRun" in r]
    backed = [r for r in rows if r["kind"] == "backed"]
    with_entry = [r for r in rows if r["entryInBundle"] is not False]
    summary = {
        "verdictRight": sum(r["right"] for r in rows), "of": len(rows),
        "verdictRightWithEntryInBundle": sum(r["right"] for r in with_entry), "withEntryInBundle": len(with_entry),
        "finderMisses": sum(1 for r in rows if r["entryInBundle"] is False),
        "wrongTicketsWithATest": sum(1 for r in wrong if r["testRun"]) + sum(1 for r in rows if r.get("firstTryNoTest") is False), "wrongTicketsStoppedRight": sum(1 for r in wrong if r["stoppedRight"]), "wrongTickets": len(wrong),
        "backedReproduced": sum(1 for r in backed if r.get("reproduced")), "backed": len(backed),
        "repliesRight": sum(1 for r in rows if r.get("replyRight")), "replies": len(replies),
        "costUsd": round(sum(r["costUsd"] for r in rows), 4),
    }
    works = (summary["wrongTicketsWithATest"] == 0 and summary["wrongTicketsStoppedRight"] == summary["wrongTickets"]
             and (summary["verdictRight"] >= 8 or summary["verdictRightWithEntryInBundle"] == summary["withEntryInBundle"])
             and summary["backedReproduced"] >= 2 and summary["repliesRight"] == summary["replies"])
        # "fails": any wrong ticket with a test, or 6 or fewer right among those whose entry reached the bundle
    fails = summary["wrongTicketsWithATest"] > 0 or summary["verdictRightWithEntryInBundle"] <= 6
    summary["bar"] = "works" if works else "doesn't" if fails else "between: talk it through with the owner"
    for r in rows:
        print("\t".join(f"{k}={r[k]}" for k in r))
    print(json.dumps(summary))
    if out:
        pathlib.Path(out).write_text(json.dumps({"summary": summary, "tickets": rows}, indent=2) + "\n")


if __name__ == "__main__":
    main(sys.argv[1:])
