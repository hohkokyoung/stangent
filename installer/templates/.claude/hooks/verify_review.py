#!/usr/bin/env python3
"""SubagentStop hook — verify a reviewing agent's report the moment it finishes.

`verify_clears.py` already runs as a step inside every review command. That step
is an instruction to the orchestrator, so it can be skipped — silently, leaving
no trace, on exactly the run where it mattered. This hook closes that: the
harness fires it whether or not anyone wants it, and the verdict lands in the run
log as a permanent record.

It does not block. SubagentStop cannot usefully fail a run, and blocking is the
wrong lever anyway — the point is that a report which does not verify can never
*quietly* pass. `/agentic-logs` surfaces the event, and a review whose evidence
did not reproduce is on the record next to its cost.

Emits one `verification` event to logs/<run_id>.jsonl:
  {ts, event:"verification", run_id, task_id, agent_role, report,
   failing, reproduced, coverage, exit}

Fail-safe by contract: ANY error → exit 0 with no event, like every other hook
here. Verification telemetry must never break a run.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))
from common import last_logged_context, note_hook_error, read_state  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
STATE_DIR = REPO_ROOT / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"
AGENTS = REPO_ROOT / ".claude" / "agents"

# role → (report location relative to state/, checklist source).
# `reviewer` is absent on purpose: its report is the task file, whose path needs
# the task id, so it is resolved separately below.
REPORTS = {
    "design-critic":     ("ui-review",       REPO_ROOT / "docs" / "design" / "DESIGN-SPEC.md"),
    "security-reviewer": ("security-review", AGENTS / "security-reviewer.md"),
    "architect":         ("design-review",   AGENTS / "architect.md"),
    "auditor":           ("audit",           AGENTS / "auditor.md"),
}

# Roles whose checklist enumerates SITES, so a declared search applies. architect
# is absent for the same reason it gets no enumerations anywhere else: its
# dimensions are questions about a design, with no population to search.
#
# This hook is the check that cannot be skipped — a review command's own verify
# step is an instruction, and one was skipped on a real run while this fired
# anyway. Leaving the enumeration comparison out of it meant the un-skippable
# check was missing the newest thing worth checking.
ENUMERATED_ROLES = ("design-critic", "security-reviewer", "auditor")
ENUMERATIONS = REPO_ROOT / "docs" / "review" / "enumerations.md"


def resolve_report(role: str, run_id: str, task_id: str | None) -> tuple[Path, Path] | None:
    """(report_path, checklist_path) for a reviewing role, or None."""
    if role == "reviewer":
        if not task_id:
            return None
        p = STATE_DIR / "plans" / run_id / f"{task_id}.md"
        return (p, AGENTS / "reviewer.md") if p.is_file() else None
    entry = REPORTS.get(role)
    if not entry:
        return None
    subdir, checklist = entry
    p = STATE_DIR / subdir / run_id / "findings.md"
    return (p, checklist) if p.is_file() else None


HOOK_NAME = "verify_review.py"


def _state(name: str) -> str | None:
    return read_state(STATE_DIR, name)


def _hook_error(exc: Exception) -> None:
    note_hook_error(LOG_DIR, STATE_DIR, HOOK_NAME, exc)


def main() -> None:
    try:
        json.loads(sys.stdin.read() or "{}")
    except json.JSONDecodeError:
        sys.exit(0)

    run_id = os.environ.get("AGENTIC_RUN_ID") or _state("current_run.txt")
    if not run_id:
        sys.exit(0)

    try:
        role = _state("current_role.txt")
        task_id = _state("current_task.txt")
        if role is None or task_id is None:
            last = last_logged_context(LOG_DIR / f"{run_id}.jsonl")
            role = role or last.get("agent_role")
            task_id = task_id or last.get("task_id")
        if not role:
            sys.exit(0)

        resolved = resolve_report(role, run_id, task_id)
        if not resolved:
            sys.exit(0)  # not a reviewing role, or it wrote no report
        report, checklist = resolved

        sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))
        from verify_clears import verify, is_failing

        rep = verify(report, REPO_ROOT,
                     checklist=checklist if checklist.is_file() else None,
                     enumerations=ENUMERATIONS if role in ENUMERATED_ROLES else None,
                     reviewer=role if role in ENUMERATED_ROLES else "")
        results = rep.get("results", [])
        failing = [r for r in results if is_failing(r)]
        cov = rep.get("coverage") or {}
        cov_bad = cov.get("status") in ("missing", "incomplete")

        event = {
            "ts": dt.datetime.now(dt.timezone.utc).isoformat(
                timespec="seconds").replace("+00:00", "Z"),
            "event": "verification",
            "run_id": run_id,
            "task_id": task_id,
            "agent_role": role,
            "report": str(report.relative_to(REPO_ROOT)),
            "reproduced": sum(1 for r in results if r["status"] == "reproduced"),
            "failing": len(failing),
            "detail": [f"{r['status']}: {r['item'][:60]}" for r in failing[:5]],
            "coverage": cov.get("status"),
            "coverage_detail": cov.get("detail"),
            "partial": len(rep.get("partial") or []),
            "exit": 1 if (failing or cov_bad) else 0,
        }
        LOG_DIR.mkdir(parents=True, exist_ok=True)
        with (LOG_DIR / f"{run_id}.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps(event, ensure_ascii=False) + "\n")
    except Exception as _e:
        _hook_error(_e)  # never break a run
    sys.exit(0)


if __name__ == "__main__":
    main()
