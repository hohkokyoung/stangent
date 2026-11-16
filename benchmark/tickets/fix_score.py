"""D345's score: does the ticket bundle help the fix loop fix a bug? Side A (--context ticket) against side B (--context
bundle) on the bugs fix_test.sh ran. All code, no AI; the one judgement (is a green fix clean, or a patch over the
symptom, SONB-18) is a fresh agent's, read from fix-test/judgments.json when it's there.

Per bug and side, from agent-run.json: green (stopped `fixed`: the saved test passes and no other test in its project
newly fails, D105), test reach, the files edited against the developer's fix (answer-key.json), the fixer's tokens and
cost. The frozen bar (D345): B passes with at least 2 more of the bugs clean and green and tokens within 10% of A, or
the same count for at least 10% fewer tokens. Until every green fix is judged, the bar is `pending`.

usage: python3 -I fix_score.py <clone> <tickets dir> [--judge-prep]
--judge-prep writes fix-test/judge/<side>-<KEY>.md for every green fix not yet judged: the ticket's summary, the
developer's diff and the agent's, for the fresh agent to mark `clean` or `symptom` in judgments.json
({"A": {"SONB-14": "clean"}, "B": {...}}). It changes nothing else.
out: fix-test/score.json and fix-test/score.md
"""
import json
import os
import subprocess
import sys

BAR_MORE = 2          # B needs at least this many more clean-and-green bugs …
TOKENS_WITHIN = 0.10  # … with tokens no more than 10% over A's,
TOKENS_FEWER = 0.10   # or the same count for at least 10% fewer.


def load(path, default=None):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def git(clone, *args):
    return subprocess.run(["git", "-C", clone, *args], capture_output=True, text=True, check=False).stdout


def run_row(run, fix_files, judgment):
    checks = {c["name"]: c["result"] for c in run.get("checks", []) if c.get("round") == max((x.get("round", 1) for x in run.get("checks", [])), default=1)}
    fixer = [s for s in run.get("steps", []) if s["name"] == "fixer"]
    edited = set(run.get("edited", []))
    dev = set(fix_files)
    # Green only when the proof's test was shown to reach the change (D346 step 3): "fixed, reach not checked" isn't.
    fixed = run["stop"]["reason"] == "fixed"
    green = fixed and checks.get("test-reach") == "passed"
    return {
        "stop": run["stop"]["reason"] if green or not fixed else "fixed-reach-not-checked",
        "green": green,
        "testReach": checks.get("test-reach"),
        "calls": len(fixer),
        "tokens": sum((s.get("inputTokens") or 0) + (s.get("outputTokens") or 0) for s in fixer),
        "costUsd": round(sum(s["costUsd"] for s in fixer), 4),
        "edited": sorted(edited),
        "devFiles": sorted(dev),
        "sharedFiles": len(edited & dev),
        "filesRecall": round(len(edited & dev) / len(dev), 3) if dev else None,
        "filesPrecision": round(len(edited & dev) / len(edited), 3) if edited else None,
        "outsideReach": run.get("outsideReach", []),
        "judgment": judgment if green else None,
    }


def side_total(rows):
    green = [r for r in rows.values() if r["green"]]
    return {
        "runs": len(rows),
        "green": len(green),
        "clean": sum(1 for r in green if r["judgment"] == "clean"),
        "symptom": sum(1 for r in green if r["judgment"] == "symptom"),
        "unjudged": sum(1 for r in green if r["judgment"] not in ("clean", "symptom")),
        "tokens": sum(r["tokens"] for r in rows.values()),
        "costUsd": round(sum(r["costUsd"] for r in rows.values()), 2),
        "touchesAFixFile": sum(1 for r in rows.values() if r["sharedFiles"] > 0),
    }


def bar(a, b):
    if a["unjudged"] or b["unjudged"]:
        return "pending", f"{a['unjudged'] + b['unjudged']} green fix(es) not yet judged clean or symptom"
    if not a["tokens"]:
        return "pending", "side A used no tokens: nothing to compare against"
    ratio = b["tokens"] / a["tokens"]
    if b["clean"] >= a["clean"] + BAR_MORE and ratio <= 1 + TOKENS_WITHIN:
        return "passed", f"B {b['clean']} clean and green vs A {a['clean']}, tokens {ratio:.0%} of A's"
    if b["clean"] == a["clean"] and ratio <= 1 - TOKENS_FEWER:
        return "passed", f"same count ({b['clean']}) for {1 - ratio:.0%} fewer tokens"
    return "failed", f"B {b['clean']} clean and green vs A {a['clean']}, tokens {ratio:.0%} of A's: the fixer gets only the finder's files and the failing test (D345)"


def judge_prep(clone, tickets, out, key_of, answer, runs, judgments):
    folder = os.path.join(out, "judge")
    os.makedirs(folder, exist_ok=True)
    written = 0
    for side, rows in runs.items():
        for key, (run, n) in rows.items():
            reach = [c["result"] for c in run.get("checks", []) if c["name"] == "test-reach"]
            if run["stop"]["reason"] != "fixed" or reach[-1:] != ["passed"] or judgments.get(side, {}).get(key) in ("clean", "symptom"):
                continue
            issue = answer["issues"][n]
            dev = git(clone, "diff", issue["parent"], issue["fixCommit"])
            mine = ""
            patch = os.path.join(out, side, key, "fix.patch")
            if os.path.exists(patch):
                with open(patch, encoding="utf-8", errors="replace") as f:
                    mine = f.read()
            summary = (load(os.path.join(clone, ".stangent", "tickets", key, "ticket.json"), {}) or {}).get("ticket", {}).get("summary", "")
            with open(os.path.join(folder, f"{side}-{key}.md"), "w", encoding="utf-8") as f:
                f.write(f"# {key}: {summary}\n\nBoth diffs make the bug's failing test pass. Is the agent's a fix of the cause "
                        "(`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against "
                        "the developer's; a different fix can still be clean.\n\n"
                        f"## The developer's fix\n\n```diff\n{dev}\n```\n\n## The agent's fix (side {side})\n\n```diff\n{mine}\n```\n")
            written += 1
    print(f"judge-prep: {written} file(s) in {folder}", file=sys.stderr)


def main():
    if len(sys.argv) < 3:
        print(__doc__.split("usage: ")[1].split("\n")[0], file=sys.stderr)
        sys.exit(2)
    clone, tickets = sys.argv[1], sys.argv[2]
    out = os.path.join(tickets, "fix-test")
    answer = load(os.path.join(tickets, "answer-key.json"))
    keys = load(os.path.join(tickets, "jira-keys.json"), {})
    issue_of = {k: n for n, k in keys.items()}
    judgments = load(os.path.join(out, "judgments.json"), {}) or {}
    runs = {}
    for side in ("A", "B"):
        runs[side] = {}
        folder = os.path.join(out, side)
        if not os.path.isdir(folder):
            continue
        for key in sorted(os.listdir(folder)):
            run = load(os.path.join(folder, key, "agent-run.json"))
            if run is not None and key in issue_of:
                runs[side][key] = (run, issue_of[key])
    if "--judge-prep" in sys.argv:
        judge_prep(clone, tickets, out, issue_of, answer, runs, judgments)
        return
    rows = {side: {key: run_row(run, answer["issues"][n]["files"], judgments.get(side, {}).get(key)) for key, (run, n) in r.items()} for side, r in runs.items()}
    # Only bugs both sides ran are compared: a partial or resumed run mustn't weigh one side with more bugs.
    both = sorted(set(rows["A"]) & set(rows["B"]))
    a, b = (side_total({k: v for k, v in rows[side].items() if k in both}) for side in ("A", "B"))
    verdict, why = bar(a, b) if both else ("pending", "no bug has both sides recorded")
    score = {
        "bar": {"result": verdict, "why": why, "rule": "B >= A + 2 clean and green with tokens within 10%, or the same count for >= 10% fewer tokens (D345)"},
        "bothSides": len(both), "A": a, "B": b, "bugs": {key: {"A": rows["A"].get(key), "B": rows["B"].get(key)} for key in sorted(set(rows["A"]) | set(rows["B"]))},
    }
    with open(os.path.join(out, "score.json"), "w", encoding="utf-8") as f:
        json.dump(score, f, indent=2, sort_keys=True)
        f.write("\n")
    lines = [f"# D345 fix test: does the bundle help a fix?\n", f"**Bar: {verdict}.** {why}.\n",
             "| side | runs | green | clean | symptom | unjudged | touches a fix file | tokens | cost |", "|---|---|---|---|---|---|---|---|---|"]
    for name, t in (("A · ticket", a), ("B · bundle", b)):
        lines.append(f"| {name} | {t['runs']} | {t['green']} | {t['clean']} | {t['symptom']} | {t['unjudged']} | {t['touchesAFixFile']} | {t['tokens']:,} | ${t['costUsd']:.2f} |")
    lines += ["", "| bug | A | B |", "|---|---|---|"]
    for key in sorted(score["bugs"]):
        cell = lambda r: "—" if r is None else f"{r['stop']}{' · ' + r['judgment'] if r['judgment'] else ''} · {r['sharedFiles']}/{len(r['devFiles'])} files · {r['tokens']:,} tok"
        lines.append(f"| {key} | {cell(score['bugs'][key]['A'])} | {cell(score['bugs'][key]['B'])} |")
    with open(os.path.join(out, "score.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(json.dumps(score["bar"]))


if __name__ == "__main__":
    main()
