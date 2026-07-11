---
description: Amend an existing run — add tasks, refine acceptance/edges, never touch done tasks
argument-hint: "[run-id] <amendment text>"
---

# /agentic-update-plan

Re-invoke the planner in **update mode** against an existing run. Use this when:

- The user changed their mind about scope.
- A blocked task revealed an unknown requirement.
- A reviewer / tester surfaced a gap that needs new work.
- An assumption recorded in `_overview.md` turned out wrong.

## Arguments

- First arg (optional): `<run-id>` (e.g. `FEAT-003`). Defaults to the latest run.
- Remaining args: free-text amendment describing what should change.

If no amendment text is given, use `AskUserQuestion` (YOU, not the planner) to elicit it before proceeding.

## Procedure

1. Resolve `<run-id>`. If not given, use `python3 .claude/hooks/lib/plan_id.py peek`.
2. Read every task file in `.claude/state/plans/<run-id>/` plus `_overview.md`.
3. Compute the **frozen set** = tasks with `status: done`. These are immutable — the planner may NOT modify their frontmatter, sections, or status.
4. **Clarification phase (YOU do this — do NOT delegate to the planner).** Using the amendment text as the starting point, walk only the dimensions from the coverage checklist (see `/agentic-plan`) that the amendment touches. Ask up to **4 rounds**, up to **3 questions per round**. Collect answers into a `## Clarifications` block (same format as `/agentic-plan`) to pass to the planner. If the amendment is unambiguous, skip this step.
5. Invoke the **planner** subagent in update mode with:
   - The existing `_overview.md`, all task files, and the amendment text.
   - The frozen set list.
   - The `## Clarifications` block from step 4 (if any).
   - An explicit "update mode" flag in the prompt.
5. Planner's allowed actions in update mode:
   - **Add new task files** (`<next-id>.md`), all `status: pending`. New ids use the smallest free `t<N>` in the run.
   - **Edit `pending` / `blocked` / `deferred` task frontmatter**: `acceptance`, `edge_cases`, `skills_to_load`, `depends_on`, `intent`. May flip `blocked` → `pending` if the blocker is resolved by the amendment — but never `deferred` → `pending`; unfreezing a parked run is `/agentic-resume`'s job.
   - **Edit `pending`/`blocked`/`deferred` task sections**: `## Goal`, `## Requirements`, `## Constraints`, `## Edge cases`, `## Test outline` (editing deferred tasks keeps the plan current for when it's resumed).
   - **Update `_overview.md`**: `## Assumptions`, `## Resolved Questions`, `## Amendments` (append-only log of every update-plan run), task index.
6. Planner MUST NOT:
   - Touch any task in the frozen set.
   - Re-allocate the `run_id` or rename the directory.
   - Delete task files (mark superseded ones as `blocked` with `blocker: "superseded by t<N>"` instead).
   - Modify `## Design`, `## Decisions log`, `## Review`, or `## Test results` of any task.
   - Change a deferred task's `status`, `blocker`, or `resume_when` — deferral state is owned by `/agentic-defer` and `/agentic-resume`.
7. **Sketch injection (mirrors `/agentic-plan` step 7).** Determine if sketching is active for this run:

   If sketching is active and new sketchers will run, first write the run_id so logs are tagged:
   ```
   printf '%s' '<run_id>' > .claude/state/current_run.txt
   ```
   - Check `_overview.md` for a `## Clarifications` block containing `sketch: yes`, OR
   - Check if any `s<N>.md` files already exist in the run dir (i.e. sketching was used on the original plan).

   If sketching is active AND the planner added any new `role: implementer` tasks:

   **Validate first:** if any newly added task already has `role: sketcher`, the planner violated its contract — stop, print an error, and ask the developer to re-run `/agentic-update-plan`.

   a. **Create all sketcher task files first** (do not invoke any sketcher yet). For each newly added `role: implementer` task `t<N>.md`:
      - Create `s<N>.md` with:
        ```
        role: sketcher
        intent: "Sketch the UI for: <original implementer task intent>"
        sketches_for: t<N>
        skills_to_load: []
        depends_on: []
        status: pending
        blocker: null
        ```
      - Update `t<N>.md`'s `depends_on` to include `s<N>`.

   b. **Run all sketchers sequentially.** For each new `s<N>.md` in creation order:
      - Run: `printf '%s' '<s-id>' > .claude/state/current_task.txt && printf '%s' 'sketcher' > .claude/state/current_role.txt`
      - Invoke the **sketcher** agent with the path to `s<N>.md`.
      - Wait for it to flip `status: done` or `status: blocked`.
      - Run: `rm -f .claude/state/current_task.txt .claude/state/current_role.txt`
      - If `blocked`: print a warning and continue — do NOT halt. The implementer task will wait until the sketch is resolved.

   After all sketchers finish, clean up:
   ```
   rm -f .claude/state/current_run.txt .claude/state/current_task.txt .claude/state/current_role.txt
   ```

   If sketching is not active, or no new implementer tasks were added, skip this step entirely.

8. After planner (and optional sketch phase) finishes, print:
   - Which tasks were added, modified, or untouched.
   - The new task index (id → intent → status).
   - "next step: /agentic-build all" if there are runnable pending tasks.

## Amendment log entry

Every `/agentic-update-plan` invocation appends a block to `_overview.md` under `## Amendments`:

```markdown
### <timestamp> — <one-line summary>
- Trigger: <amendment text or "user-elicited">
- Added: [t4, t5]
- Modified: [t2 (acceptance), t3 (edge_cases)]
- Frozen (skipped): [t1]
```

## Constraints

- Single planner call (no MCP, no `retrieve`).
- All write-scope rules from `agents/planner.md` still apply, plus the frozen-set rule.
- `/agentic-build` afterward will only dispatch tasks whose deps are `done` — so newly added tasks may sit `pending` until their (also new) dependencies finish.
- **Does NOT touch git.** The feature branch was created by `/agentic-plan` at run start; update-plan operates on whatever branch the user is currently on. If you've left the feature branch, switch back yourself before running `/agentic-build`.
