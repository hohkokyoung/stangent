#!/usr/bin/env python3
"""SubagentStop hook — attribute a finished subagent's token usage + cost.

When a subagent (a dispatched agent = Task subagent) finishes, Claude Code fires
SubagentStop with the session `transcript_path`. Subagent turns are marked
`isSidechain: true` in that transcript. Because v1 dispatch is sequential, the
just-finished subagent's turns are the trailing contiguous sidechain block — so
we sum their `message.usage` (stateless, no cursor) and attribute the total to
the active workflow context (current_run/task/role, still set at this point).

Appends one `usage` event to logs/<run_id>.jsonl:
  {ts, event:"usage", run_id, task_id, agent_role, model, turns, tokens{...}, cost_usd}

Fail-safe by contract: ANY error → exit 0 with no event. Telemetry must never
break a run. If no context is active, or the transcript has no sidechain turns,
nothing is written.
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


def trailing_sidechain_usage(records: list[dict]) -> tuple[dict, str | None, int]:
    """Sum usage of the final contiguous run of isSidechain records.

    Returns (tokens, model, turns). `turns` is the count of assistant messages
    summed — 0 means no attributable subagent block was found.
    """
    from token_cost import tokens_of  # local import so failures stay contained

    last_main = -1
    for i, r in enumerate(records):
        if not r.get("isSidechain"):
            last_main = i
    block = records[last_main + 1:]

    tot = {"input": 0, "output": 0, "cache_read": 0, "cache_write": 0}
    model = None
    turns = 0
    for r in block:
        if r.get("type") != "assistant":
            continue
        msg = r.get("message") or {}
        t = tokens_of(msg.get("usage") or {})
        for k in tot:
            tot[k] += t[k]
        if msg.get("model"):
            model = msg["model"]
        turns += 1
    return tot, model, turns


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
        records = read_jsonl(tx)
        tokens, model, turns = trailing_sidechain_usage(records)
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
