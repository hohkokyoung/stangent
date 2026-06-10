"""case_01_status_lifecycle — implementer must flip status to done, fill Design and Decisions log.

Helpers available (injected by run.py):
  parse_frontmatter(path) -> dict
  load_tasks(run_dir) -> list[{path, frontmatter}]
  list_task_files(run_dir) -> list[Path]
  overview_text(run_dir) -> str
"""
import re
from pathlib import Path


def _section_body(text: str, heading: str) -> str:
    """Return text between `## heading` and the next `##` (or EOF), stripped."""
    pattern = rf"## {re.escape(heading)}\n(.*?)(?=\n## |\Z)"
    m = re.search(pattern, text, re.DOTALL)
    if not m:
        return ""
    # Strip template comments and blank lines to see if real content exists.
    lines = [
        ln for ln in m.group(1).splitlines()
        if ln.strip() and not ln.strip().startswith("<!--")
        and ln.strip() not in ("- Files to add/change:", "- API shape / contracts:", "- Data model / migrations:")
    ]
    return "\n".join(lines)


def check(run_dir: Path) -> list[str]:
    failures: list[str] = []

    task_file = run_dir / "t1.md"
    if not task_file.exists():
        failures.append("t1.md not found in run dir")
        return failures

    fm = parse_frontmatter(task_file)  # type: ignore[name-defined]
    text = task_file.read_text(encoding="utf-8")

    # Status must be done
    if fm.get("status") != "done":
        failures.append(f"expected status=done, got {fm.get('status')!r}")

    # blocker must be null
    if fm.get("blocker") not in (None, "null", ""):
        failures.append(f"expected blocker=null, got {fm.get('blocker')!r}")

    # ## Design must have real content
    design = _section_body(text, "Design")
    if not design:
        failures.append("## Design section is empty — implementer must list files changed")

    # ## Decisions log must have real content
    dlog = _section_body(text, "Decisions log")
    if not dlog:
        failures.append("## Decisions log section is empty — implementer must record at least one decision")

    return failures
