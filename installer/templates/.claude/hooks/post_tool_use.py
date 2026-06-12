#!/usr/bin/env python3
"""JSONL logger for every tool call.

One line per tool call with:
  {ts, run_id, task_id, agent_role, tool, args, ok, deny_reason?}

`args` is a compact dict of short, human-readable values (NOT type names).
Multi-line strings are flattened. Long strings are truncated mid-value with
an ellipsis so they stay JSONL-friendly.

Log file path:
  .claude/state/logs/<run_id>.jsonl    when run_id is known
  .claude/state/logs/_no-run.jsonl     otherwise (ambient / setup tool calls)

run_id / task_id / agent_role / agent_model are read from state files written by
the dispatcher before each subagent call:
  .claude/state/current_run.txt   → run_id
  .claude/state/current_task.txt  → task_id
  .claude/state/current_role.txt  → agent_role
  .claude/state/current_model.txt → agent_model (the selected model after complexity routing)
Env vars AGENTIC_RUN_ID / AGENTIC_TASK_ID / AGENTIC_AGENT_ROLE / AGENTIC_AGENT_MODEL are
checked first as an override but are not set by any dispatcher in practice.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import re
import sys
from pathlib import Path

LOG_DIR = Path.cwd() / ".claude" / "state" / "logs"

MAX_VALUE_LEN = 120
MAX_KEYS = 6
SECRET_KEYS = {"password", "token", "secret", "api_key", "authorization", "bearer"}


def _short(value, key: str = "") -> object:
    """Return a JSON-safe short representation of `value`."""
    if value is None or isinstance(value, (bool, int, float)):
        return value
    if isinstance(value, str):
        if key.lower() in SECRET_KEYS:
            return "***"
        flat = re.sub(r"\s+", " ", value).strip()
        if len(flat) > MAX_VALUE_LEN:
            return flat[:MAX_VALUE_LEN - 1] + "…"
        return flat
    if isinstance(value, list):
        if not value:
            return []
        head = [_short(v, key) for v in value[:3]]
        if len(value) > 3:
            head.append(f"…(+{len(value) - 3})")
        return head
    if isinstance(value, dict):
        out = {}
        for k in list(value.keys())[:MAX_KEYS]:
            out[k] = _short(value[k], k)
        if len(value) > MAX_KEYS:
            out["…"] = f"+{len(value) - MAX_KEYS} keys"
        return out
    # fall back to safe repr
    return str(type(value).__name__)


def summarize(tool_input) -> dict:
    if not isinstance(tool_input, dict):
        return {}
    return {k: _short(v, k) for k, v in list(tool_input.items())[:MAX_KEYS]}


def main() -> None:
    raw = sys.stdin.buffer.read().decode("utf-8", errors="replace") or "{}"
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        sys.exit(0)

    tool = payload.get("tool_name") or payload.get("tool") or ""
    tool_input = payload.get("tool_input") or payload.get("input") or {}
    tool_response = payload.get("tool_response") or payload.get("output") or {}

    ok = True
    deny_reason = None
    if isinstance(tool_response, dict):
        if tool_response.get("is_error") or tool_response.get("error"):
            ok = False
        if "deny_reason" in tool_response:
            deny_reason = tool_response.get("deny_reason")

    def _read_state(filename: str) -> str | None:
        p = Path.cwd() / ".claude" / "state" / filename
        if not p.exists():
            return None
        try:
            return p.read_text(encoding="utf-8").strip() or None
        except Exception:
            return None

    run_id = os.environ.get("AGENTIC_RUN_ID") or _read_state("current_run.txt")
    task_id = os.environ.get("AGENTIC_TASK_ID") or _read_state("current_task.txt")
    agent_role = os.environ.get("AGENTIC_AGENT_ROLE") or _read_state("current_role.txt")
    agent_model = os.environ.get("AGENTIC_AGENT_MODEL") or _read_state("current_model.txt")
    line = {
        "ts": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "run_id": run_id,
        "task_id": task_id,
        "agent_role": agent_role,
        "model": agent_model,
        "tool": tool,
        "ok": ok,
        "args": summarize(tool_input),
    }
    if deny_reason:
        line["deny_reason"] = deny_reason

    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log_name = f"{run_id}.jsonl" if run_id else "_no-run.jsonl"
    out = LOG_DIR / log_name
    with out.open("a", encoding="utf-8") as f:
        f.write(json.dumps(line, ensure_ascii=False) + "\n")

    sys.exit(0)


if __name__ == "__main__":
    main()
