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

Env: CLAUDE (default `claude`), MODEL (default sonnet), WORK (export folder, default a temp folder; removed after).
Transcripts are kept beside out.json (`<out>.transcripts/KEY.jsonl`) to check the agent only read the export.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

TURNS, BUDGET, MAX_FILES = 30, 1.0, 10
HOME = os.path.expanduser("~")

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


def run_one(clone, tickets, key, number, answer, out_dir, work_root):
    work = tempfile.mkdtemp(prefix=f"{key}-", dir=work_root)
    try:
        archive = subprocess.run(["git", "-C", clone, "archive", answer["parent"]], capture_output=True, check=True).stdout
        subprocess.run(["tar", "-x", "-C", work], input=archive, check=True)
        ticket = json.load(open(os.path.join(clone, ".stangent", "tickets", key, "ticket.json")))
        denied = [f"Read(/{p}/**)" for p in (os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")),
                                             clone, os.path.join(HOME, "stangent-bench"), os.path.join(HOME, "Projects"))]
        args = [os.environ.get("CLAUDE", "claude"), "-p", "--model", os.environ.get("MODEL", "sonnet"),
                "--tools", "Read,Grep,Glob", "--allowedTools", "Read", "Grep", "Glob", "--disallowedTools", *denied,
                "--permission-mode", "dontAsk", "--setting-sources", "", "--strict-mcp-config", "--disable-slash-commands",
                "--no-session-persistence", "--max-turns", str(TURNS), "--max-budget-usd", str(BUDGET),
                "--json-schema", json.dumps(SCHEMA), "--output-format", "stream-json", "--verbose",
                PROMPT.format(n=MAX_FILES, ticket=ticket_text(ticket))]
        proc = subprocess.run(args, cwd=work, capture_output=True, text=True)
        transcript = os.path.join(out_dir, f"{key}.jsonl")
        with open(transcript, "w") as f:
            f.write(proc.stdout)
        events = [json.loads(l) for l in proc.stdout.splitlines() if l.strip().startswith("{")]
        result = next((e for e in reversed(events) if e.get("type") == "result"), {})
        named = [f["path"].lstrip("./") for f in (result.get("structured_output") or {}).get("files", [])][:MAX_FILES]
        fix = answer["files"]
        # Every path the agent's tools touched must be inside the export (isolation check).
        touched = []
        for e in events:
            for c in (e.get("message") or {}).get("content") or []:
                if isinstance(c, dict) and c.get("type") == "tool_use":
                    for k in ("file_path", "path"):
                        if isinstance(c.get("input", {}).get(k), str):
                            touched.append(c["input"][k])
        outside = [p for p in touched if os.path.isabs(p) and not os.path.realpath(p).startswith(os.path.realpath(work))]
        rank = next((i + 1 for i, p in enumerate(named) if p in fix), None)
        return {
            "key": key, "issue": number, "hit": rank is not None, "rank": rank, "named": named, "fix": fix,
            "status": result.get("subtype"), "turns": result.get("num_turns"), "costUsd": result.get("total_cost_usd"),
            "toolCalls": len(touched), "outsideExport": outside,
        }
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main(clone, tickets, out, keys):
    answers = json.load(open(os.path.join(tickets, "answer-key.json")))["issues"]
    number_of = {v: k for k, v in json.load(open(os.path.join(tickets, "jira-keys.json"))).items()}
    out_dir = out + ".transcripts"
    os.makedirs(out_dir, exist_ok=True)
    work_root = os.environ.get("WORK") or tempfile.mkdtemp(prefix="agent-baseline-")
    rows = []
    for key in keys:
        n = number_of[key]
        row = run_one(clone, tickets, key, int(n), answers[n], out_dir, work_root)
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


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:])
