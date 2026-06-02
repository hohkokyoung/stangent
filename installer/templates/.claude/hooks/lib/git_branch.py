#!/usr/bin/env python3
"""Create / switch to a feature branch for a plan run.

Reads `.agentic.yml: git.{auto_branch, branch_template, base_branch, fail_on_wip}`
and performs the safe set of git operations for /agentic-plan.

Usage:
    python git_branch.py create FEAT-007
        - if not a git repo: print warning, exit 0 (non-fatal)
        - if auto_branch=false: print "auto_branch disabled", exit 0
        - if working tree dirty AND fail_on_wip=true: print message, exit 1
        - if target branch already exists: switch to it, exit 0
        - else: create from base_branch (or current HEAD), switch to it, exit 0
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

REPO_ROOT = Path.cwd().resolve()
AGENTIC_YML = REPO_ROOT / ".claude" / ".agentic.yml"


def load_git_cfg() -> dict:
    defaults = {
        "auto_branch": True,
        "branch_template": "feat/{run_id}",
        "base_branch": "",
        "fail_on_wip": True,
    }
    if not AGENTIC_YML.exists() or yaml is None:
        return defaults
    try:
        full = yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or {}
    except Exception:
        return defaults
    g = (full.get("git") or {})
    return {**defaults, **{k: g[k] for k in defaults if k in g}}


def run(cmd: list[str], check: bool = True) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True, check=check)


def is_git_repo() -> bool:
    try:
        run(["git", "rev-parse", "--is-inside-work-tree"])
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False


def working_tree_dirty() -> bool:
    r = run(["git", "status", "--porcelain"], check=False)
    return bool(r.stdout.strip())


def branch_exists(name: str) -> bool:
    r = run(["git", "rev-parse", "--verify", "--quiet", f"refs/heads/{name}"], check=False)
    return r.returncode == 0


def current_branch() -> str:
    r = run(["git", "branch", "--show-current"], check=False)
    return r.stdout.strip()


def cmd_create(run_id: str) -> int:
    cfg = load_git_cfg()

    if not is_git_repo():
        print("[git_branch] not a git repo; skipping branch creation")
        return 0

    if not cfg["auto_branch"]:
        print("[git_branch] auto_branch disabled in .agentic.yml; skipping")
        return 0

    name = cfg["branch_template"].format(run_id=run_id)

    if branch_exists(name):
        # Branch already exists — find the next available versioned name
        v = 2
        while branch_exists(f"{name}-v{v}"):
            v += 1
        name = f"{name}-v{v}"
        print(f"[git_branch] base branch already exists; using '{name}'")

    if cfg["fail_on_wip"] and working_tree_dirty():
        print(f"[git_branch] working tree has uncommitted changes; "
              f"commit or stash before /agentic-plan creates '{name}'")
        return 1

    base = cfg["base_branch"]
    args = ["git", "switch", "-c", name]
    if base:
        args.append(base)
    r = run(args, check=False)
    if r.returncode != 0:
        print(f"[git_branch] git switch -c failed: {r.stderr.strip()}")
        return r.returncode
    base_desc = f"from '{base}'" if base else "from current HEAD"
    print(f"[git_branch] created and switched to '{name}' {base_desc}")
    return 0


def main() -> None:
    if len(sys.argv) < 3 or sys.argv[1] != "create":
        sys.stderr.write("usage: git_branch.py create <run_id>\n")
        sys.exit(2)
    sys.exit(cmd_create(sys.argv[2]))


if __name__ == "__main__":
    main()
