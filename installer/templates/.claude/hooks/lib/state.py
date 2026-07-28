#!/usr/bin/env python3
"""Dispatch state-file hygiene.

The dispatcher writes `.claude/state/current_*.txt` before each subagent and
deletes them after. If a build crashes or the session ends mid-task, those
files survive and mistag every later log line (post_tool_use.py reads them) as
belonging to the dead run. This module clears that leftover state.

Usage:
    state.py clear                 # remove all dispatch state files (teardown)
    state.py clear --agent         # remove task/role/model only, keep the run id
                                   # (between two subagents in one command)
    state.py check [--max-age N]   # report present/stale state (doctor); --json
    state.py clean [--max-age-days N] [--apply]
                                   # prune empty review dirs + run artifacts
                                   # older than N days (default 30). Dry-run
                                   # unless --apply.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import read_text_or_none  # noqa: E402

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

# Review outputs are grouped one dir per role, sharing a <review_id>. Commands
# mkdir these up front, so an aborted review leaves empty dirs behind.
REVIEW_BASES = ("audit", "design-review", "security-review", "ui-review", "pr-review")
# Run artifacts pruned by age.
AGED_BASES = ("plans", "logs", *REVIEW_BASES)
DEFAULT_CLEAN_MAX_AGE_DAYS = 30


# Files identifying the subagent that just ran, as opposed to the workflow it
# ran inside. A command that dispatches several agents clears these BETWEEN
# them, while `current_run.txt` stays put so every tool call keeps landing in
# the same run's log.
AGENT_FILES = ("current_task.txt", "current_role.txt", "current_model.txt")


def present(names: tuple[str, ...] | None = None) -> list[Path]:
    return [STATE_DIR / n for n in (names or STATE_FILES) if (STATE_DIR / n).exists()]


def clear(names: tuple[str, ...] | None = None) -> list[str]:
    """Delete dispatch state. Defaults to all of it (command teardown); pass
    AGENT_FILES to clear only the per-subagent files and keep the run context."""
    removed = []
    for p in present(names):
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
    run_id = read_text_or_none(STATE_DIR / "current_run.txt")
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


def _is_empty_dir(p: Path) -> bool:
    try:
        return p.is_dir() and not any(p.iterdir())
    except OSError:
        return False


def find_empty_review_dirs() -> list[Path]:
    """Empty per-review-id subdirs under the review bases — orphaned by an
    aborted review that mkdir'd its output dir but wrote no findings."""
    out: list[Path] = []
    for base in REVIEW_BASES:
        d = STATE_DIR / base
        if not d.is_dir():
            continue
        out.extend(sub for sub in d.iterdir() if _is_empty_dir(sub))
    return out


def _run_is_protected(run_dir: Path) -> bool:
    """A plan dir must never be age-pruned if it is the current run, or holds a
    `deferred` (parked) task — deleting it would break `/agentic-resume`."""
    current = read_text_or_none(STATE_DIR / "current_run.txt")
    if current and run_dir.name == current:
        return True
    for md in run_dir.glob("*.md"):
        try:
            if "status: deferred" in md.read_text(encoding="utf-8"):
                return True
        except OSError:
            pass
    return False


def find_old_runs(max_age_days: float) -> list[Path]:
    """Run artifacts (plan dirs, per-run logs, review-output dirs) whose mtime is
    older than `max_age_days`. Rotated/ambient logs are included by age too.
    Parked (deferred) and current-run plan dirs are protected regardless of age."""
    cutoff = time.time() - max_age_days * 86400
    out: list[Path] = []
    for base in AGED_BASES:
        d = STATE_DIR / base
        if not d.is_dir():
            continue
        for entry in d.iterdir():
            try:
                if entry.stat().st_mtime >= cutoff:
                    continue
            except OSError:
                continue
            if base == "plans" and entry.is_dir() and _run_is_protected(entry):
                continue
            out.append(entry)
    return out


def clean(max_age_days: float, apply: bool) -> dict:
    """Return (and optionally remove) prunable state: empty review dirs (any age)
    plus run artifacts older than `max_age_days`. Dedupes overlaps."""
    empties = find_empty_review_dirs()
    old = find_old_runs(max_age_days)
    targets: list[Path] = []
    seen: set[Path] = set()
    for p in empties + old:
        if p not in seen:
            seen.add(p)
            targets.append(p)

    removed: list[str] = []
    if apply:
        for p in targets:
            try:
                if p.is_dir():
                    shutil.rmtree(p)
                else:
                    p.unlink()
                removed.append(str(p.relative_to(STATE_DIR)))
            except OSError:
                pass
    return {
        "empty_dirs": [str(p.relative_to(STATE_DIR)) for p in empties],
        "aged": [str(p.relative_to(STATE_DIR)) for p in old],
        "candidates": [str(p.relative_to(STATE_DIR)) for p in targets],
        "removed": removed,
        "applied": apply,
        "max_age_days": max_age_days,
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    clr = sub.add_parser("clear")
    clr.add_argument("--agent", action="store_true",
                     help="clear only the per-subagent files (task/role/model), "
                          "keeping current_run.txt — use between dispatches "
                          "inside one command")
    chk = sub.add_parser("check")
    chk.add_argument("--max-age", type=float, default=DEFAULT_STALE_SECONDS)
    chk.add_argument("--json", action="store_true")
    cln = sub.add_parser("clean")
    cln.add_argument("--max-age-days", type=float, default=DEFAULT_CLEAN_MAX_AGE_DAYS)
    cln.add_argument("--apply", action="store_true", help="actually delete (default: dry-run)")
    cln.add_argument("--json", action="store_true")
    args = ap.parse_args()

    if args.cmd == "clean":
        report = clean(args.max_age_days, args.apply)
        if args.json:
            print(json.dumps(report))
        else:
            verb = "removed" if args.apply else "would remove"
            items = report["removed"] if args.apply else report["candidates"]
            if not items:
                print("nothing to clean")
            else:
                print(f"{verb} {len(items)} item(s) "
                      f"(empty review dirs + artifacts >{args.max_age_days:g}d old):")
                for it in items:
                    print(f"  {it}")
                if not args.apply:
                    print("\nre-run with --apply to delete.")
        sys.exit(0)

    if args.cmd == "clear":
        removed = clear(AGENT_FILES if args.agent else None)
        scope = "agent" if args.agent else "dispatch"
        if removed:
            print(f"cleared {scope} state: " + ", ".join(removed))
        else:
            print(f"no {scope} state to clear")
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
