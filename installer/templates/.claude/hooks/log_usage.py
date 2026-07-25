#!/usr/bin/env python3
"""SubagentStop hook — attribute a finished subagent's token usage + cost.

When a dispatched agent (a Task subagent) finishes, Claude Code fires
SubagentStop with the MAIN session `transcript_path`. The subagent's own turns
are NOT in that file — Claude Code writes them to a separate transcript:

    <main-transcript-without-.jsonl>/subagents/agent-<id>.jsonl

So we derive that `subagents/` dir and take the most-recently-modified file (the
just-finished subagent; v1 dispatch is sequential), sum its assistant-message
`message.usage`, and attribute the total to the active workflow context
(current_run/task/role, still set when this hook fires).

Appends one `usage` event to logs/<run_id>.jsonl:
  {ts, event:"usage", run_id, task_id, agent_role, model, turns, tokens{...}, cost_usd}

Fail-safe by contract: ANY error → exit 0 with no event. Telemetry must never
break a run. If no context is active or no subagent transcript is found, nothing
is written.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))
from common import read_jsonl, read_text_or_none  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
STATE_DIR = REPO_ROOT / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"


def _read_state(name: str) -> str | None:
    return read_text_or_none(STATE_DIR / name)


def subagent_usage(records: list[dict]) -> tuple[dict, str | None, int]:
    """Sum assistant-message usage across a subagent transcript (the whole file is
    one subagent's conversation). Returns (tokens, model, turns); turns==0 means
    nothing to attribute."""
    from token_cost import tokens_of  # local import so failures stay contained

    tot = {"input": 0, "output": 0, "cache_read": 0, "cache_write": 0}
    model = None
    turns = 0
    for r in records:
        if r.get("type") != "assistant":
            continue
        msg = r.get("message") or {}
        usage = msg.get("usage")
        if not usage:
            continue
        t = tokens_of(usage)
        for k in tot:
            tot[k] += t[k]
        if msg.get("model"):
            model = msg["model"]
        turns += 1
    return tot, model, turns


def resolve_subagent_transcript(transcript_path: str) -> Path | None:
    """Locate the just-finished subagent's transcript.

    SubagentStop passes the MAIN transcript_path; the subagent's turns live in
    `<main-without-.jsonl>/subagents/agent-*.jsonl`. Take the newest one (dispatch
    is sequential). If the path already points inside a subagents/ dir, use it."""
    p = Path(transcript_path)
    if "subagents" in p.parts:
        return p if p.is_file() else None
    sub_dir = p.with_suffix("") / "subagents"
    if not sub_dir.is_dir():
        return None
    files = sorted(sub_dir.glob("*.jsonl"), key=lambda f: f.stat().st_mtime, reverse=True)
    return files[0] if files else None


def main() -> None:
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except json.JSONDecodeError:
        sys.exit(0)

    tx = payload.get("transcript_path")
    run_id = os.environ.get("AGENTIC_RUN_ID") or _read_state("current_run.txt")
    # No workflow context, or no transcript → nothing to attribute.
    if not tx or not run_id:
        sys.exit(0)

    try:
        sub = resolve_subagent_transcript(tx)
        records = read_jsonl(sub) if sub else []
        tokens, model, turns = subagent_usage(records)
        # Fallback for versions that inline subagent turns in the main transcript
        # as isSidechain=true, rather than in a subagents/ subfolder.
        if turns == 0:
            tokens, model, turns = subagent_usage(
                [r for r in read_jsonl(tx) if r.get("isSidechain")])
        if turns == 0:
            sys.exit(0)
        model = model or _read_state("current_model.txt") or ""
        from token_cost import cost_usd
        event = {
            "ts": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
            "event": "usage",
            "run_id": run_id,
            "task_id": _read_state("current_task.txt"),
            "agent_role": _read_state("current_role.txt"),
            "model": model,
            "turns": turns,
            "tokens": tokens,
            "cost_usd": cost_usd(model, tokens),
        }
        LOG_DIR.mkdir(parents=True, exist_ok=True)
        with (LOG_DIR / f"{run_id}.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps(event, ensure_ascii=False) + "\n")
    except Exception:
        pass  # telemetry must never break a run
    sys.exit(0)


if __name__ == "__main__":
    main()
