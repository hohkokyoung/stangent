"""<case name> — <one-line description>.

Helpers available (injected by run.py at import time):
  parse_frontmatter(path) -> dict
  load_tasks(run_dir) -> list[{path, frontmatter}]
  list_task_files(run_dir) -> list[Path]
  overview_text(run_dir) -> str
"""
from pathlib import Path


def check(run_dir: Path) -> list[str]:
    """Return [] on pass, or a list of human-readable failure messages."""
    failures: list[str] = []

    tasks = load_tasks(run_dir)  # type: ignore[name-defined]

    # Example structural checks — adapt to the case:
    if len(tasks) == 0:
        failures.append("expected at least 1 task, got 0")

    # if not (run_dir / "_overview.md").exists():
    #     failures.append("_overview.md missing")

    # for t in tasks:
    #     fm = t["frontmatter"]
    #     if fm.get("status") != "pending":
    #         failures.append(f"{t['path'].name}: status expected 'pending', got {fm.get('status')!r}")

    return failures
