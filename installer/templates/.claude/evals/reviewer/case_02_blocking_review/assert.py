"""case_02_blocking_review — reviewer must block a task with a clear injection vulnerability.

Helpers available (injected by run.py):
  parse_frontmatter(path) -> dict
  load_tasks(run_dir) -> list[{path, frontmatter}]
  list_task_files(run_dir) -> list[Path]
  overview_text(run_dir) -> str
"""
import re
from pathlib import Path


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

    # status must be blocked
    if fm.get("status") != "blocked":
        failures.append(f"expected status=blocked (SQL injection is a blocking violation), got {fm.get('status')!r}")

    # blocker must be set
    blocker = str(fm.get("blocker") or "")
    if not blocker or blocker in ("null", ""):
        failures.append("blocker field must be set when reviewer blocks a task")

    # ## Review must have content
    review = _section_body(text, "Review")
    if not review:
        failures.append("## Review section is empty — reviewer must append findings")

    # Review must indicate a blocking verdict
    review_lower = review.lower()
    if "blocking" not in review_lower:
        failures.append("## Review must contain the word 'blocking' for a security violation finding")

    # Review should mention injection (sql, injection, owasp, a03, or interpolat)
    injection_terms = ["injection", "sql", "owasp", "a03", "interpolat", "parameteriz", "f-string", "f\""]
    if not any(t in review_lower for t in injection_terms):
        failures.append(
            "## Review should mention the injection vulnerability "
            f"(looked for: {injection_terms})"
        )

    return failures
