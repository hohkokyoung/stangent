#!/usr/bin/env python3
"""D339 with subagents: turn each subagent transcript into an agent_baseline.py-shaped row, by code.

    python3 -I benchmark/tickets/d339_collect.py <exports dir> <runs.json> <out dir>

<runs.json> maps "KEY-SIDE" (e.g. "SONB-1-A") to the subagent's transcript (JSONL). Writes <out>/side-A.json and
<out>/side-B.json ({"cases": [...]}, as score_d339.py reads) and copies each transcript to <out>/transcripts/.
Per row: the files named (the last JSON object in the agent's final text), every tool call, paths read outside the
export (an isolation breach), Bash commands that are not the export's `stangent graph impact` (a breach on side B,
any Bash on side A), impact calls, and tokens (input + cache creation + cache read + output, summed over the
transcript's assistant messages, each message id counted once).
"""
import json
import os
import re
import shutil
import sys

PATH_KEYS = ("file_path", "path", "pattern_path")


def last_json(text):
    for m in reversed(list(re.finditer(r"\{", text))):
        try:
            obj, _ = json.JSONDecoder().raw_decode(text[m.start():])
            if isinstance(obj, dict) and "files" in obj:
                return obj
        except ValueError:
            continue
    return None


def collect(transcript, export, side):
    events = [json.loads(l) for l in open(transcript) if l.strip().startswith("{")]
    seen, tools_seen, usage, calls, outside, bash_other, impact, final = set(), set(), {}, 0, [], [], 0, ""
    for e in events:
        msg = e.get("message") or {}
        if msg.get("role") == "assistant":
            mid = msg.get("id")
            if mid and mid not in seen and msg.get("usage"):
                seen.add(mid)
                for k, v in msg["usage"].items():
                    if isinstance(v, int):
                        usage[k] = usage.get(k, 0) + v
            for c in msg.get("content") or []:
                if not isinstance(c, dict):
                    continue
                if c.get("type") == "text":
                    final = c.get("text") or final
                if c.get("type") != "tool_use" or c.get("id") in tools_seen:
                    continue
                tools_seen.add(c.get("id"))
                inp = c.get("input") or {}
                if c.get("name") == "SubagentHandback":
                    final = str(inp.get("message", ""))
                    continue
                calls += 1
                if c.get("name") == "Bash":
                    cmd = str(inp.get("command", ""))
                    if side == "B" and cmd.startswith(os.path.join(export, ".bin", "stangent") + " graph impact"):
                        impact += 1
                    else:
                        bash_other.append(cmd)
                for k in PATH_KEYS:
                    p = inp.get(k)
                    if isinstance(p, str) and os.path.isabs(p):
                        real = os.path.realpath(p)
                        ticket = os.path.realpath(export.rsplit("-", 1)[0] + ".ticket.md")
                        if real != ticket and not real.startswith(os.path.realpath(export) + os.sep) and real != os.path.realpath(export):
                            outside.append(p)
    answer = last_json(final) or {}
    named = [str(f.get("path", "")).removeprefix("./").removeprefix(export + "/") for f in answer.get("files", [])][:10]
    tokens = sum(usage.get(k, 0) for k in ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens"))
    return {"named": named, "toolCalls": calls, "impactCalls": impact, "bashOther": bash_other, "outsideExport": outside,
            "usage": usage, "tokens": tokens, "answerParsed": bool(answer)}


def main(exports, runs_path, out):
    runs = json.load(open(runs_path))
    key = json.load(open("benchmark/tickets/sonarr/answer-key.json"))["issues"]
    number_of = {v: k for k, v in json.load(open("benchmark/tickets/sonarr/jira-keys.json")).items()}
    os.makedirs(os.path.join(out, "transcripts"), exist_ok=True)
    sides = {"A": [], "B": []}
    for run, transcript in sorted(runs.items()):
        k, side = run.rsplit("-", 1)
        n = number_of[k]
        row = collect(transcript, os.path.join(exports, run), side)
        fix = key[n]["files"]
        rank = next((i + 1 for i, p in enumerate(row["named"]) if p in fix), None)
        sides[side].append({"key": k, "issue": int(n), "hit": rank is not None, "rank": rank, "fix": fix, "costUsd": None, **row})
        shutil.copy(transcript, os.path.join(out, "transcripts", f"{run}.jsonl"))
    for side, rows in sides.items():
        json.dump({"what": f"D339 side {side} as Sonnet subagents (see D339's amendment)", "tickets": len(rows),
                   "hits": sum(r["hit"] for r in rows), "isolationBreaches": sum(len(r["outsideExport"]) + (len(r["bashOther"])) for r in rows),
                   "cases": rows}, open(os.path.join(out, f"side-{side}.json"), "w"), indent=1)
        print(side, len(rows), "hits", sum(r["hit"] for r in rows), "unparsed", sum(not r["answerParsed"] for r in rows),
              "breaches", sum(len(r["outsideExport"]) + len(r["bashOther"]) for r in rows), "tokens", sum(r["tokens"] for r in rows))


if __name__ == "__main__":
    main(*sys.argv[1:4])
