---
description: Print the dashboard for a run (or all runs); regenerate _overview.md from task files
argument-hint: "[run-id | --all]"
---

# /agentic-status

Two modes: single-run (default) and `--all` (cross-run registry, including parked features).

## Single-run mode

1. Resolve `run-id`: `$ARGUMENTS` if given, else latest run directory by mtime under `.claude/state/plans/`.
2. Read every task file in that directory (exclude `_overview.md`).
3. For each: extract `id`, `role`, `skills_to_load`, `intent`, `status`, `blocker`, `resume_when`, `depends_on`, file mtime.
4. Compute `waiting_on` per task: list of blocked-, deferred-, or pending-dependency ids.
5. Print a table:
   ```
   ID  ROLE         SKILLS         STATUS    AGE        BLOCKER / WAITING_ON
   t1  implementer  fastapi        done      2h
   t2  tester       fastapi        blocked   1h         test: 401 not returned
   t3  implementer  flutter        deferred  1h         external: backend not deployed → resume when staging API live
   t4  implementer  flutter        pending   1h         waiting_on=[t2]
   ```
6. Regenerate `.claude/state/plans/<run-id>/_overview.md`:
   - Run-level framing (goal, requirements, constraints, edge cases, assumptions, deferral) — preserve from existing `_overview.md`.
   - Replace the "Task index" section with the current task table.
7. Print summary counts: `pending: N | running: N | done: N | blocked: N | deferred: N`.
8. If the overview is `status: deferred`, print the dossier path from its `## Deferral` block and: `next step (once the blocker clears): /agentic-resume <run-id>`.

## `--all` — cross-run registry

1. List every run dir under `.claude/state/plans/` (newest mtime first). For each, read `_overview.md` frontmatter plus task statuses and derive the run state: `complete` (all tasks done) · `deferred` (overview says so) · `blocked` (any blocked task) · `in-progress` (otherwise).
2. Merge with `docs/FEATURES.md` (if present): rows whose run dir no longer exists locally (e.g. a fresh clone where run state wasn't committed) still appear, with state `deferred (dossier only)`.
3. Print a table:
   ```
   RUN       STATE                GOAL                       DONE  BLOCKED ON / RESUME WHEN
   FEAT-005  in-progress          push notifications         2/5
   FEAT-004  deferred             chat attachments           3/6   external: backend not deployed → staging API live
   FEAT-002  complete             login flow                 4/4
   ```
4. `--all` mode writes nothing.

## Constraints

- Read-only with respect to task files.
- Only `_overview.md` is rewritten (single-run mode; only its task-index section — preserve the framing portion, including any `## Deferral` block).
- `--all` writes no files at all.
