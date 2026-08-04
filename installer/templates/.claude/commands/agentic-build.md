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
   sh .claude/py .claude/hooks/lib/state.py clear
   printf '%s' '<resolved-run-id>' > .claude/state/current_run.txt
   ```
   The second command lets the post-tool hook tag every log entry with the correct run_id.

2. **Design refresh check (claude-design source only).** Read the `design:` block from `.claude/.agentic.yml`. If `design.source` is `claude-design` and `design.project_id` is non-empty, then for each `role: implementer` task in the run with `status: pending` whose `## Sketch` section contains a `Design HTML (synced with Claude Design):` line:
   a. Derive the remote path from the linked local path by stripping the `.claude/design/` prefix (e.g. `.claude/design/screens/FEAT-001/t2.html` → `screens/FEAT-001/t2.html`).
   b. Call `DesignSync get_file` for that remote path and compare with the local mirror file. If DesignSync is unavailable or the call fails, print one warning and skip this entire step — build with the local mirrors as-is; never halt the build over this.
   c. If the remote content differs (the developer edited the design on claude.ai/design): overwrite the local mirror with the remote content, then edit the corresponding sketcher task file `s<N>.md` — set `status: pending` and add `refresh: true` to its frontmatter. The existing `depends_on: [s<N>]` on the implementer task guarantees the sketcher re-renders before the implementer runs.
   d. If identical or the remote file is missing: leave everything untouched.

3. **Get the next task.** Run (pass `--task <task-id>` when a single task was requested; pass `--session-model <current session model id>` so unset per-role models fall back correctly):
   ```
   sh .claude/py .claude/hooks/lib/build_step.py next '<run_id>' [--task '<task-id>'] [--session-model '<session-model>']
   ```
   This computes the plan via `dispatch_plan.py`, re-indexes project files, writes the dispatch state, and logs the dispatch — one call instead of four. Exit codes are `dispatch_plan.py`'s, unchanged:
   - **Exit 3 (dependency cycle):** abort with the printed error. Do NOT partially dispatch. Jump to step 6 cleanup.
   - **Exit 4 (`--task` refused):** print the refusal (deps not done / already done) and stop. This is the dependency check — do NOT bypass it, even for `/agentic-build <task-id>`.
   - **Exit 0:** parse the JSON. `task` is the one fully-resolved task to dispatch now, already carrying `task_id`, `path`, `role`, `model`, `role_baseline`, `routing_applied`, `complexity`, `skills`, and `k` — do not recompute any of it. `counts` reports how many tasks remain runnable and how many are waiting. `blocked_by_dep` lists pending tasks transitively waiting on a blocked or deferred dep — they are never dispatched; `/agentic-status` shows them as waiting. `invalid_deps` lists pending tasks whose `depends_on` names a task id that does not exist in the run (a planner typo or a hand-edit) — print a one-line warning naming each and its `missing` ids. If `"done": true` is present there is nothing runnable: go to step 5. If nothing is runnable only because of `invalid_deps`, tell the developer to fix those dependencies and stop.

   Only the next task is emitted, not the whole plan. The loop never used more than one, and re-printing every remaining task on every iteration grew this conversation's context quadratically in task count — which is the dispatcher's own bill, since every later turn re-reads it.

4. **Execute sequentially** (v1 is sequential only — never parallel). Loop:
   a. If the last `build_step.py next` reported `"done": true`, exit the loop and go to step 5.
   b. Let `T` be the `task` object it printed.
   c. Invoke the matching subagent (`planner` is never invoked here — only `implementer` / `reviewer` / `tester` / `sketcher` / `refactor`) with:
      - The absolute path to the task file (`T.path`)
      - The `run_id`
      - The skill files: for each name in `T.skills`, `.claude/skills/<name>/SKILL.md`. Skip the name `"project"` — it is a retrieve-only pseudo-skill with no SKILL.md; that task gets project chunks through `retrieve()` only. If `T.skills` is empty for a tester (the config's `skill_groups.test` intersection was empty), print a one-line warning that no testing method is injected and continue — do not abort.
      - `T.k`, passed to the agent as the retrieve k parameter
      - **`T.model`** — pass as the `model` parameter so it overrides the session default.
   d. After the subagent returns, close the task out:
      ```
      sh .claude/py .claude/hooks/lib/build_step.py finish '<run_id>' '<T.task_id>' \
        --role '<T.role>' --path '<T.path>'
      ```
      One call instead of three. It verifies the Coverage table (reviewer tasks
      only), clears the per-task state, then checkpoints — in that order, which is
      not cosmetic: `pre_tool_use.py` denies `git commit` while any role is
      active, so checkpointing before the clear is refused. That is the same rule
      that stops a subagent committing on its own behalf.

      Print `verify_clears` when present. A missing Coverage row means an
      evaluation area was never examined — and since a non-blocking review lets
      the task proceed, an unexamined area passes as silently as a checked one.
      Report it alongside the verdict; do not re-dispatch automatically.

      Print the `checkpoint` line too. The checkpoint is the run's only recovery
      boundary: without it a build accumulates every task's edits uncommitted, and
      a task that damages earlier work leaves nothing to fall back to — a live
      risk now that an implementer may run a codemod across a whole directory. It
      never fails the build. A skipped checkpoint (not a git repo, disabled in
      config, branch switched mid-run, nothing to commit, a rejecting pre-commit
      hook) says so in that line and the build continues; never retry it and never
      commit by hand in its place.
   e. **Re-run the step 3 command** to get the next task (statuses on disk have
      changed). Go back to (a).

5. Stop when no runnable tasks remain. If tasks remain with `status: deferred` (the run was parked by `/agentic-defer`), never dispatch them — print the dossier path from `_overview.md`'s `## Deferral` block and suggest `/agentic-resume <run-id>` once the external blocker clears.

6. Run this exact Bash command to clean up (mandatory — do not skip):
   ```
   sh .claude/py .claude/hooks/lib/state.py clear
   ```
   Then print the final dashboard.

## Constraints

- v1 is sequential only. Do not dispatch tasks in parallel.
- Ordering, routing, and the runnable set come from `dispatch_plan.py` (which `build_step.py` calls) — never re-derive them by hand, and never re-run the individual helpers `build_step` wraps (`retriever.py reindex`, the `current_*.txt` writes, `log_dispatch.py`, `verify_clears.py`, `state.py clear --agent`, `git_branch.py checkpoint`). Calling them separately restores the six-turns-per-task shape this replaced.
- Do not modify task files yourself. Only subagents write to them. Exception: the design refresh check (step 2) may flip a sketcher task back to `pending` with `refresh: true`, and may overwrite local mirror files under `.claude/design/`.
- Do not bypass the dependency check, even for `/agentic-build <task-id>` (enforced by `dispatch_plan.py` exit 4).
