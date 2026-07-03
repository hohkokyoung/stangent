#!/usr/bin/env python3
"""Dispatch state-file hygiene.

The dispatcher writes `.claude/state/current_*.txt` before each subagent and
deletes them after. If a build crashes or the session ends mid-task, those
files survive and mistag every later log line (post_tool_use.py reads them) as
belonging to the dead run. This module clears that leftover state.

Usage:
    state.py clear                 # remove all dispatch state files (build start)
    state.py check [--max-age N]   # report present/stale state (doctor); --json
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

STATE_DIR = Path.cwd().resolve() / ".claude" / "state"
STATE_FILES = [
    "current_run.txt",
    "current_task.txt",
    "current_role.txt",
    "current_model.txt",
]
# Present state older than this (no per-task rewrite in that long) is leftover
# from a crash, not an in-flight dispatch.
DEFAULT_STALE_SECONDS = 1800


def present() -> list[Path]:
    return [STATE_DIR / n for n in STATE_FILES if (STATE_DIR / n).exists()]


def clear() -> list[str]:
    removed = []
    for p in present():
        try:
            p.unlink()
            removed.append(p.name)
        except OSError:
            pass
    return removed


def _latest_activity() -> float | None:
    """Newest mtime across present state files AND the CURRENT run's log.

    The dispatcher writes `current_run.txt` once at build start and
    `current_task.txt` once per task, but post_tool_use.py appends to
    `logs/<run>.jsonl` on every tool call. So a long-running task keeps that log
    fresh even though the state files are old — using it avoids flagging an
    active build as stale. We look ONLY at the current run's log (from
    `current_run.txt`), not `_no-run.jsonl` or other runs, so ambient tool use
    elsewhere in the session can't mask genuinely leftover state.
    """
    times: list[float] = []
    for p in present():
        try:
            times.append(p.stat().st_mtime)
        except OSError:
            pass
    try:
        run_id = (STATE_DIR / "current_run.txt").read_text(encoding="utf-8").strip()
    except OSError:
        run_id = ""
    if run_id:
        run_log = STATE_DIR / "logs" / f"{run_id}.jsonl"
        try:
            times.append(run_log.stat().st_mtime)
        except OSError:
            pass
    return max(times) if times else None


def find_stale(max_age: float = DEFAULT_STALE_SECONDS) -> list[dict]:
    """Present state files are stale only if there has been no dispatch activity
    (state-file or log write) within `max_age` — i.e. no build is mid-flight."""
    files = present()
    if not files:
        return []
    latest = _latest_activity()
    now = time.time()
    if latest is not None and (now - latest) <= max_age:
        return []
    out = []
    for p in files:
        try:
            out.append({"file": p.name, "age_seconds": int(now - p.stat().st_mtime)})
        except OSError:
            pass
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("clear")
    chk = sub.add_parser("check")
    chk.add_argument("--max-age", type=float, default=DEFAULT_STALE_SECONDS)
    chk.add_argument("--json", action="store_true")
    args = ap.parse_args()

    if args.cmd == "clear":
        removed = clear()
        if removed:
            print("cleared leftover dispatch state: " + ", ".join(removed))
        else:
            print("no leftover dispatch state")
        sys.exit(0)

    # check
    stale = find_stale(args.max_age)
    if args.json:
        print(json.dumps({"present": [p.name for p in present()], "stale": stale}))
    else:
        if stale:
            for s in stale:
                print(f"stale: {s['file']} ({s['age_seconds']}s old)")
        else:
            print("no stale dispatch state")
    sys.exit(0)


if __name__ == "__main__":
    main()
