#!/usr/bin/env python3
"""JSONL logger for every tool call.

One line per tool call with:
  {ts, run_id, task_id, agent_role, tool, args, ok, deny_reason?}

`args` is a compact dict of short, human-readable values (NOT type names).
Multi-line strings are flattened. Long strings are truncated mid-value with
an ellipsis so they stay JSONL-friendly.

Log file path:
  .claude/state/logs/<run_id>.jsonl    when a workflow context is active
  .claude/state/logs/_no-run.jsonl     only if AGENTIC_LOG_AMBIENT=1 (opt-in)

`run_id` is the WORKFLOW/LOG CONTEXT — set by every agentic command around its
agent dispatch, not just /agentic-build: build → FEAT-NNN, reviews → SEC-/DR-/
UIR-/PR-, debug → DBG-, design → DS-, baseline tests → baseline-<flow>. This is
what makes logging uniform: any command that dispatches an agent writes
current_run.txt, so its tool calls are captured under that context. Ambient
tool use (no command active) has no context and is skipped by default.

run_id / task_id / agent_role / agent_model are read from state files written by
the command before each subagent call:
  .claude/state/current_run.txt   → run_id (workflow/log context)
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

# Derive the repo root from THIS file's location, not the process cwd — the hook
# runs with an unreliable cwd, so a cwd-based path wrote state into whatever
# subdirectory happened to be current. __file__ is
# <repo>/.claude/hooks/post_tool_use.py → parents[2].
REPO_ROOT = Path(__file__).resolve().parents[2]
STATE_DIR = REPO_ROOT / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"

MAX_VALUE_LEN = 120
MAX_KEYS = 6
# A single log past this is rotated to `<name>.1.jsonl` so no log grows without
# bound (a long build's run log, or an opted-in ambient log).
MAX_LOG_BYTES = 5 * 1024 * 1024
# Normalized secret markers: matched as substrings of the separator-stripped,
# lowercased key, so `access_token`, `apiKey`, `x-api-key`, and
# `GITHUB_PERSONAL_ACCESS_TOKEN` all redact — not just the bare words. Kept
# specific (e.g. `apikey`/`privatekey`, never a bare `key`) so common,
# non-sensitive keys like `file_path` are not over-redacted.
SECRET_MARKERS = (
    "password", "passwd", "token", "secret", "apikey", "authorization",
    "bearer", "credential", "privatekey", "clientsecret", "sessionkey",
)


def _is_secret_key(key: str) -> bool:
    norm = re.sub(r"[^a-z0-9]", "", key.lower())
    return any(marker in norm for marker in SECRET_MARKERS)


def _short(value, key: str = "") -> object:
    """Return a JSON-safe short representation of `value`."""
    if value is None or isinstance(value, (bool, int, float)):
        return value
    if isinstance(value, str):
        if _is_secret_key(key):
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


def _ambient_logging_enabled() -> bool:
    return os.environ.get("AGENTIC_LOG_AMBIENT", "").strip().lower() in ("1", "true", "yes")


def _rotate_if_large(path: Path) -> None:
    """Keep any single log bounded: past MAX_LOG_BYTES, roll it to one prior
    generation (`<name>.1.jsonl`, overwriting an older roll) and start fresh."""
    try:
        if path.exists() and path.stat().st_size > MAX_LOG_BYTES:
            path.replace(path.parent / (path.stem + ".1.jsonl"))
    except OSError:
        pass


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
    error_text = None
    if isinstance(tool_response, dict):
        if tool_response.get("is_error") or tool_response.get("error"):
            ok = False
            # Capture a short reason so a failed run is diagnosable from the log,
            # not just "ok:false". Prefer an explicit error string; fall back to a
            # stringified content/response snippet.
            err = tool_response.get("error")
            if not isinstance(err, str):
                err = tool_response.get("content") or tool_response.get("stderr") or ""
            error_text = _short(err, "error") if err else None
        if "deny_reason" in tool_response:
            deny_reason = tool_response.get("deny_reason")

    # Kept inline (not shared via lib/common) on purpose: this hook fires on
    # EVERY tool call and its test runs it copied alone into a temp dir, so it
    # stays self-contained with no lib import to break.
    def _read_state(filename: str) -> str | None:
        p = STATE_DIR / filename
        if not p.exists():
            return None
        try:
            return p.read_text(encoding="utf-8").strip() or None
        except Exception:
            return None

    run_id = os.environ.get("AGENTIC_RUN_ID") or _read_state("current_run.txt")

    # Audit agentic work, not the whole project. Every agentic command sets a
    # workflow context (current_run.txt) around its dispatch, so a missing run_id
    # means no command is active — ambient general dev. Logging that made
    # `_no-run.jsonl` an unbounded record of ALL Claude Code use in the repo, so
    # skip it unless the user opts in with AGENTIC_LOG_AMBIENT=1.
    if not run_id and not _ambient_logging_enabled():
        sys.exit(0)

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
    if error_text:
        line["error"] = error_text

    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log_name = f"{run_id}.jsonl" if run_id else "_no-run.jsonl"
    out = LOG_DIR / log_name
    _rotate_if_large(out)
    with out.open("a", encoding="utf-8") as f:
        f.write(json.dumps(line, ensure_ascii=False) + "\n")

    sys.exit(0)


if __name__ == "__main__":
    main()
