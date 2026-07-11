---
description: Park a half-finished run on an external blocker — freeze tasks, export a committed handoff dossier
argument-hint: "[run-id] <why it stopped>"
---

# /agentic-defer

Parks a run that cannot progress for **external** reasons — backend not deployed yet, credentials pending, a third party or another team not ready. This is different from `blocked` (an agent failed at its job) and from `/agentic-update-plan` (the scope changed). Deferral means: *the plan is fine, the world isn't ready.*

Run state under `.claude/state/` is gitignored working memory — none of it written for humans, none of it surviving a fresh clone. A parked run's context — what shipped, what's half-done, why it stopped — evaporates from memory long before then. This command exports that context to a **committed** dossier under `docs/features/` and registers it in `docs/FEATURES.md`, so the feature can be resumed cold, by anyone.

## Arguments

- First arg (optional): `<run-id>` (e.g. `FEAT-004`). Omit it for features that were never planned through `/agentic-plan` — deferral works without a run (see **no-run mode** below).
- Remaining args: free text — why the work stopped.

## Procedure

1. **Resolve the run — or decide there isn't one.**
   - Explicit `<run-id>` given → use it.
   - Otherwise read the `_overview.md` goal of every run under `.claude/state/plans/` and compare against the reason text / session context. A run qualifies only if its goal plausibly describes the same feature — **recency alone does NOT qualify it**. Exactly one qualifying run → confirm via `AskUserQuestion` ("Defer <run-id> — <goal>?") before touching it; several → ask which; none → **no-run mode**.
   - NEVER freeze a run whose goal does not match the deferral reason — freezing the wrong run silently corrupts unrelated work.

   With a run resolved, read every task file plus `_overview.md`. Refuse (print why, STOP) if:
   - every task is `done` — nothing to defer, the run is complete;
   - the overview is already `status: deferred` — point at the existing dossier instead.
2. If no reason text was given, use `AskUserQuestion` to elicit:
   - the external dependency that stops the run (becomes `blocked_on`);
   - the observable condition under which work resumes (becomes `resume_when`).
   If reason text was given but no resume condition can be inferred from it, ask only for the resume condition.
3. Distill two strings:
   - `blocked_on` = `"external: <dependency>"` — see `templates/blocker-reference.md`; this is the only blocker family set by a human command, never by an agent.
   - `resume_when` = one **observable** condition ("backend `/health` returns 200 on staging"), not a vague hope ("later").
4. **Freeze the tasks.** For every task whose `status` is NOT `done`: set `status: deferred`, `blocker: "<blocked_on>"`, `resume_when: "<resume_when>"`. Never touch `done` tasks or any body section.
5. **Mark the overview.** In `_overview.md`: set frontmatter `status: deferred` and fill the `## Deferral` section:
   ```markdown
   ## Deferral
   - Deferred: <UTC date> — <reason text>
   - Blocked on: external: <dependency>
   - Resume when: <condition>
   - Dossier: docs/features/<run-id>-<slug>.md
   ```
6. **Gather git facts** (read-only — never switch branches, commit, or push): does the run's branch exist (`.agentic.yml: git.branch_template`, default `feat/{run_id}`)? If so, capture its last commit short SHA + subject.
7. **Write the dossier.** Copy `.claude/templates/feature-dossier.md` to `docs/features/<run-id>-<slug>.md` (slug from the overview's one-line goal; create `docs/features/` if missing) and fill every section:
   - frontmatter from steps 3 and 6; `deferred_on` = today (UTC);
   - `## Goal` from `_overview.md`;
   - `## What shipped` — one bullet per `done` task: id, intent, and the load-bearing lines of its `## Decisions log`;
   - `## What's half-done / remaining` — one bullet per deferred task: id, intent, how far it got (`## Design` filled? never started?);
   - `## Why it stopped` — the reason in plain words;
   - `## Resume checklist` — fill the placeholders with real values;
   - `## Context that will be lost otherwise` — anything not recoverable from code or task files. If the session surfaced any (verbal agreements, external tickets, gotchas), record them; otherwise ask the user once via `AskUserQuestion` whether there is any.
8. **Register in the index.** Create `docs/FEATURES.md` if missing:
   ```markdown
   # Feature registry

   Parked and shipped features. Dossiers live in [features/](features/). Rows are managed by /agentic-defer and /agentic-resume — hand edits welcome.

   | Run | Feature | Status | Branch | Blocked on | Resume when | Dossier |
   |---|---|---|---|---|---|---|
   ```
   Then upsert this run's row, e.g.:
   ```markdown
   | FEAT-004 | chat attachments | deferred | feat/FEAT-004 | backend not deployed | staging API live | [dossier](features/FEAT-004-chat-attachments.md) |
   ```
9. Print a summary: frozen task ids, dossier path, and a reminder that the dossier only survives if committed — suggest `git add docs/FEATURES.md docs/features/ && git commit`, but do NOT run it yourself unless the user asks.

## No-run mode (feature built outside `/agentic-plan`)

When step 1 finds no qualifying run, there are no task files to freeze — produce only the dossier and registry row. Skip steps 4–5 and adjust the rest:

- Source the dossier from the session, the code, and git history instead of task files: `## What shipped` / `## What's half-done` come from what you actually know. Ask the user to fill gaps rather than guessing.
- Git facts (step 6): use the branch the work actually lives on (usually the current branch).
- Dossier path: `docs/features/<slug>.md`; frontmatter `run_id: null`.
- Registry row `Run` column: `-`.
- Tell the user resume also works run-less: `/agentic-resume` matches the feature by its dossier and, once the blocker clears, seeds a fresh `/agentic-plan` from `## What's half-done / remaining`.

## Constraints

- Task-file writes are limited to the frontmatter fields `status`, `blocker`, `resume_when` of non-`done` tasks. Body sections untouched.
- Never touch `done` tasks. Never touch source code. Never switch branches, commit, or push.
- `docs/features/` and `docs/FEATURES.md` are the ONLY writes outside `.claude/state/`.
- Deferral is reversed only by `/agentic-resume` — neither agents nor `/agentic-update-plan` may flip `deferred` back to `pending`.
