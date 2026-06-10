"""case_01_review_appended — reviewer must fill ## Review without changing status or other sections.

Helpers available (injected by run.py):
  parse_frontmatter(path) -> dict
  load_tasks(run_dir) -> list[{path, frontmatter}]
  list_task_files(run_dir) -> list[Path]
  overview_text(run_dir) -> str
"""
import re
from pathlib import Path

# Sections the reviewer must NEVER modify.
PROTECTED_HEADINGS = ["Goal", "Requirements", "Constraints", "Edge cases", "Design", "Decisions log", "Test outline", "Test results"]

ORIGINAL_SECTIONS = {
    "Goal": "Add a minimal health-check endpoint to the FastAPI service.",
    "Design": "app/routes/health.py",  # key phrase that must still be present
    "Decisions log": "dedicated",       # key phrase
}


def _section_body(text: str, heading: str) -> str:
    pattern = rf"## {re.escape(heading)}\n(.*?)(?=\n## |\Z)"
    m = re.search(pattern, text, re.DOTALL)
    if not m:
        return ""
    lines = [ln for ln in m.group(1).splitlines() if ln.strip() and not ln.strip().startswith("<!--")]
    return "\n".join(lines)


def check(run_dir: Path) -> list[str]:
    failures: list[str] = []

    task_file = run_dir / "t1.md"
    if not task_file.exists():
        failures.append("t1.md not found in run dir")
        return failures

    fm = parse_frontmatter(task_file)  # type: ignore[name-defined]
    text = task_file.read_text(encoding="utf-8")

    # ## Review must have real content
    review = _section_body(text, "Review")
    if not review:
        failures.append("## Review section is empty — reviewer must append findings")

    # status must remain done (reviewer never sets done, but also must not flip to blocked for a clean task)
    if fm.get("status") != "done":
        failures.append(f"expected status=done (unchanged), got {fm.get('status')!r} — reviewer should not change status for a passing review")

    # blocker must remain null
    if fm.get("blocker") not in (None, "null", ""):
        failures.append(f"expected blocker=null, got {fm.get('blocker')!r} — no blocking issues in this task")

    # Protected sections must not have been modified (check key phrases still present)
    for heading, key_phrase in ORIGINAL_SECTIONS.items():
        body = _section_body(text, heading)
        if key_phrase not in body:
            failures.append(f"## {heading} appears to have been modified — reviewer must not touch this section (missing: {key_phrase!r})")

    return failures
