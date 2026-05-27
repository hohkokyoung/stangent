---
description: Print the dashboard for a run; regenerate _overview.md from task files
argument-hint: "[run-id]"
---

# /agentic-status

## Procedure

1. Resolve `run-id`: `$ARGUMENTS` if given, else latest run directory by mtime under `.claude/state/plans/`.
2. Read every task file in that directory (exclude `_overview.md`).
3. For each: extract `id`, `role`, `skills_to_load`, `intent`, `status`, `blocker`, `depends_on`, file mtime.
4. Compute `waiting_on` per task: list of blocked-or-pending dependency ids.
5. Print a table:
   ```
   ID  ROLE         SKILLS         STATUS    AGE        BLOCKER / WAITING_ON
   t1  implementer  fastapi        done      2h
   t2  tester       fastapi        blocked   1h         test: 401 not returned
   t3  implementer  flutter        pending   1h         waiting_on=[t2]
   ```
6. Regenerate `.claude/state/plans/<run-id>/_overview.md`:
   - Run-level framing (goal, requirements, constraints, edge cases, assumptions) — preserve from existing `_overview.md`.
   - Replace the "Task index" section with the current task table.
7. Print summary counts: `pending: N | running: N | done: N | blocked: N`.

## Constraints

- Read-only with respect to task files.
- Only `_overview.md` is rewritten (and only its task-index section is regenerated; preserve the framing portion).
