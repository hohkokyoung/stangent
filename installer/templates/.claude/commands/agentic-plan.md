---
description: Run the planner agent on a goal; emit _overview.md + per-task files (all pending)
argument-hint: "<goal text>"
---

# /agentic-plan

Run the planner on the given goal.

## Procedure

1. Allocate the next `run_id` by running:
   ```
   python .claude/hooks/lib/plan_id.py next
   ```
   The script reads `.claude/.agentic.yml: plan_id.{prefix,pad,start}` and scans existing `.claude/state/plans/<prefix>-*` dirs. Default format: `FEAT-001`, `FEAT-002`, ...

2. **Create the feature branch** (if `git.auto_branch` is true in `.agentic.yml`):
   ```
   python .claude/hooks/lib/git_branch.py create <run_id>
   ```
   - Skips silently if not a git repo.
   - **Refuses (exit 1) if the working tree has uncommitted changes** and `git.fail_on_wip` is true. If this happens, STOP — tell the user to commit or stash, then re-run `/agentic-plan` with the same goal. Do NOT proceed to step 3 in this case (otherwise you'd allocate a `run_id` and have a half-finished plan with no branch).
   - On success, creates `feat/<run_id>` (template configurable) from the current HEAD (or `git.base_branch` if set) and switches to it.

3. Create `.claude/state/plans/<run-id>/`.

4. Invoke the **planner** agent with:
   - The user goal: `$ARGUMENTS`
   - The generated `run_id`
   - The contents of `.claude/.agentic.yml`

5. The planner writes `_overview.md` + per-task files (all `status: pending`).

6. After planner finishes, print:
   - `run_id`
   - feature branch name (or "no branch — not a git repo / auto_branch disabled")
   - task index (id → intent → role → status)
   - "next step: /agentic-build all" (or `/agentic-build <task-id>`)

## Constraints

- Do NOT call any MCP tool yourself. The planner has its own constraints.
- Do NOT modify task files outside the planner.
- Do NOT dispatch tasks; that's `/agentic-build`.
- Do NOT commit or push anything yourself. Branch creation is the only git operation. Commits and merges are user-driven.
