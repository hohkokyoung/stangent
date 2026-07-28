#!/usr/bin/env python3
"""Create / switch to a feature branch for a plan run; checkpoint finished tasks.

Reads `.agentic.yml: git.{auto_branch, branch_template, base_branch, fail_on_wip,
checkpoint_commits}` and performs the safe set of git operations for
/agentic-plan and /agentic-build.

Usage:
    python git_branch.py create FEAT-007
        - if not a git repo: print warning, exit 0 (non-fatal)
        - if auto_branch=false: print "auto_branch disabled", exit 0
        - if working tree dirty AND fail_on_wip=true: print message, exit 1
        - if target branch already exists: switch to it, exit 0
        - else: create from base_branch (or current HEAD), switch to it, exit 0

    python git_branch.py checkpoint FEAT-007 t4 [--role implementer] [--note "..."]
        Commit whatever the just-finished task wrote, on the run's branch.
        Always exit 0 — a failed checkpoint must never abort a build.

Why checkpoints exist: a build accumulates every task's changes in the working
tree and commits nothing, so there is no boundary to fall back to when task 7
damages what task 3 wrote. That was tolerable when each change was a single
reviewable Edit; it is not once an implementer may run a codemod across a whole
directory, where one wrong pattern rewrites hundreds of files at once. It also
can't be fixed inside the agent: pre_tool_use.py denies `git commit` to any
subagent (commits are user-driven), so the checkpoint has to be taken by the
dispatcher between tasks, while no role is active.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import read_agentic_config, config_section  # noqa: E402

REPO_ROOT = Path.cwd().resolve()


def load_git_cfg() -> dict:
    defaults = {
        "auto_branch": True,
        "branch_template": "feat/{run_id}",
        "base_branch": "",
        "fail_on_wip": True,
        # Default ON: the checkpoint is the only recovery boundary inside a run,
        # and it is cheap and local (never pushed). Projects seeded before this
        # key existed get the protection without editing config; set it to false
        # to opt out.
        "checkpoint_commits": True,
    }
    # A deliberate `checkpoint_commits: false` or `auto_branch: false`
    # that does not apply looks exactly like one that did.
    cfg = read_agentic_config(REPO_ROOT, "git_branch",
                              "`git:` settings are ignored and defaults used")
    return config_section(cfg, "git", defaults)


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


def expected_branches(run_id: str, cfg: dict) -> tuple[str, str]:
    """(base name, versioned prefix) this run's branch may legitimately have.
    cmd_create appends -v2/-v3 on collision, so both shapes are valid."""
    name = cfg["branch_template"].format(run_id=run_id)
    return name, name + "-v"


def cmd_checkpoint(run_id: str, task_id: str, role: str = "", note: str = "") -> int:
    """Commit the finished task's work. Never fails the build — exit is always 0."""
    cfg = load_git_cfg()

    if not cfg["checkpoint_commits"]:
        print("[git_branch] checkpoint_commits disabled in .agentic.yml; skipping")
        return 0
    if not is_git_repo():
        print("[git_branch] not a git repo; skipping checkpoint")
        return 0

    # Only ever commit onto this run's own branch. If the branch was switched
    # mid-run (by the developer, or a task that shelled out), committing here
    # would land the run's work on whatever is checked out — main, most likely.
    # Skipping loses a checkpoint; committing to the wrong branch loses trust in
    # every checkpoint.
    base, vprefix = expected_branches(run_id, cfg)
    cur = current_branch()
    if cur != base and not cur.startswith(vprefix):
        print(f"[git_branch] on '{cur or 'detached HEAD'}', expected '{base}' — "
              f"skipping checkpoint for {task_id} (commit it yourself if intended)")
        return 0

    if not working_tree_dirty():
        print(f"[git_branch] {task_id}: nothing to checkpoint")
        return 0

    # -A respects .gitignore, so .claude/state/ (run working memory) stays out.
    r = run(["git", "add", "-A"], check=False)
    if r.returncode != 0:
        print(f"[git_branch] git add failed: {r.stderr.strip()} — skipping checkpoint")
        return 0

    subject = f"{run_id} {task_id}"
    if role:
        subject += f" ({role})"
    if note:
        subject += f": {note}"
    body = ("Checkpoint written by /agentic-build after the task completed.\n"
            "Recovery boundary only — squash or rewrite before opening a PR.")

    r = run(["git", "commit", "-m", subject, "-m", body], check=False)
    if r.returncode != 0:
        # A pre-commit hook, missing git identity, or an empty diff after
        # gitignore filtering. None of these are worth aborting a 3-hour build.
        # A rejecting hook often prints nothing at all, so fall back to the exit
        # code rather than reporting a bare "unknown error".
        out = (r.stderr.strip() or r.stdout.strip())
        detail = out.splitlines()[0] if out else f"git commit exited {r.returncode}"
        print(f"[git_branch] checkpoint for {task_id} did not commit: {detail}")
        return 0

    sha = run(["git", "rev-parse", "--short", "HEAD"], check=False).stdout.strip()
    print(f"[git_branch] checkpointed {task_id} as {sha} on '{cur}'")
    return 0


def main() -> None:
    argv = sys.argv[1:]
    if len(argv) >= 2 and argv[0] == "create":
        sys.exit(cmd_create(argv[1]))
    if len(argv) >= 3 and argv[0] == "checkpoint":
        role = note = ""
        rest = argv[3:]
        for flag, target in (("--role", "role"), ("--note", "note")):
            if flag in rest:
                i = rest.index(flag)
                if i + 1 < len(rest):
                    if target == "role":
                        role = rest[i + 1]
                    else:
                        note = rest[i + 1]
        sys.exit(cmd_checkpoint(argv[1], argv[2], role, note))
    sys.stderr.write(
        "usage: git_branch.py create <run_id>\n"
        "       git_branch.py checkpoint <run_id> <task_id> [--role R] [--note N]\n")
    sys.exit(2)


if __name__ == "__main__":
    main()
