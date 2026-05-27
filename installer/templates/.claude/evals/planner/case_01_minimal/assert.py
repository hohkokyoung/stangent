"""case_01_minimal — trivial ask, expect ≤2 tasks, no questions, fastapi skill only.

Helpers available (injected by run.py):
  parse_frontmatter(path) -> dict
  load_tasks(run_dir) -> list[{path, frontmatter}]
  list_task_files(run_dir) -> list[Path]
  overview_text(run_dir) -> str
"""
from pathlib import Path


def check(run_dir: Path) -> list[str]:
    failures: list[str] = []

    tasks = load_tasks(run_dir)  # type: ignore[name-defined]
    n = len(tasks)
    if n == 0:
        failures.append("expected 1-2 tasks, got 0 (planner emitted nothing)")
    elif n > 2:
        failures.append(f"over-decomposed: expected ≤2 tasks for a trivial ask, got {n}")

    if not (run_dir / "_overview.md").exists():
        failures.append("_overview.md missing")

    impl = [t for t in tasks if t["frontmatter"].get("role") == "implementer"]
    if len(impl) != 1:
        failures.append(f"expected exactly 1 implementer task, got {len(impl)}")

    for t in impl:
        fm = t["frontmatter"]
        skills = fm.get("skills_to_load")
        if skills != ["fastapi"]:
            failures.append(f"{t['path'].name}: skills_to_load expected [fastapi], got {skills!r}")
        adrs = fm.get("adrs")
        if adrs not in (None, []):
            failures.append(f"{t['path'].name}: adrs expected [] (no ADRs in eval env), got {adrs!r}")
        if fm.get("status") != "pending":
            failures.append(f"{t['path'].name}: status expected 'pending', got {fm.get('status')!r}")

    return failures
