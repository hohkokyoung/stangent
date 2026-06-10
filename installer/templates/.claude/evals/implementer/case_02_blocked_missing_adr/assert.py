"""case_02_blocked_missing_adr — implementer must flip to blocked when a listed ADR is absent.

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
    lines = [
        ln for ln in m.group(1).splitlines()
        if ln.strip() and not ln.strip().startswith("<!--")
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

    # Status must be blocked
    if fm.get("status") != "blocked":
        failures.append(f"expected status=blocked, got {fm.get('status')!r} — implementer should not proceed with a missing ADR")

    # blocker must reference missing_adr
    blocker = str(fm.get("blocker") or "")
    if "missing_adr" not in blocker.lower():
        failures.append(f"blocker should mention 'missing_adr', got {blocker!r}")

    # Design section must be empty — no code written
    design = _section_body(text, "Design")
    if design:
        failures.append("## Design section has content — implementer should not write code when blocked on missing ADR")

    return failures
