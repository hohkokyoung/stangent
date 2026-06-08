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
   a. Re-index project files by running:
      ```
      PYEXE=$(ls .venv/bin/python venv/bin/python .env/bin/python 2>/dev/null | head -1) && ${PYEXE:-python3} .claude/hooks/lib/retriever.py reindex --project-only
      ```
      This ensures the vector index reflects any code written by earlier tasks in this run. Skills are not re-embedded (that is handled by `/agentic-index`).
   b. Read the task file's `role` field.
   c. Invoke the matching subagent (`planner` is never invoked here — only `implementer` / `reviewer` / `tester` / `sketcher`) with:
      - The absolute path to the task file
      - The `run_id`
      - The list of skill files (resolved from `skills_to_load` → `.claude/skills/<name>/SKILL.md`). If a skill name is `"project"`, skip SKILL.md injection — it is a retrieve-only pseudo-skill with no corresponding SKILL.md file. The agent receives project file chunks exclusively through `retrieve()`.
      - The task's `k` frontmatter value (default `6` if unset), passed to the agent as the retrieve k parameter
   d. Wait for the subagent to flip `status` (`done` | `blocked`) or for failure.
7. If a dependency ends up `blocked`, do NOT dispatch its dependents. They stay `pending`; `/agentic-status` will show them as transitively waiting.
8. After each task, re-evaluate step 4.
9. Stop when no runnable tasks remain.
10. Print the final dashboard.

## Constraints

- v1 is sequential only. Do not dispatch tasks in parallel.
- Do not modify task files yourself. Only subagents write to them.
- Do not bypass the dependency check, even for `/agentic-build <task-id>`.
