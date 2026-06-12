#!/usr/bin/env python3
"""Append a structured dispatch event to .claude/state/logs/dispatch.jsonl.

Called by dispatcher commands (agentic-build, agentic-refactor) before each
subagent invocation. Each line captures the model routing decision so the full
audit trail — role, task complexity, baseline model, and final model — is
queryable after a build run.

Usage:
    python3 .claude/hooks/lib/log_dispatch.py \
        --run_id FEAT-001 --task_id t1 --role implementer \
        --complexity medium --role_baseline claude-sonnet-4-6 \
        --model_selected claude-sonnet-4-6
    # add --routing_applied when the selected model differs from the baseline
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path


def main() -> None:
    ap = argparse.ArgumentParser(description="Append a dispatch event to dispatch.jsonl.")
    ap.add_argument("--run_id", default="")
    ap.add_argument("--task_id", default="")
    ap.add_argument("--role", default="")
    ap.add_argument("--complexity", default="medium", choices=["low", "medium", "high"])
    ap.add_argument("--role_baseline", default="", help="Model selected by role config before complexity routing")
    ap.add_argument("--model_selected", default="", help="Final model passed to the subagent")
    ap.add_argument("--routing_applied", action="store_true", help="Set when complexity routing changed the model")
    args = ap.parse_args()

    record = {
        "ts": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "event": "dispatch",
        "run_id": args.run_id or None,
        "task_id": args.task_id or None,
        "role": args.role or None,
        "complexity": args.complexity,
        "role_baseline": args.role_baseline or None,
        "model_selected": args.model_selected or None,
        "routing_applied": args.routing_applied,
    }

    log_path = Path.cwd() / ".claude" / "state" / "logs" / "dispatch.jsonl"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    with log_path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    main()
