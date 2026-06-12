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

1. Read `.claude/.agentic.yml`:
   a. Extract the `models` section. Build a role→model lookup:
      - For each role (`planner`, `sketcher`, `implementer`, `reviewer`, `tester`, `debugger`, `refactor`), use `models.<role>` if set and non-empty; otherwise fall back to `models.default`; otherwise fall back to the current session model.
   b. Extract the `complexity_routing` section (if present):
      - `enabled` — default `false` if the section is absent
      - `low_cap` — default `claude-haiku-4-5-20251001`
      - `high_floor` — default `claude-sonnet-4-6`
   c. Model capability order used for all routing comparisons (cheapest → most capable):
      `claude-haiku-4-5-20251001` < `claude-sonnet-4-6` < `claude-opus-4-8`
      Any model not in this list is treated as `claude-sonnet-4-6` for comparison purposes.

2. Resolve `run-id`. List `.claude/state/plans/<run-id>/*.md` (exclude `_overview.md`). Then **immediately** run this exact Bash command (mandatory — do not skip):
   ```
   printf '%s' '<resolved-run-id>' > .claude/state/current_run.txt
   ```
   This lets the post-tool hook tag every log entry with the correct run_id.
3. Parse every task file's frontmatter into `{id, role, depends_on, status}`.
4. Topologically sort by `depends_on`. **Cycle → abort with error.** Do NOT partially dispatch.
5. Filter to runnable: `status == pending` AND every dep is `status == done`.
6. If single `<task-id>` was given, restrict the runnable set to that one (or refuse if its deps aren't done).
7. **Execute sequentially.** For each runnable task in topo order:
   a. Re-index project files by running:
      ```
      PYEXE=$(ls .venv/bin/python venv/bin/python .env/bin/python 2>/dev/null | head -1) && ${PYEXE:-python3} .claude/hooks/lib/retriever.py reindex --project-only
      ```
      This ensures the vector index reflects any code written by earlier tasks in this run. Skills are not re-embedded (that is handled by `/agentic-index`).
   b. Read the task file's `role` and `complexity` fields. Default `complexity` to `medium` if the field is absent or unrecognized.
   c. Determine the selected model:
      - `role_model` = role→model lookup from step 1a
      - If `complexity_routing.enabled` is true, apply routing using the capability order from step 1c:
        - `complexity: low` → `selected_model` = lesser of (`role_model`, `low_cap`) — Haiku wins if role is already Haiku
        - `complexity: medium` → `selected_model` = `role_model` (no change)
        - `complexity: high` → `selected_model` = greater of (`role_model`, `high_floor`) — more capable model wins
      - If routing is disabled: `selected_model = role_model`
      - `routing_applied` = true if `selected_model != role_model`
   d. Write state files and log the dispatch decision:
      ```
      printf '%s' '<task-id>' > .claude/state/current_task.txt
      printf '%s' '<role>' > .claude/state/current_role.txt
      printf '%s' '<selected_model>' > .claude/state/current_model.txt
      python3 .claude/hooks/lib/log_dispatch.py \
        --run_id '<run_id>' --task_id '<task_id>' --role '<role>' \
        --complexity '<complexity>' --role_baseline '<role_model>' \
        --model_selected '<selected_model>' [add --routing_applied if routing_applied is true]
      ```
   e. Invoke the matching subagent (`planner` is never invoked here — only `implementer` / `reviewer` / `tester` / `sketcher` / `refactor`) with:
      - The absolute path to the task file
      - The `run_id`
      - The list of skill files (resolved from `skills_to_load` → `.claude/skills/<name>/SKILL.md`). If a skill name is `"project"`, skip SKILL.md injection — it is a retrieve-only pseudo-skill with no corresponding SKILL.md file. The agent receives project file chunks exclusively through `retrieve()`.
      - The task's `k` frontmatter value (default `6` if unset), passed to the agent as the retrieve k parameter
      - **`selected_model` from step 7c** — pass this as the `model` parameter when invoking the subagent so it overrides the session default.
   f. After the subagent returns, run:
      ```
      rm -f .claude/state/current_task.txt .claude/state/current_role.txt .claude/state/current_model.txt
      ```
8. If a dependency ends up `blocked`, do NOT dispatch its dependents. They stay `pending`; `/agentic-status` will show them as transitively waiting.
9. After each task, re-evaluate step 5.
10. Stop when no runnable tasks remain.
11. Run this exact Bash command to clean up (mandatory — do not skip):
    ```
    rm -f .claude/state/current_run.txt .claude/state/current_task.txt .claude/state/current_role.txt
    ```
    Then print the final dashboard.

## Constraints

- v1 is sequential only. Do not dispatch tasks in parallel.
- Do not modify task files yourself. Only subagents write to them.
- Do not bypass the dependency check, even for `/agentic-build <task-id>`.
