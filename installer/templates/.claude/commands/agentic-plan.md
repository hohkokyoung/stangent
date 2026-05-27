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
2. Create `.claude/state/plans/<run-id>/`.
3. Invoke the **planner** agent with:
   - The user goal: `$ARGUMENTS`
   - The generated `run_id`
   - The contents of `.claude/.agentic.yml`
4. The planner writes `_overview.md` + per-task files (all `status: pending`).
5. After planner finishes, print:
   - `run_id`
   - task index (id → intent → role → status)
   - "next step: /agentic-build all" (or `/agentic-build <task-id>`)

## Constraints

- Do NOT call any MCP tool yourself. The planner has its own constraints.
- Do NOT modify task files outside the planner.
- Do NOT dispatch tasks; that's `/agentic-build`.
