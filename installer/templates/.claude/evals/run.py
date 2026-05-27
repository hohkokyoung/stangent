#!/usr/bin/env python3
"""Score-only eval runner. v1 contract:

  - You manually invoke the agent (e.g. /agentic-plan) and produce a run dir
    at .claude/state/plans/<FEAT-NNN>/.
  - This script loads <case>/assert.py and calls check(run_dir) on it.
  - check() returns a list of failure messages; empty list = pass.

Usage:
  python .claude/evals/run.py planner/case_01_minimal FEAT-001
  python .claude/evals/run.py --all                       # rough batch mode
"""
from __future__ import annotations

import argparse
import importlib.util
import re
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()
EVALS_DIR = REPO_ROOT / ".claude" / "evals"
PLANS_DIR = REPO_ROOT / ".claude" / "state" / "plans"


# ---------- helpers exposed to asserters ----------

_FM_RE = re.compile(r"^---\s*\n(.*?)\n---\s*\n", re.DOTALL)


def parse_frontmatter(path: Path) -> dict:
    """Tiny YAML-ish frontmatter parser. Handles strings, ints, bools,
    lists (`[a, b]` or `["a", "b"]`), and `null`. Avoids the PyYAML dep
    so the harness works offline without extras.
    """
    text = path.read_text(encoding="utf-8")
    m = _FM_RE.match(text)
    if not m:
        return {}
    out: dict = {}
    for raw_line in m.group(1).splitlines():
        line = raw_line.rstrip()
        if not line or line.lstrip().startswith("#"):
            continue
        if ":" not in line:
            continue
        key, _, value = line.partition(":")
        out[key.strip()] = _parse_scalar(value.strip())
    return out


def _parse_scalar(s: str):
    if s == "" or s.lower() == "null":
        return None
    if s.lower() == "true":
        return True
    if s.lower() == "false":
        return False
    if s.startswith("[") and s.endswith("]"):
        inner = s[1:-1].strip()
        if not inner:
            return []
        return [item.strip().strip('"').strip("'") for item in inner.split(",")]
    if s.startswith(('"', "'")) and s.endswith(s[0]):
        return s[1:-1]
    try:
        return int(s)
    except ValueError:
        pass
    return s


def list_task_files(run_dir: Path) -> list[Path]:
    return sorted(
        p for p in run_dir.glob("*.md")
        if p.name != "_overview.md" and not p.name.startswith("_")
    )


def load_tasks(run_dir: Path) -> list[dict]:
    """Return [{path, frontmatter}] for every task file."""
    return [{"path": p, "frontmatter": parse_frontmatter(p)} for p in list_task_files(run_dir)]


def overview_text(run_dir: Path) -> str:
    f = run_dir / "_overview.md"
    return f.read_text(encoding="utf-8") if f.exists() else ""


# ---------- runner ----------

def import_asserter(case_dir: Path):
    f = case_dir / "assert.py"
    spec = importlib.util.spec_from_file_location(f"asserter_{case_dir.name}", f)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot import {f}")
    mod = importlib.util.module_from_spec(spec)
    # expose helpers
    mod.parse_frontmatter = parse_frontmatter         # type: ignore[attr-defined]
    mod.load_tasks = load_tasks                       # type: ignore[attr-defined]
    mod.list_task_files = list_task_files             # type: ignore[attr-defined]
    mod.overview_text = overview_text                 # type: ignore[attr-defined]
    spec.loader.exec_module(mod)
    return mod


def run_case(case_rel: str, run_id: str) -> tuple[bool, list[str]]:
    case_dir = EVALS_DIR / case_rel
    if not case_dir.is_dir():
        return False, [f"case dir not found: {case_dir}"]
    run_dir = PLANS_DIR / run_id
    if not run_dir.is_dir():
        return False, [f"run dir not found: {run_dir}"]
    try:
        mod = import_asserter(case_dir)
    except Exception as e:
        return False, [f"failed to import asserter: {e}"]
    if not hasattr(mod, "check"):
        return False, [f"{case_dir}/assert.py missing check() function"]
    try:
        failures = list(mod.check(run_dir))
    except Exception as e:
        return False, [f"asserter raised: {e!r}"]
    return (not failures), failures


def cmd_one(case_rel: str, run_id: str) -> int:
    ok, failures = run_case(case_rel, run_id)
    if ok:
        print(f"[pass] {case_rel}  ←  {run_id}")
        return 0
    print(f"[fail] {case_rel}  ←  {run_id}")
    for msg in failures:
        print(f"       - {msg}")
    return 1


def cmd_all(role: str) -> int:
    """Rough batch mode: walks .claude/evals/<role>/case_*/ and looks for a
    sibling .runid file inside each case (one line, FEAT-NNN) to map case
    → run dir. Skip cases without a .runid (write one after running the case)."""
    role_dir = EVALS_DIR / role
    if not role_dir.is_dir():
        print(f"no eval dir at {role_dir}")
        return 1
    cases = sorted(p for p in role_dir.iterdir() if p.is_dir() and p.name.startswith("case_"))
    if not cases:
        print(f"no cases under {role_dir}")
        return 1
    fail = 0
    for c in cases:
        runid_file = c / ".runid"
        if not runid_file.exists():
            print(f"[skip] {role}/{c.name}  (no .runid; write one with the FEAT-NNN you used)")
            continue
        run_id = runid_file.read_text(encoding="utf-8").strip()
        ok, failures = run_case(f"{role}/{c.name}", run_id)
        if ok:
            print(f"[pass] {role}/{c.name}  ←  {run_id}")
        else:
            fail += 1
            print(f"[fail] {role}/{c.name}  ←  {run_id}")
            for msg in failures:
                print(f"       - {msg}")
    return 0 if fail == 0 else 1


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("case_or_role", help="e.g. planner/case_01_minimal  OR  planner (with --all)")
    ap.add_argument("run_id", nargs="?", default=None, help="FEAT-NNN to score against")
    ap.add_argument("--all", action="store_true", help="batch every case under the given role")
    args = ap.parse_args()

    if args.all:
        sys.exit(cmd_all(args.case_or_role))
    if not args.run_id:
        ap.error("run_id required unless --all is given")
    sys.exit(cmd_one(args.case_or_role, args.run_id))


if __name__ == "__main__":
    main()
