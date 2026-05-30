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

4. **Clarification phase (YOU do this — do NOT delegate to the planner).**

   Walk the coverage checklist below. For every dimension where a blocking ambiguity remains after re-reading the user's goal, batch related questions into one `AskUserQuestion` round. Repeat up to **4 rounds**, up to **3 questions per round**.

   | Dimension | What to confirm |
   |---|---|
   | **Scope** | New feature, extension, or refactor? Touches existing screens/endpoints/tables? |
   | **Functional requirements** | Exact user-visible behavior — inputs, outputs, state transitions. |
   | **Acceptance criteria** | Concrete, testable bullets for "done." |
   | **Edge cases** | Empty/null/zero, max sizes, concurrent edits, offline, partial failure, idempotency. |
   | **Auth & permissions** | Who can do this? Anonymous, authenticated, owner-only, admin? RLS implications? |
   | **Security surface** | Does the goal add an HTTP endpoint, browser-facing UI, form, file upload, user input reaching a DB query, auth flow, or outbound HTTP from user-supplied URL? |
   | **Validation** | Field constraints (lengths, ranges, formats, enums). Server-side, client-side, or both? |
   | **Error UX** | What does the user see on failure? 401 vs 403, retry vs hard-fail, toast vs full-screen. |
   | **Data model impact** | New tables/columns? Migrations? Backfill? RLS policies? |
   | **API surface** | New endpoints? Breaking changes? Versioning? Idempotency keys? |
   | **Non-functional** | Perf budgets, payload sizes, rate limits, observability, audit log. |

   Rules:
   - Provide a default suggestion per question so the user can answer with one word or "default fine."
   - Never ask about file names, class names, or implementation choices — those belong to the implementer.
   - Never ask questions the goal already answers.
   - If the user says "skip" or "use your judgment" for a question, record an assumption and move on.
   - After 4 rounds with unresolved blocking gaps: tell the user which gaps remain and STOP. Do NOT create `.claude/state/plans/<run-id>/` or invoke the planner.

   Collect all answers into a `## Clarifications` block (format below) to pass to the planner.

   ```markdown
   ## Clarifications
   - Q: <question> → A: <answer>
   - ASSUMPTION: <statement> (user declined / judgment call)
   ```

5. Invoke the **planner** agent with:
   - The user goal: `$ARGUMENTS`
   - The generated `run_id`
   - The contents of `.claude/.agentic.yml`
   - The `## Clarifications` block from step 4

6. The planner writes `_overview.md` + per-task files (all `status: pending`).

7. After planner finishes, print:
   - `run_id`
   - feature branch name (or "no branch — not a git repo / auto_branch disabled")
   - task index (id → intent → role → status)
   - "next step: /agentic-build all" (or `/agentic-build <task-id>`)

## Constraints

- Do NOT call any MCP tool yourself. The planner has its own constraints.
- Do NOT modify task files outside the planner.
- Do NOT dispatch tasks; that's `/agentic-build`.
- Do NOT commit or push anything yourself. Branch creation is the only git operation. Commits and merges are user-driven.
