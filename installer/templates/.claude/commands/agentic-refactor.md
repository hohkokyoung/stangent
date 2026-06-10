---
description: Clarify, decompose, and execute a refactoring goal — no new behavior, tests must stay green
argument-hint: "<refactoring goal>"
---

# /agentic-refactor

Run a refactoring session: clarify scope → create task file(s) → run the refactor agent → report.

## Procedure

1. **Allocate a run_id:**
   ```
   python3 .claude/hooks/lib/plan_id.py next
   ```

2. **Create the feature branch** (if `git.auto_branch` is true in `.agentic.yml`):
   ```
   python3 .claude/hooks/lib/git_branch.py create <run_id>
   ```
   Refuse (exit 1) if the working tree has uncommitted changes and `git.fail_on_wip` is true.
   Tell the user to commit or stash, then re-run.

3. **Create the run dir:**
   ```
   mkdir -p .claude/state/plans/<run-id>
   ```

4. **Clarification phase (YOU do this — do NOT delegate).**

   Walk this checklist. Batch related questions into one `AskUserQuestion` round.
   Up to **3 rounds**, up to **3 questions per round**.

   | Dimension | What to confirm |
   |---|---|
   | **What changes** | Which code specifically? Dead code removal, rename, extract function, consolidate duplication, simplify conditionals, move files? |
   | **Why now** | What's the pain? Readability, onboarding friction, bug-prone complexity, performance? |
   | **Blast radius** | Single file, one module, or cross-cutting? Any consumers outside this repo that could break? |
   | **Test coverage** | Does the affected code have tests? If not — add tests first before refactoring, or accept the risk consciously? |
   | **Public API** | Are any public-facing signatures (HTTP endpoints, exported functions, CLI arguments) changing? |

   Rules:
   - After 3 rounds with blocking gaps unresolved, list them and STOP.
   - Never ask about implementation details — the refactor agent decides how.
   - Collect answers into a `## Clarifications` block.

5. **Create task file(s) directly** (no planner agent). Refactor tasks are almost always 1–2 tasks;
   use your judgment from the Clarifications block. Write each as `.claude/state/plans/<run-id>/t<N>.md`
   using the task template at `.claude/templates/task.md` with these specifics:
   - `role: refactor`
   - `skills_to_load: ["project"]` — always include `"project"` so the agent can retrieve existing code;
     add any skill relevant to the language/framework (e.g. `fastapi`, `react`) if the goal touches
     framework-specific patterns
   - `acceptance`: phrase as "behavior is identical before and after; all existing tests pass; <concrete improvement>"
   - `edge_cases`: enumerate the edge cases of the refactoring itself (concurrent callers of renamed function, places that import the moved module, etc.)
   - Do NOT set `depends_on` unless you genuinely have two tasks where one must land before the other.
   - Also write `_overview.md` using `.claude/templates/overview.md`. Mark `type: refactor` in the frontmatter goals section.

6. **Write run_id to state:**
   ```
   printf '%s' '<run_id>' > .claude/state/current_run.txt
   ```

7. **Dispatch each task sequentially** (refactors must not run in parallel — they touch overlapping code):
   For each `t<N>.md`:
   a. `printf '%s' '<t-id>' > .claude/state/current_task.txt && printf '%s' 'refactor' > .claude/state/current_role.txt`
   b. Invoke the **refactor** agent with the task file path.
   c. Wait for it to flip `status: done` or `status: blocked`.
   d. `rm -f .claude/state/current_task.txt .claude/state/current_role.txt`
   e. If `blocked`: print `refactor <task-id> blocked: <blocker>` and STOP — do not continue to the next task.
      A blocked refactor likely means tests are already failing or a regression was introduced; it must be resolved manually.

8. **Clean up state:**
   ```
   rm -f .claude/state/current_run.txt .claude/state/current_task.txt .claude/state/current_role.txt
   ```

9. **Print summary:**
   - `run_id` and branch name
   - Task index: id → intent → status
   - Test run result (pass/fail count from the refactor agent's final test run, if recorded in `## Design`)
   - Next steps: "review with /agentic-build <reviewer-task-id>" if you want a formal review,
     or "git diff to inspect changes, then commit when satisfied"

## Constraints

- Do NOT add new behavior. If the goal description includes "also add X" — split that into a separate `/agentic-plan` run.
- Do NOT call any MCP tool yourself.
- Do NOT commit. Commits are user-driven.
- Refactor tasks always run sequentially — never dispatch two refactor agents in parallel.
