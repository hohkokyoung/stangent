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

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import read_jsonl  # noqa: E402

STATE_DIR = Path.cwd().resolve() / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"
PLANS_DIR = STATE_DIR / "plans"
DISPATCH_LOG = LOG_DIR / "dispatch.jsonl"

RETRIEVE_TOOL = "mcp__agentic_mcp__retrieve"
SYMBOL_TOOL = "mcp__agentic_mcp__get_symbol"

# How many "heaviest result" calls to show. Enough to spot a pattern (the same
# unbounded grep three times), short enough to stay scannable.
HEAVY_CALLS = 8


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


def _aggregate_usage(events: list[dict]) -> dict:
    """task_id → summed {input,output,cache_read,cache_write,cost,turns,role} from
    the usage events — SubagentStop (log_usage.py) for each dispatched agent, and
    Stop (stop_usage.py) for the orchestrator's own thread. Empty if telemetry
    hasn't run.

    `role` is carried through so the `(session)` bucket can be labelled: the
    orchestrator's own tool calls run with no `current_role.txt`, so without this
    the row holding the dispatcher's entire cost printed a bare `-`."""
    by_task: dict[str, dict] = {}
    for e in events:
        tid = e.get("task_id") or "(session)"
        agg = by_task.setdefault(tid, {"input": 0, "output": 0, "cache_read": 0,
                                       "cache_write": 0, "cost": 0.0, "turns": 0,
                                       "role": None})
        tk = e.get("tokens") or {}
        for k in ("input", "output", "cache_read", "cache_write"):
            agg[k] += int(tk.get(k, 0) or 0)
        agg["cost"] += float(e.get("cost_usd", 0) or 0)
        agg["turns"] += int(e.get("turns", 0) or 0)
        if agg["role"] is None and e.get("agent_role"):
            agg["role"] = e["agent_role"]
    return by_task


def summarize(run_id: str) -> dict:
    raw = read_jsonl(LOG_DIR / f"{run_id}.jsonl")
    budget = [c for c in raw if c.get("event") == "budget"]
    hook_errors = [c for c in raw if c.get("event") == "hook_error"]
    verifications = [c for c in raw if c.get("event") == "verification"]
    calls = [c for c in raw if not c.get("event")]
    usage_by_task = _aggregate_usage([c for c in raw if c.get("event") == "usage"])
    dispatches = [d for d in read_jsonl(DISPATCH_LOG) if d.get("run_id") == run_id]
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
                "get_symbol": 0, "denials": 0, "failures": 0, "res_chars": 0,
                "first": None, "last": None, "events": [],
            }
            order.append(tid)
        t["calls"] += 1
        t["res_chars"] += int(c.get("res_chars") or 0)
        if c.get("tool") == RETRIEVE_TOOL:
            t["retrieve"] += 1
        if c.get("tool") == SYMBOL_TOOL:
            t["get_symbol"] += 1
        if c.get("deny_reason"):
            t["denials"] += 1
            t["events"].append(("deny", tid, c.get("tool"), c.get("deny_reason")))
        if c.get("ok") is False:
            t["failures"] += 1
            t["events"].append(("fail", tid, c.get("tool"), c.get("error") or "tool error"))
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
        use = usage_by_task.get(tid, {})
        task_list.append({
            "task_id": tid,
            "role": t["role"] or d.get("role") or use.get("role"),
            "model": t["model"] or d.get("model_selected"),
            "status": statuses.get(tid, "-"),
            "calls": t["calls"], "retrieve": t["retrieve"],
            "get_symbol": t["get_symbol"], "denials": t["denials"],
            "failures": t["failures"], "duration_s": dur,
            "res_chars": t["res_chars"],
            "routing_applied": d.get("routing_applied", False),
            "tokens": {k: use.get(k, 0) for k in ("input", "output", "cache_read", "cache_write")},
            "cost_usd": round(use.get("cost", 0.0), 4),
        })

    # Usage recorded for a task with no tool-call lines (rare) — still surface it.
    for tid, use in usage_by_task.items():
        if tid not in order:
            task_list.append({
                "task_id": tid, "role": use.get("role"), "model": None,
                "status": statuses.get(tid, "-"), "calls": 0, "retrieve": 0,
                "get_symbol": 0, "denials": 0, "failures": 0, "duration_s": None,
                "res_chars": 0, "routing_applied": False,
                "tokens": {k: use.get(k, 0) for k in ("input", "output", "cache_read", "cache_write")},
                "cost_usd": round(use.get("cost", 0.0), 4),
            })

    def _tok_sum(key):
        return sum(t["tokens"][key] for t in task_list)

    # The calls that put the most into context. Cost here is cache_read —
    # context resident × turns — so a call returning 80k chars on turn 5 is paid
    # for on every turn after it. Per-task totals say which task was expensive;
    # this says which CALL made it expensive, which is the actionable part.
    heavy = sorted(
        (c for c in calls if c.get("res_chars")),
        key=lambda c: c.get("res_chars") or 0, reverse=True)[:HEAVY_CALLS]

    return {
        "run_id": run_id,
        "tasks": task_list,
        "events": all_events,
        # `axis` defaults to calls: events predating the result_chars axis have no
        # such key, and every one of those was a call-count warning.
        "hook_errors": [{"hook": h.get("hook"), "error": h.get("error")}
                        for h in hook_errors],
        "budget": [{"task_id": b.get("task_id"), "calls": b.get("calls"),
                    "res_chars": b.get("res_chars"),
                    "axis": b.get("axis") or "calls",
                    "threshold": b.get("threshold")} for b in budget],
        "verifications": [{"agent_role": v.get("agent_role"),
                           "report": v.get("report"),
                           "reproduced": v.get("reproduced"),
                           "failing": v.get("failing"),
                           "coverage": v.get("coverage"),
                           "exit": v.get("exit")} for v in verifications],
        "heavy_calls": [{
            "task_id": c.get("task_id") or "(session)",
            "tool": c.get("tool"),
            "res_chars": c.get("res_chars"),
            "args": c.get("args") or {},
        } for c in heavy],
        "totals": {
            "tasks": len(task_list),
            "calls": sum(t["calls"] for t in task_list),
            "denials": sum(t["denials"] for t in task_list),
            "failures": sum(t["failures"] for t in task_list),
            "retrieve": sum(t["retrieve"] for t in task_list),
            "get_symbol": sum(t["get_symbol"] for t in task_list),
            "cost_usd": round(sum(t["cost_usd"] for t in task_list), 4),
            "tokens": {k: _tok_sum(k) for k in ("input", "output", "cache_read", "cache_write")},
            "res_chars": sum(t["res_chars"] for t in task_list),
        },
        "has_usage": bool(usage_by_task),
        "started": run_first.isoformat() if run_first else None,
        "ended": run_last.isoformat() if run_last else None,
        "duration_s": (run_last - run_first).total_seconds() if run_first and run_last else None,
        "has_logs": bool(raw),
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


def _fmt_tok(n: int) -> str:
    if not n:
        return "-"
    if n >= 1000:
        return f"{n / 1000:.0f}k"
    return str(n)


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
    if tot.get("res_chars"):
        print(f"  tool results into context: {_fmt_tok(tot['res_chars'])} chars "
              f"(~{_fmt_tok(tot['res_chars'] // 4)} tokens)")
    has_usage = rep.get("has_usage")
    if has_usage:
        tk = tot["tokens"]
        billable = tk["input"] + tk["output"] + tk["cache_write"]
        cache_pct = (100 * tk["cache_read"] / (tk["cache_read"] + billable)) if (tk["cache_read"] + billable) else 0
        print(f"  cost: ${tot['cost_usd']:.2f}   tokens: in {_fmt_tok(tk['input'])} "
              f"out {_fmt_tok(tk['output'])} cache-read {_fmt_tok(tk['cache_read'])} "
              f"cache-write {_fmt_tok(tk['cache_write'])}   cache-hit {cache_pct:.0f}%")
    print()
    extra = f"{'tok':>7}{'cost':>8}" if has_usage else ""
    print(f"  {'task':<14}{'role':<16}{'model':<14}{'status':<9}{'calls':>6}"
          f"{'ret':>5}{'sym':>5}{'deny':>6}{'fail':>6}{'res':>7}{'dur':>7}{extra}")
    for t in rep["tasks"]:
        row = (f"  {str(t['task_id']):<14}{str(t['role'] or '-'):<16}"
               f"{_short_model(t['model']):<14}{str(t['status']):<9}"
               f"{t['calls']:>6}{t['retrieve']:>5}{t['get_symbol']:>5}"
               f"{t['denials']:>6}{t['failures']:>6}"
               f"{_fmt_tok(t.get('res_chars', 0)):>7}{_fmt_dur(t['duration_s']):>7}")
        if has_usage:
            tokt = t["tokens"]["input"] + t["tokens"]["output"]
            row += f"{_fmt_tok(tokt):>7}{('$' + format(t['cost_usd'], '.2f')):>8}"
        print(row)
    if rep.get("heavy_calls"):
        print("\n  heaviest results (what filled the context):")
        for h in rep["heavy_calls"]:
            arg = ""
            for k in ("command", "file_path", "query", "pattern", "description"):
                if h["args"].get(k):
                    arg = re.sub(r"\s+", " ", str(h["args"][k]))[:70]
                    break
            print(f"    {_fmt_tok(h['res_chars']):>7}  {h['task_id']:<12}"
                  f"{str(h['tool']):<28}{arg}")
    if rep.get("verifications"):
        print("\n  report verification (SubagentStop — cannot be skipped):")
        for v in rep["verifications"]:
            mark = "FAIL" if v.get("exit") else " ok "
            cov = f"  coverage: {v['coverage']}" if v.get("coverage") else ""
            print(f"    [{mark}] {str(v['agent_role']):<18} "
                  f"reproduced {v.get('reproduced', 0)}, failing {v.get('failing', 0)}{cov}")
            if v.get("exit"):
                print(f"           {v['report']} — evidence did not re-derive; "
                      "treat those items as unreviewed")
    if rep.get("hook_errors"):
        print("\n  hooks that failed (telemetry below is incomplete):")
        for h in rep["hook_errors"]:
            print(f"    [FAIL] {h['hook']}: {h['error']}")
    if rep.get("budget"):
        print("\n  budget thresholds crossed:")
        for b in rep["budget"]:
            if b.get("axis") == "result_chars":
                thresh = _fmt_tok(b.get("threshold") or 0)
                print(f"    {b['task_id']}  results passed {thresh} chars "
                      f"(at call {b['calls']}) — the same edit repeated across "
                      "sites; a mechanical change should have been scripted")
            else:
                res = _fmt_tok(b.get("res_chars") or 0)
                print(f"    {b['task_id']}  crossed {b['threshold']} calls "
                      f"(results {res} chars) — long-running")
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
