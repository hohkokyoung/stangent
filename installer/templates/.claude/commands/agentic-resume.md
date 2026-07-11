---
description: Unfreeze a deferred run once its external blocker has cleared
argument-hint: "[run-id]"
---

# /agentic-resume

Reverse of `/agentic-defer`. Verifies the parked run's `resume_when` condition is actually met, flips its frozen tasks back to `pending`, and checks how stale the run got while parked.

## Arguments

- Optional: `<run-id>` (e.g. `FEAT-004`). If omitted, the command finds the deferred runs itself.

## Procedure

1. **Resolve the run.**
   - `$ARGUMENTS` given → use it.
   - Else collect deferred runs: rows with status `deferred` in `docs/FEATURES.md` (fallback if the file is missing: scan `_overview.md` frontmatter under `.claude/state/plans/`). Exactly one → use it; several → `AskUserQuestion`; none → say so and STOP.
2. **Load context.** Read the dossier (path from the `Dossier` column or the overview's `## Deferral` block) and `_overview.md`. If the run dir is missing under `.claude/state/plans/` (e.g. a fresh clone where run state wasn't committed), STOP and explain: the dossier is the surviving context; re-create the plan with `/agentic-plan`, feeding it the dossier's `## What's half-done / remaining`, then hand-set the dossier frontmatter to `status: resumed`.
3. **Verify the blocker cleared.** Print `resume_when` and confirm via `AskUserQuestion` that it is true now. If the condition is externally checkable with read-only tools you already have (e.g. `curl` a health URL), check it yourself and show the result. Not met → STOP, nothing modified.
4. **Unfreeze.** Every task with `status: deferred` → `status: pending`, `blocker: null`, `resume_when: null`. `done` tasks untouched.
5. **Reopen the overview.** `_overview.md` frontmatter `status: deferred` → `pending`; append `- Resumed: <UTC date>` to the `## Deferral` block (keep the block for traceability).
6. **Update the registry.** Dossier frontmatter: `status: resumed`, `resumed_on: <UTC date>`. Update the run's row in `docs/FEATURES.md` to `resumed`.
7. **Staleness check** (read-only), print the results:
   - Does the run's branch still exist? Has the base branch moved since the dossier's `last_commit` (`git log --oneline <last_commit>..<base>` count)? If drifted, recommend rebasing before building.
   - Recommend `/agentic-index` — code and skills likely changed while parked.
   - If anything in the dossier or `_overview.md ## Assumptions` no longer holds, recommend `/agentic-update-plan <run-id> <what changed>` BEFORE building; otherwise print `next step: /agentic-build all`.

## Constraints

- Only this command flips `deferred → pending`.
- Writes: task frontmatter (`status`, `blocker`, `resume_when`), `_overview.md`, the dossier frontmatter + `## Deferral` note, and the run's row in `docs/FEATURES.md`. Nothing else.
- Read-only on git — never rebase, switch, or commit for the user; recommend only.
