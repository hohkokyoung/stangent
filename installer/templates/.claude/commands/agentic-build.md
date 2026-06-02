---
description: Dispatch each pending task in dependency order to its role agent
argument-hint: "[task-id|all]"
---

# /agentic-build

Dispatcher. The only orchestrator. Algorithm is fixed; do not invent your own.

## Arguments

- `all` (default): build every runnable task in the latest run.
- `<task-id>`: build only that task (refuses if its deps aren't `done`).
- Optional second arg: `<run-id>` to target a specific run. Default = latest run directory by mtime.

## Algorithm (FIXED CONTRACT — do not deviate)

1. Resolve `run-id`. List `.claude/state/plans/<run-id>/*.md` (exclude `_overview.md`).
2. Parse every task file's frontmatter into `{id, role, depends_on, status}`.
3. Topologically sort by `depends_on`. **Cycle → abort with error.** Do NOT partially dispatch.
4. Filter to runnable: `status == pending` AND every dep is `status == done`.
5. If single `<task-id>` was given, restrict the runnable set to that one (or refuse if its deps aren't done).
6. **Execute sequentially.** For each runnable task in topo order:
   a. Read the task file's `role` field.
   b. Invoke the matching subagent (`planner` is never invoked here — only `implementer` / `reviewer` / `tester` / `sketcher`) with:
      - The absolute path to the task file
      - The `run_id`
      - The list of skill files (resolved from `skills_to_load` → `.claude/skills/<name>/SKILL.md`)
   c. Wait for the subagent to flip `status` (`done` | `blocked`) or for failure.
7. If a dependency ends up `blocked`, do NOT dispatch its dependents. They stay `pending`; `/agentic-status` will show them as transitively waiting.
8. After each task, re-evaluate step 4.
9. Stop when no runnable tasks remain.
10. Print the final dashboard.

## Constraints

- v1 is sequential only. Do not dispatch tasks in parallel.
- Do not modify task files yourself. Only subagents write to them.
- Do not bypass the dependency check, even for `/agentic-build <task-id>`.
