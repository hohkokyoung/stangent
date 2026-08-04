#!/usr/bin/env python3
"""Stop hook — attribute the MAIN session's own token usage + cost.

`log_usage.py` covers subagents only. Nothing covered the orchestrator's own
thread, so `/agentic-logs` reported a fraction of a run's bill and the remainder
had no home: a build whose subagent events summed to well under what the run
actually cost looked like a pricing bug rather than what it was — the
dispatcher's own context, re-read on every turn of a long sequential loop.

That thread is not a rounding error. `/agentic-build` runs ~5-6 orchestrator
turns per task (reindex, state writes, dispatch, verification, checkpoint, then a
full re-emission of the dispatch plan), and every turn re-reads everything before
it. Un-measured, there was no way to tell whether a change to that loop helped.

Attribution: `task_id: null` + `agent_role: "orchestrator"`, which buckets under
`(session)` in `/agentic-logs` — the same bucket the dispatcher's own tool calls
already land in, since those run with no `current_role.txt` set.

## Why a cursor

Stop fires at the end of EVERY main-agent response, not once per run. Summing the
transcript each time would re-count every earlier turn, inflating the total
quadratically — the exact error this hook exists to expose. So we record how many
main-thread assistant records have been attributed and count only past it.

The cursor is keyed by transcript path: a new session starts a new count. It is
deliberately NOT in `state.py`'s clear list — that runs between dispatches and
per build, while this cursor tracks a *session*. Clearing it mid-session would
replay the whole transcript into the next event.

## Why sidechain records are excluded

Some Claude Code versions inline subagent turns into the main transcript as
`isSidechain: true`, and `log_usage.py` falls back to exactly those when no
`subagents/` file exists. Counting them here too would bill every subagent twice.

Fail-safe by contract: ANY error → exit 0 with no event. Telemetry must never
break a run.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))
from common import note_hook_error, read_jsonl, read_state  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
STATE_DIR = REPO_ROOT / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"
CURSOR = STATE_DIR / "main_usage_cursor.json"

HOOK_NAME = "stop_usage.py"


def main_turns(records: list[dict]) -> list[dict]:
    """The main session's own assistant turns, in transcript order.

    Excludes `isSidechain` records: those are subagent turns inlined into the
    main transcript, and `log_usage.py` already attributes them."""
    return [r for r in records
            if r.get("type") == "assistant"
            and not r.get("isSidechain")
            and (r.get("message") or {}).get("usage")]


def read_cursor(transcript: str) -> int:
    """Records already attributed for THIS transcript. A different transcript (or
    an unreadable/malformed cursor) starts from zero — under-counting a fresh
    session is recoverable, double-counting an old one is not."""
    try:
        data = json.loads(CURSOR.read_text(encoding="utf-8"))
        if isinstance(data, dict) and data.get("transcript") == transcript:
            n = data.get("counted")
            return n if isinstance(n, int) and n >= 0 else 0
    except (OSError, ValueError):
        pass
    return 0


def write_cursor(transcript: str, counted: int) -> None:
    try:
        CURSOR.parent.mkdir(parents=True, exist_ok=True)
        CURSOR.write_text(
            json.dumps({"transcript": transcript, "counted": counted}),
            encoding="utf-8")
    except OSError:
        pass  # a lost cursor costs one duplicate event, not a broken run


def sum_usage(turns: list[dict]) -> tuple[dict, str | None]:
    from token_cost import tokens_of  # local import so failures stay contained

    tot = {"input": 0, "output": 0, "cache_read": 0, "cache_write": 0}
    model = None
    for r in turns:
        msg = r.get("message") or {}
        t = tokens_of(msg.get("usage") or {})
        for k in tot:
            tot[k] += t[k]
        if msg.get("model"):
            model = msg["model"]
    return tot, model


def main() -> None:
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except json.JSONDecodeError:
        sys.exit(0)

    tx = payload.get("transcript_path")
    run_id = os.environ.get("AGENTIC_RUN_ID") or read_state(STATE_DIR, "current_run.txt")
    # No workflow context, or no transcript → nothing to attribute. Ambient
    # sessions outside a run are deliberately not logged: they have no run file
    # to land in, and inventing one would make `/agentic-logs` list non-runs.
    if not tx or not run_id:
        sys.exit(0)

    try:
        turns = main_turns(read_jsonl(tx))
        already = read_cursor(tx)
        new = turns[already:]
        if not new:
            sys.exit(0)  # nothing since the last Stop

        tokens, model = sum_usage(new)
        model = model or read_state(STATE_DIR, "current_model.txt") or ""

        from token_cost import cost_usd
        event = {
            "ts": dt.datetime.now(dt.timezone.utc).isoformat(
                timespec="seconds").replace("+00:00", "Z"),
            "event": "usage",
            "run_id": run_id,
            # The orchestrator is not a task. Null buckets it under "(session)"
            # in /agentic-logs, alongside its own untagged tool calls.
            "task_id": None,
            "agent_role": "orchestrator",
            "model": model,
            "turns": len(new),
            "tokens": tokens,
            "cost_usd": cost_usd(model, tokens),
        }
        LOG_DIR.mkdir(parents=True, exist_ok=True)
        with (LOG_DIR / f"{run_id}.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps(event, ensure_ascii=False) + "\n")
        # Advance only after the event is durable: a crash between the two
        # re-counts these turns next time, which is visible in the log. The
        # reverse order would drop them silently.
        write_cursor(tx, len(turns))
    except Exception as _e:
        note_hook_error(LOG_DIR, STATE_DIR, HOOK_NAME, _e)
    sys.exit(0)


if __name__ == "__main__":
    main()
