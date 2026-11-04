#!/usr/bin/env python3
"""The agent baseline (retrieval redesign, 2026-11-02): can a coding agent, given only a ticket and the code, name the
files the real fix changed? It decides whether stangent builds its own search or lets an agent search while code
checks what it names.

For each ticket: the code at the fix's parent commit is exported with `git archive` (no history, so no future commits
to find), headless Claude Code gets the ticket's text and read-only search tools (Read, Grep, Glob; reads under the
stangent repo, the benchmark folder and the home folder's projects are denied), and returns up to 10 repo-relative
files, most likely first. Capped like a proof attempt (D86): 30 turns, $1. Scored by code: a hit is any fix file among
the named files (D79's right place, at the file level).

    python3 -I benchmark/tickets/agent_baseline.py <clone> <tickets dir> <out.json> KEY [KEY...]
    python3 -I benchmark/tickets/agent_baseline.py --heldout <clone> <heldout.json> <out.json>   # D100's bitwarden set

In held-out mode the ticket is the issue's title, body and the comments before the fix (select.py), the code is the
merge commit's first parent, and the fix files are what the merge commit changed (its own paths, not traced forward).

The hard stop (T.2): STANGENT_MAX_COST (dollars, or `none`) must be set, or nothing runs (exit 2). A ticket starts only if
its whole cap is left. A usage limit stops the run (exit 5); what's done is saved in the output file, and the same command
resumes from it.
Env: CLAUDE (default `claude`), MODEL (default sonnet), WORK (export folder, default a temp folder; removed after).
Transcripts are kept beside out.json (`<out>.transcripts/KEY.jsonl`) to check the agent only read the export.
"""
import json
import os
import shutil
import re
import subprocess
import sys
import tempfile

TURNS, BUDGET, MAX_FILES = 30, 1.0, 10
HOME = os.path.expanduser("~")
LIMIT_WORDS = re.compile(r"usage limit|limit reached|limit exceeded|hit your( [a-z-]+)* limit|reached your( [a-z-]+)* limit|out of( extra)? usage|rate.?limit|weekly limit|session limit|quota", re.I)

SCHEMA = {
    "type": "object", "additionalProperties": False, "required": ["files"],
    "properties": {"files": {"type": "array", "maxItems": MAX_FILES, "items": {
        "type": "object", "additionalProperties": False, "required": ["path", "reason"],
        "properties": {"path": {"type": "string"}, "reason": {"type": "string"}}}}},
}

PROMPT = """A bug report for the code in the current folder is below. Find the source files most likely to need
changing to fix it. Search the code with Grep, Glob and Read; don't change anything. Return up to {n} file paths,
relative to the current folder, most likely first, each with a one-line reason.

{ticket}"""


def ticket_text(t):
    parts = [f"Summary: {t['ticket']['summary']}", "", t.get("description") or ""]
    for c in t.get("comments") or []:
        parts += ["", "Comment:", c.get("body") or c.get("text") or ""]
    return "\n".join(parts).strip()


def run_one(clone, key, number, parent, fix, text, out_dir, work_root):
    work = tempfile.mkdtemp(prefix=f"{key.strip('#')}-", dir=work_root)
    try:
        archive = subprocess.run(["git", "-C", clone, "archive", parent], capture_output=True, check=True).stdout
        subprocess.run(["tar", "-x", "-C", work], input=archive, check=True)
        denied = [f"Read(/{p}/**)" for p in (os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")),
                                             clone, os.path.join(HOME, "stangent-bench"), os.path.join(HOME, "Projects"),
                                             os.path.join(HOME, ".claude"), "/private/tmp/claude-501")]
        args = [os.environ.get("CLAUDE", "claude"), "-p", "--model", os.environ.get("MODEL", "sonnet"),
                "--tools", "Read,Grep,Glob", "--allowedTools", "Read", "Grep", "Glob", "--disallowedTools", *denied,
                "--permission-mode", "dontAsk", "--setting-sources", "", "--strict-mcp-config", "--disable-slash-commands",
                "--no-session-persistence", "--max-turns", str(TURNS), "--max-budget-usd", str(BUDGET),
                "--json-schema", json.dumps(SCHEMA), "--output-format", "stream-json", "--verbose",
                PROMPT.format(n=MAX_FILES, ticket=text)]
        proc = subprocess.run(args, cwd=work, capture_output=True, text=True)
        transcript = os.path.join(out_dir, f"{key.strip('#')}.jsonl")
        with open(transcript, "w") as f:
            f.write(proc.stdout)
        events = [json.loads(l) for l in proc.stdout.splitlines() if l.strip().startswith("{")]
        result = next((e for e in reversed(events) if e.get("type") == "result"), {})
        named = [f["path"].removeprefix("./") for f in (result.get("structured_output") or {}).get("files", [])][:MAX_FILES]
        # Every path the agent's tools touched must be inside the export (isolation check).
        touched, calls = [], 0
        for e in events:
            for c in (e.get("message") or {}).get("content") or []:
                if isinstance(c, dict) and c.get("type") == "tool_use" and c.get("name") != "StructuredOutput":
                    calls += 1
                    for k in ("file_path", "path"):
                        if isinstance(c.get("input", {}).get(k), str):
                            touched.append(c["input"][k])
        outside = [p for p in touched if os.path.isabs(p) and not os.path.realpath(p).startswith(os.path.realpath(work))]
        rank = next((i + 1 for i, p in enumerate(named) if p in fix), None)
        return {
            "key": key, "issue": number, "hit": rank is not None, "rank": rank, "named": named, "fix": fix,
            "status": result.get("subtype"), "turns": result.get("num_turns"), "costUsd": result.get("total_cost_usd"),
            "toolCalls": calls, "outsideExport": outside,
            "failure": (str(result.get("result") or "") + " " + proc.stderr) if result.get("is_error") or not result else None,
        }
    finally:
        shutil.rmtree(work, ignore_errors=True)


def sonarr_cases(clone, tickets, keys):
    answers = json.load(open(os.path.join(tickets, "answer-key.json")))["issues"]
    number_of = {v: k for k, v in json.load(open(os.path.join(tickets, "jira-keys.json"))).items()}
    for key in keys:
        n = number_of[key]
        ticket = json.load(open(os.path.join(clone, ".stangent", "tickets", key, "ticket.json")))
        yield key, int(n), answers[n]["parent"], answers[n]["files"], ticket_text(ticket)


def heldout_cases(clone, heldout):
    for c in json.load(open(heldout)):
        merge = c["fix"]["mergeCommit"]
        parent = subprocess.run(["git", "-C", clone, "rev-parse", merge + "^1"], capture_output=True, text=True, check=True).stdout.strip()
        files = subprocess.run(["git", "-C", clone, "diff", "--name-only", parent, merge], capture_output=True, text=True, check=True).stdout.split()
        text = "\n".join([f"Summary: {c['summary']}", "", c["description"], *[f"\nComment:\n{x}" for x in c["comments"]]]).strip()
        yield c["id"], int(c["id"].strip("#")), parent, files, text


def main(clone, out, cases):
    out_dir = out + ".transcripts"
    os.makedirs(out_dir, exist_ok=True)
    work_root = os.environ.get("WORK") or tempfile.mkdtemp(prefix="agent-baseline-")
    cap = os.environ.get("STANGENT_MAX_COST")
    if not cap:
        print("STOPPED (no_cap): set STANGENT_MAX_COST to dollars, or to none. Nothing was run.", file=sys.stderr)
        sys.exit(2)
    rows = json.load(open(out))["cases"] if os.path.exists(out) else []
    done, why = {r["key"] for r in rows}, None
    for key, n, parent, fix, text in cases:
        if key in done:
            continue
        spent = sum(r["costUsd"] or 0 for r in rows)
        if cap != "none" and spent + BUDGET > float(cap):
            why = f"cap_reached: --max-cost ${cap}: ${spent:.2f} spent and the next ticket could take ${BUDGET}"
            break
        row = run_one(clone, key, n, parent, fix, text, out_dir, work_root)
        limit = row.pop("failure", None)
        if limit and LIMIT_WORDS.search(limit):
            why = f"usage_limit: Claude Code said: {limit[:300]}"
            break
        rows.append(row)
        print(f"{key}: hit={row['hit']} rank={row['rank']} turns={row['turns']} cost={row['costUsd']} outside={len(row['outsideExport'])}", file=sys.stderr)
    cost = sum(r["costUsd"] or 0 for r in rows)
    summary = {
        "what": "agent baseline: headless Claude Code, ticket text + code at the fix's parent (no history), Read/Grep/Glob only, up to 10 files",
        "model": os.environ.get("MODEL", "sonnet"), "turns": TURNS, "budgetUsd": BUDGET,
        "tickets": len(rows), "hits": sum(r["hit"] for r in rows), "costUsd": round(cost, 4),
        "isolationBreaches": sum(len(r["outsideExport"]) for r in rows), "cases": rows,
    }
    json.dump(summary, open(out, "w"), indent=1)
    print(json.dumps({k: v for k, v in summary.items() if k != "cases"}), file=sys.stderr)
    if why:
        print(f"STOPPED ({why})\n{len(rows)} tickets are saved in {out}. To resume, run the same command again.", file=sys.stderr)
        sys.exit(5)


if __name__ == "__main__":
    if sys.argv[1] == "--heldout":
        main(sys.argv[2], sys.argv[4], heldout_cases(sys.argv[2], sys.argv[3]))
    else:
        main(sys.argv[1], sys.argv[3], sonarr_cases(sys.argv[1], sys.argv[2], sys.argv[4:]))
