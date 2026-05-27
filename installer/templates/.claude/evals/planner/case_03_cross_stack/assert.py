"""case_03_cross_stack — fastapi + flutter + supabase feature.

Pins task count, skill coverage, dep-graph sanity, and "no implementation
details in planner output" (heuristic — look for class-case identifiers
and file extensions in intent/acceptance).
"""
from pathlib import Path
import re


IMPL_DETAIL_RE = re.compile(
    r"\b("
    r"[A-Z][a-zA-Z0-9]+[A-Z][a-zA-Z0-9]+"          # PascalCase identifiers
    r"|[a-z_][a-z0-9_]*\.(?:py|dart|sql|ts|tsx|js|jsx)\b"  # filenames with extensions
    r"|class\s+\w+"
    r"|def\s+\w+"
    r")"
)


def check(run_dir: Path) -> list[str]:
    failures: list[str] = []
    tasks = load_tasks(run_dir)  # type: ignore[name-defined]
    n = len(tasks)

    if not (4 <= n <= 6):
        failures.append(f"expected 4-6 tasks, got {n}")

    if not (run_dir / "_overview.md").exists():
        failures.append("_overview.md missing")

    # skill coverage
    all_skills: set[str] = set()
    for t in tasks:
        s = t["frontmatter"].get("skills_to_load") or []
        all_skills.update(s)
    for required in ("fastapi", "flutter", "supabase"):
        if required not in all_skills:
            failures.append(f"no task loads skill '{required}' — required for cross-stack feature")

    # role mix
    roles = [t["frontmatter"].get("role") for t in tasks]
    if "implementer" not in roles:
        failures.append("no implementer task")
    # tester optional but encouraged
    if "tester" not in roles:
        failures.append("no tester task — recommended for this feature")

    # dep graph sanity
    by_id = {t["frontmatter"].get("id"): t for t in tasks if t["frontmatter"].get("id")}
    for tid, t in by_id.items():
        deps = t["frontmatter"].get("depends_on") or []
        for d in deps:
            if d not in by_id:
                failures.append(f"{tid}: depends_on unknown task '{d}'")

    # naive cycle check
    visited, stack = set(), set()
    def visit(node):
        if node in stack:
            failures.append(f"dependency cycle involving '{node}'")
            return
        if node in visited:
            return
        stack.add(node)
        for d in (by_id.get(node, {}).get("frontmatter", {}).get("depends_on") or []):
            visit(d)
        stack.discard(node)
        visited.add(node)
    for tid in by_id:
        visit(tid)

    # no implementation-detail leakage in intent/acceptance
    for t in tasks:
        fm = t["frontmatter"]
        for field in ("intent", "acceptance"):
            v = fm.get(field) or ""
            if not isinstance(v, str):
                continue
            m = IMPL_DETAIL_RE.search(v)
            if m:
                failures.append(
                    f"{t['path'].name}: '{field}' contains implementation detail "
                    f"'{m.group(1)}' — planner should stay above filenames/classes"
                )

    return failures
