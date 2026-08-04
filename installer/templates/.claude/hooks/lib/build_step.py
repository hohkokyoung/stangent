#!/usr/bin/env python3
"""One call per side of a build task, instead of six.

    build_step.py next   <run_id> [--task ID] [--session-model M]
    build_step.py finish <run_id> <task_id> --role R --path P

`/agentic-build` used to spend ~6 orchestrator turns per task and do real work in
exactly one of them: reindex, three state writes, a dispatch-log line, the
subagent, a coverage check, a state clear, a checkpoint, then a full re-run of
`dispatch_plan.py`. The other five were the dispatcher talking to its own scripts.

That matters because billed input is the sum over turns of the ENTIRE prefix —
system prompt, tool schemas, command body, and everything accumulated so far.
Every extra turn re-reads all of it, so the turn count, not the size of any one
message, is what sets the orchestrator's bill. Modelled on a 12-task run, folding
6 turns per task into 3 was worth roughly twice what shrinking the messages was.

This script changes ONLY how many calls the bookkeeping takes. Ordering, cycle
detection, the runnable set, and per-task model/skill/k resolution still come
from `dispatch_plan.py` — imported, never reimplemented — so the fixed contract
in agentic-build.md holds and stays unit-tested in one place.

Exit codes are `dispatch_plan.py`'s, unchanged, because the command branches on
them: 0 ok, 2 bad run, 3 dependency cycle, 4 `--task` refused. `next` adds one
case of its own: exit 0 with `{"done": true}` when nothing is runnable.

Both subcommands print a single compact JSON object. `next` deliberately emits
the ONE resolved task the dispatcher acts on plus counts, not the whole plan —
the loop only ever used `runnable[0]`, and re-emitting every remaining task on
every iteration made the orchestrator's context grow quadratically in task count.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import dispatch_plan  # noqa: E402

LIB = Path(__file__).resolve().parent
CLAUDE = LIB.parents[1]
STATE = CLAUDE / "state"


def _run(args: list[str]) -> tuple[int, str]:
    """Run a sibling helper under THIS interpreter.

    Not `sh .claude/py`: that shim exists to pick an interpreter for a fresh
    process tree, and we are already running under the one it would pick.
    """
    p = subprocess.run([sys.executable, *args], capture_output=True, text=True,
                       cwd=str(CLAUDE.parent))
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def _write_state(name: str, value: str) -> None:
    STATE.mkdir(parents=True, exist_ok=True)
    (STATE / name).write_text(value, encoding="utf-8")


def cmd_next(run_id: str, only_task: str | None, session_model: str | None) -> int:
    plan, code = dispatch_plan.build_plan(
        run_id, dispatch_plan.load_config(), only_task, session_model)
    if code != 0:
        print(json.dumps(plan, ensure_ascii=False))
        return code

    runnable = plan.get("runnable") or []
    out: dict = {
        "run_id": run_id,
        "counts": {
            "runnable": len(runnable),
            "blocked_by_dep": len(plan.get("blocked_by_dep") or []),
        },
        # Never summarised to a count: each names a task the planner or a hand-edit
        # left pointing at a task id that does not exist, and the developer has to
        # see which ones to fix them.
        "invalid_deps": plan.get("invalid_deps") or [],
        "blocked_by_dep": plan.get("blocked_by_dep") or [],
    }
    if not runnable:
        out["done"] = True
        print(json.dumps(out, ensure_ascii=False))
        return 0

    t = runnable[0]

    # Re-index before the task runs so retrieval reflects code written by earlier
    # ones. Its own summary line is captured rather than echoed: on a warm index
    # it says "0 indexed, N skipped (unchanged)" every single task.
    rc, reindex_out = _run([str(LIB / "retriever.py"), "reindex", "--project-only"])
    # Its own `[retriever] ...` summary, not merely the last line: fastembed writes
    # a download progress bar to stderr on a cold model cache, which otherwise won
    # the tail and put "Fetching 5 files: 100%|####|" into the dispatcher's context
    # once per task — exactly the noise this script exists to remove.
    summary = [ln for ln in reindex_out.splitlines() if ln.startswith("[retriever]")]
    out["reindex"] = summary[-1] if summary else ""
    if rc != 0:
        # Stale retrieval is a degraded task, not a failed build — the same call
        # was advisory when the command made it directly.
        out["reindex_failed"] = True

    _write_state("current_task.txt", t["task_id"])
    _write_state("current_role.txt", t["role"])
    _write_state("current_model.txt", t["model"])

    args = [str(LIB / "log_dispatch.py"),
            "--run_id", run_id, "--task_id", t["task_id"], "--role", t["role"],
            "--complexity", t.get("complexity") or "medium",
            "--role_baseline", t.get("role_baseline") or "",
            "--model_selected", t["model"]]
    if t.get("routing_applied"):
        args.append("--routing_applied")
    _run(args)

    out["task"] = t
    print(json.dumps(out, ensure_ascii=False))
    return 0


def cmd_finish(run_id: str, task_id: str, role: str, path: str) -> int:
    out: dict = {"run_id": run_id, "task_id": task_id, "role": role}

    # A reviewer's Coverage table, checked before anything else: a non-blocking
    # review lets the task proceed, so an evaluation area that was never examined
    # passes as silently as one that was checked.
    if role == "reviewer" and path:
        _, verify_out = _run([str(LIB / "verify_clears.py"), path, "--cwd", ".",
                              "--checklist", str(CLAUDE / "agents" / "reviewer.md")])
        out["verify_clears"] = verify_out.strip()

    # Order matters and is not cosmetic: pre_tool_use.py denies `git commit` while
    # any role is active, so checkpointing before this clear is refused — the same
    # rule that stops a subagent committing on its own behalf.
    for name in ("current_task.txt", "current_role.txt", "current_model.txt",
                 "edit_counts.json"):
        try:
            (STATE / name).unlink()
        except OSError:
            pass

    # Always exits 0. A skipped checkpoint (not a git repo, disabled in config,
    # branch switched mid-run, nothing to commit, a rejecting pre-commit hook)
    # prints one line and the build continues; it is never retried or replaced by
    # a hand-written commit.
    _, cp = _run([str(LIB / "git_branch.py"), "checkpoint", run_id, task_id,
                  "--role", role])
    out["checkpoint"] = cp.strip().splitlines()[-1] if cp.strip() else ""

    print(json.dumps(out, ensure_ascii=False))
    return 0


def main() -> None:
    ap = argparse.ArgumentParser(prog="build_step.py")
    sub = ap.add_subparsers(dest="cmd", required=True)

    n = sub.add_parser("next")
    n.add_argument("run_id")
    n.add_argument("--task", default=None)
    n.add_argument("--session-model", default=None)

    f = sub.add_parser("finish")
    f.add_argument("run_id")
    f.add_argument("task_id")
    f.add_argument("--role", default="")
    f.add_argument("--path", default="")

    args = ap.parse_args()
    if args.cmd == "next":
        sys.exit(cmd_next(args.run_id, args.task, args.session_model))
    sys.exit(cmd_finish(args.run_id, args.task_id, args.role, args.path))


if __name__ == "__main__":
    main()
