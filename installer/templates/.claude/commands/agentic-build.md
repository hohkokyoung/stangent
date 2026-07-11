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

Ordering, cycle detection, the runnable set, and per-task model/skills/k resolution are **computed by `dispatch_plan.py`, not by you.** You never topologically sort, compare model capabilities, or apply complexity routing yourself — you run the script and execute what it emits. This keeps the contract deterministic and unit-tested.

1. **Clear any leftover dispatch state** from a previously interrupted build (so stale `current_*.txt` don't mistag this run's logs), then resolve `run-id` (default = latest run dir by mtime; or the `<run-id>` argument) and write it to state. Run:
   ```
   python3 .claude/hooks/lib/state.py clear
   printf '%s' '<resolved-run-id>' > .claude/state/current_run.txt
   ```
   The second command lets the post-tool hook tag every log entry with the correct run_id.

2. **Design refresh check (claude-design source only).** Read the `design:` block from `.claude/.agentic.yml`. If `design.source` is `claude-design` and `design.project_id` is non-empty, then for each `role: implementer` task in the run with `status: pending` whose `## Sketch` section contains a `Design HTML (synced with Claude Design):` line:
   a. Derive the remote path from the linked local path by stripping the `.claude/design/` prefix (e.g. `.claude/design/screens/FEAT-001/t2.html` → `screens/FEAT-001/t2.html`).
   b. Call `DesignSync get_file` for that remote path and compare with the local mirror file. If DesignSync is unavailable or the call fails, print one warning and skip this entire step — build with the local mirrors as-is; never halt the build over this.
   c. If the remote content differs (the developer edited the design on claude.ai/design): overwrite the local mirror with the remote content, then edit the corresponding sketcher task file `s<N>.md` — set `status: pending` and add `refresh: true` to its frontmatter. The existing `depends_on: [s<N>]` on the implementer task guarantees the sketcher re-renders before the implementer runs.
   d. If identical or the remote file is missing: leave everything untouched.

3. **Compute the dispatch plan.** Run (pass `--task <task-id>` when a single task was requested; pass `--session-model <current session model id>` so unset per-role models fall back correctly):
   ```
   python3 .claude/hooks/lib/dispatch_plan.py '<run_id>' [--task '<task-id>'] [--session-model '<session-model>']
   ```
   - **Exit 3 (dependency cycle):** abort with the printed error. Do NOT partially dispatch. Jump to step 6 cleanup.
   - **Exit 4 (`--task` refused):** print the refusal (deps not done / already done) and stop. This is the dependency check — do NOT bypass it, even for `/agentic-build <task-id>`.
   - **Exit 0:** parse the JSON. `runnable` is a list of fully-resolved tasks, each already carrying `task_id`, `path`, `role`, `model`, `role_baseline`, `routing_applied`, `complexity`, `skills`, and `k`. `blocked_by_dep` lists pending tasks transitively waiting on a blocked or deferred dep — do not dispatch them; `/agentic-status` shows them as waiting. `invalid_deps` lists pending tasks whose `depends_on` names a task id that does not exist in the run (a planner typo or a hand-edit) — print a one-line warning naming each and its `missing` ids; they are never dispatched. If `runnable` is empty only because of `invalid_deps`, tell the developer to fix those dependencies and stop.

4. **Execute sequentially** (v1 is sequential only — never parallel). Loop:
   a. If `runnable` is empty, exit the loop and go to step 5.
   b. Take the **first** entry `T` in `runnable`. Every value you need is already in `T` — do not recompute any of it.
   c. Re-index project files so retrieval reflects code written by earlier tasks:
      ```
      PYEXE=$(ls .venv/bin/python venv/bin/python .env/bin/python 2>/dev/null | head -1) && ${PYEXE:-python3} .claude/hooks/lib/retriever.py reindex --project-only
      ```
      Skills are not re-embedded (that is handled by `/agentic-index`).
   d. Write state files and log the dispatch using `T`'s fields:
      ```
      printf '%s' '<T.task_id>' > .claude/state/current_task.txt
      printf '%s' '<T.role>'    > .claude/state/current_role.txt
      printf '%s' '<T.model>'   > .claude/state/current_model.txt
      python3 .claude/hooks/lib/log_dispatch.py \
        --run_id '<run_id>' --task_id '<T.task_id>' --role '<T.role>' \
        --complexity '<T.complexity>' --role_baseline '<T.role_baseline>' \
        --model_selected '<T.model>' [add --routing_applied if T.routing_applied is true]
      ```
   e. Invoke the matching subagent (`planner` is never invoked here — only `implementer` / `reviewer` / `tester` / `sketcher` / `refactor`) with:
      - The absolute path to the task file (`T.path`)
      - The `run_id`
      - The skill files: for each name in `T.skills`, `.claude/skills/<name>/SKILL.md`. Skip the name `"project"` — it is a retrieve-only pseudo-skill with no SKILL.md; that task gets project chunks through `retrieve()` only. If `T.skills` is empty for a tester (the config's `skill_groups.test` intersection was empty), print a one-line warning that no testing method is injected and continue — do not abort.
      - `T.k`, passed to the agent as the retrieve k parameter
      - **`T.model`** — pass as the `model` parameter so it overrides the session default.
   f. After the subagent returns, clear the per-task state:
      ```
      rm -f .claude/state/current_task.txt .claude/state/current_role.txt .claude/state/current_model.txt
      ```
   g. **Re-run the step 3 command** to recompute `runnable` (task statuses on disk have changed). Go back to (a).

5. Stop when no runnable tasks remain. If tasks remain with `status: deferred` (the run was parked by `/agentic-defer`), never dispatch them — print the dossier path from `_overview.md`'s `## Deferral` block and suggest `/agentic-resume <run-id>` once the external blocker clears.

6. Run this exact Bash command to clean up (mandatory — do not skip):
   ```
   rm -f .claude/state/current_run.txt .claude/state/current_task.txt .claude/state/current_role.txt .claude/state/current_model.txt
   ```
   Then print the final dashboard.

## Constraints

- v1 is sequential only. Do not dispatch tasks in parallel.
- Ordering, routing, and the runnable set come from `dispatch_plan.py` — never re-derive them by hand.
- Do not modify task files yourself. Only subagents write to them. Exception: the design refresh check (step 2) may flip a sketcher task back to `pending` with `refresh: true`, and may overwrite local mirror files under `.claude/design/`.
- Do not bypass the dependency check, even for `/agentic-build <task-id>` (enforced by `dispatch_plan.py` exit 4).
