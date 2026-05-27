"""case_02_ambiguous — under-specified ask; planner must either ask
or block, never silently assume.

Asserter cannot directly observe AskUserQuestion calls (those happen
in the agent's runtime, not the run dir). We infer from artifacts:

  - Resolved Questions section in _overview.md  → planner asked
  - Assumptions section listing >=1 assumption   → planner documented its leap
  - _overview.md status: blocked + Open Questions → planner gave up cleanly
  - none of the above + tasks emitted             → FAIL (silent assumption)
"""
from pathlib import Path


def check(run_dir: Path) -> list[str]:
    failures: list[str] = []

    if not (run_dir / "_overview.md").exists():
        return ["_overview.md missing — planner did not write anything"]

    ov = overview_text(run_dir)  # type: ignore[name-defined]
    tasks = load_tasks(run_dir)  # type: ignore[name-defined]

    has_resolved   = "## Resolved Questions" in ov
    has_assumptions = "## Assumptions" in ov and "ASSUMPTION:" in ov
    has_open_qs    = "## Open Questions" in ov
    is_blocked     = "status: blocked" in ov.split("---")[1] if "---" in ov else False

    # Outcome B — gave up cleanly
    if is_blocked and has_open_qs and not tasks:
        return []

    # Outcome A — asked then produced
    if tasks and (has_resolved or has_assumptions):
        return []

    # Anything else is a failure
    if tasks and not has_resolved and not has_assumptions:
        failures.append(
            "planner emitted tasks for under-specified input WITHOUT recording "
            "resolved questions or explicit assumptions"
        )
    if not tasks and not is_blocked:
        failures.append("no tasks emitted but _overview.md status is not 'blocked'")
    if not tasks and not has_open_qs:
        failures.append("no tasks emitted but no '## Open Questions' section in _overview.md")
    return failures
