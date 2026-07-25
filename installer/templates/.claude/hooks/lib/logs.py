#!/usr/bin/env python3
"""Summarize a workflow's logs into a readable report.

stangent writes two JSONL streams under .claude/state/logs/:
  - <id>.jsonl     one line per tool call, tagged with run_id/task_id/role
  - dispatch.jsonl  one line per /agentic-build dispatch (routing decision)
plus per-task status lives in .claude/state/plans/<id>/*.md when the context is
a build run. These are write-only until now — this reads them back.

CLI:
  logs.py summarize <id> [--json]   per-task + run totals for one context
  logs.py list [--json]             list available log contexts, newest first

Derives entirely from existing data: tool-call counts, denials, failures,
retrieve/get_symbol usage, and per-task duration (from timestamp deltas).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from pathlib import Path

STATE_DIR = Path.cwd().resolve() / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"
PLANS_DIR = STATE_DIR / "plans"
DISPATCH_LOG = LOG_DIR / "dispatch.jsonl"

RETRIEVE_TOOL = "mcp__agentic_mcp__retrieve"
SYMBOL_TOOL = "mcp__agentic_mcp__get_symbol"


def _read_jsonl(path: Path) -> list[dict]:
    out: list[dict] = []
    try:
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line:
                try:
                    out.append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    except OSError:
        pass
    return out


def _parse_ts(s) -> dt.datetime | None:
    if not isinstance(s, str):
        return None
    try:
        return dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
    except ValueError:
        return None


def _fmt_dur(seconds: float | None) -> str:
    if seconds is None:
        return "-"
    seconds = int(seconds)
    if seconds < 90:
        return f"{seconds}s"
    if seconds < 5400:
        return f"{seconds // 60}m"
    return f"{seconds // 3600}h{(seconds % 3600) // 60}m"


def _short_model(m) -> str:
    if not isinstance(m, str) or not m:
        return "-"
    return re.sub(r"^claude-", "", m).replace("-20251001", "")


def _task_statuses(run_id: str) -> dict:
    out: dict[str, str] = {}
    run_dir = PLANS_DIR / run_id
    if not run_dir.is_dir():
        return out
    for md in run_dir.glob("*.md"):
        if md.name == "_overview.md":
            continue
        text = md.read_text(encoding="utf-8", errors="replace")
        m_id = re.search(r"^id:\s*(.+)$", text, re.MULTILINE)
        m_st = re.search(r"^status:\s*(\w+)", text, re.MULTILINE)
        tid = (m_id.group(1).strip() if m_id else md.stem)
        out[tid] = (m_st.group(1) if m_st else "?")
    return out


def summarize(run_id: str) -> dict:
    calls = _read_jsonl(LOG_DIR / f"{run_id}.jsonl")
    dispatches = [d for d in _read_jsonl(DISPATCH_LOG) if d.get("run_id") == run_id]
    statuses = _task_statuses(run_id)

    # role/model per task from the dispatch log (authoritative for build runs).
    disp_by_task = {d.get("task_id"): d for d in dispatches}

    tasks: dict[str, dict] = {}
    order: list[str] = []
    for c in calls:
        tid = c.get("task_id") or "(session)"
        t = tasks.get(tid)
        if t is None:
            t = tasks[tid] = {
                "task_id": tid, "role": c.get("agent_role"),
                "model": c.get("model"), "calls": 0, "retrieve": 0,
                "get_symbol": 0, "denials": 0, "failures": 0,
                "first": None, "last": None, "events": [],
            }
            order.append(tid)
        t["calls"] += 1
        if c.get("tool") == RETRIEVE_TOOL:
            t["retrieve"] += 1
        if c.get("tool") == SYMBOL_TOOL:
            t["get_symbol"] += 1
        if c.get("deny_reason"):
            t["denials"] += 1
            t["events"].append(("deny", tid, c.get("tool"), c.get("deny_reason")))
        if c.get("ok") is False:
            t["failures"] += 1
            t["events"].append(("fail", tid, c.get("tool"), "tool error"))
        ts = _parse_ts(c.get("ts"))
        if ts:
            t["first"] = ts if t["first"] is None else min(t["first"], ts)
            t["last"] = ts if t["last"] is None else max(t["last"], ts)
        if not t.get("role"):
            t["role"] = c.get("agent_role")
        if not t.get("model"):
            t["model"] = c.get("model")

    task_list = []
    all_events = []
    run_first = run_last = None
    for tid in order:
        t = tasks[tid]
        d = disp_by_task.get(tid, {})
        dur = None
        if t["first"] and t["last"]:
            dur = (t["last"] - t["first"]).total_seconds()
            run_first = t["first"] if run_first is None else min(run_first, t["first"])
            run_last = t["last"] if run_last is None else max(run_last, t["last"])
        all_events.extend(t.pop("events"))
        task_list.append({
            "task_id": tid,
            "role": t["role"] or d.get("role"),
            "model": t["model"] or d.get("model_selected"),
            "status": statuses.get(tid, "-"),
            "calls": t["calls"], "retrieve": t["retrieve"],
            "get_symbol": t["get_symbol"], "denials": t["denials"],
            "failures": t["failures"], "duration_s": dur,
            "routing_applied": d.get("routing_applied", False),
        })

    return {
        "run_id": run_id,
        "tasks": task_list,
        "events": all_events,
        "totals": {
            "tasks": len(task_list),
            "calls": sum(t["calls"] for t in task_list),
            "denials": sum(t["denials"] for t in task_list),
            "failures": sum(t["failures"] for t in task_list),
            "retrieve": sum(t["retrieve"] for t in task_list),
            "get_symbol": sum(t["get_symbol"] for t in task_list),
        },
        "started": run_first.isoformat() if run_first else None,
        "ended": run_last.isoformat() if run_last else None,
        "duration_s": (run_last - run_first).total_seconds() if run_first and run_last else None,
        "has_logs": bool(calls),
    }


def list_contexts() -> list[dict]:
    out = []
    if LOG_DIR.is_dir():
        for f in LOG_DIR.glob("*.jsonl"):
            if f.name in ("dispatch.jsonl", "_no-run.jsonl") or ".1.jsonl" in f.name:
                continue
            try:
                out.append({"id": f.stem, "mtime": f.stat().st_mtime,
                            "size": f.stat().st_size})
            except OSError:
                pass
    out.sort(key=lambda r: r["mtime"], reverse=True)
    return out


def _print_report(rep: dict) -> None:
    if not rep["has_logs"]:
        print(f"no tool-use log for '{rep['run_id']}' "
              f"(logs/{rep['run_id']}.jsonl not found)")
        return
    span = ""
    if rep["started"]:
        s = _parse_ts(rep["started"]); e = _parse_ts(rep["ended"])
        span = f"  {s:%Y-%m-%d %H:%M} → {e:%H:%M}  ({_fmt_dur(rep['duration_s'])})"
    print(f"Run {rep['run_id']}{span}")
    tot = rep["totals"]
    print(f"  tasks: {tot['tasks']}   tool calls: {tot['calls']}   "
          f"retrieve: {tot['retrieve']}   get_symbol: {tot['get_symbol']}   "
          f"denials: {tot['denials']}   failures: {tot['failures']}")
    print()
    hdr = f"  {'task':<14}{'role':<16}{'model':<14}{'status':<9}{'calls':>6}{'ret':>5}{'sym':>5}{'deny':>6}{'fail':>6}{'dur':>7}"
    print(hdr)
    for t in rep["tasks"]:
        print(f"  {str(t['task_id']):<14}{str(t['role'] or '-'):<16}"
              f"{_short_model(t['model']):<14}{str(t['status']):<9}"
              f"{t['calls']:>6}{t['retrieve']:>5}{t['get_symbol']:>5}"
              f"{t['denials']:>6}{t['failures']:>6}{_fmt_dur(t['duration_s']):>7}")
    if rep["events"]:
        print("\n  denials / failures:")
        for kind, tid, tool, detail in rep["events"]:
            d = re.sub(r"\s+", " ", str(detail))[:80]
            print(f"    [{kind}] {tid}  {tool}  — {d}")


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(prog="logs.py")
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("summarize"); s.add_argument("id"); s.add_argument("--json", action="store_true")
    l = sub.add_parser("list"); l.add_argument("--json", action="store_true")
    args = ap.parse_args(argv[1:])

    if args.cmd == "summarize":
        rep = summarize(args.id)
        if args.json:
            print(json.dumps(rep, default=str))
        else:
            _print_report(rep)
        return 0

    ctxs = list_contexts()
    if args.json:
        print(json.dumps(ctxs, default=str))
    elif not ctxs:
        print("no run logs yet")
    else:
        print("available log contexts (newest first):")
        for c in ctxs:
            when = dt.datetime.fromtimestamp(c["mtime"]).strftime("%Y-%m-%d %H:%M")
            print(f"  {c['id']:<28} {when}  {c['size'] // 1024}K")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
