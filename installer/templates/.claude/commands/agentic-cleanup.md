---
description: CODE cleanup — audit source for inconsistencies, duplication, and bad practices, then create and dispatch refactor tasks to fix them. (For .claude/state/ hygiene, use /agentic-clean-state.)
argument-hint: "[commits:<N> | dir:<path> | all]"
---

# /agentic-cleanup

Two-phase command: **audit** (discover problems) → **cleanup** (fix them via the refactor agent).

> **This cleans CODE, not state.** It refactors your source files. To prune
> `.claude/state/` (old runs, logs, empty review dirs), use `/agentic-clean-state`.

## Arguments

| Argument | What it does |
|---|---|
| *(none)* | Interactive — asks what to scan |
| `commits:<N>` | Audit only files touched in the last N commits (e.g. `commits:10`) |
| `dir:<path>` | Audit a specific directory (e.g. `dir:src/api`) |
| `all` | Audit the full codebase |

---

## Procedure

### Step 1 — Allocate an audit ID

```bash
AUDIT_ID=AUDIT-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/audit/$AUDIT_ID
```

### Step 2 — Clarification phase (YOU do this — do NOT delegate)

If `$ARGUMENTS` is empty, ask the developer — **one `AskUserQuestion` round, up to 3 questions**. (1 round because cleanup scope is concrete once you know what changed recently.)

| Question | Suggested default |
|---|---|
| What should be scanned? (recent commits / specific dir / full codebase) | `commits:10` |
| Which issue types? (all / inconsistency / duplication / bad-practice / oversized) | `all` |
| Line-count threshold for "oversized" files? | 300 for code, 200 for docs/markdown |

If `$ARGUMENTS` is provided, parse it directly:
- `commits:<N>` → `scope=commits:<N>`
- `dir:<path>` → `scope=dir:<path>`
- `all` → `scope=all` — every file *eligible*, not every file read. This is a
  sampling audit; `/agentic-sweep audit` is the exhaustive form. Say so in one
  line when the developer chooses it, especially since duplication and
  inconsistency are cross-file properties that a partial pass reports as absent
  rather than as unchecked.

Use `types=all` and default thresholds unless the developer specified otherwise.

### Step 3 — Run the auditor

Write role state first so the pre-tool hook enforces the auditor's write-scope (`.claude/state/audit/` only):
```bash
printf '%s' 'auditor' > .claude/state/current_role.txt
```

Invoke the **auditor** agent with:
- `audit_id`: the allocated ID
- `scope`: from step 2
- `types`: from step 2
- `size_threshold`: from step 2

Wait for it to write `.claude/state/audit/<audit_id>/findings.md` and print its summary. Then clear the state (mandatory — do not skip, so the refactor dispatch in step 6 starts clean):
```bash
python3 .claude/hooks/lib/state.py clear --agent
```

### Step 3b — Verify the auditor's citations

```bash
${PYEXE:-python3} .claude/hooks/lib/verify_clears.py \
  .claude/state/audit/<audit_id>/findings.md --cwd . \
  --checklist .claude/agents/auditor.md \
  --enumerations docs/review/enumerations.md --reviewer auditor
```

If that output reports `unverified — no enumeration declared` for any item, or
any `[FAIL] undeclared` / `search-broken` row, follow **How this file gets
populated** in `.claude/templates/review-enumerations.md`. Propose the rows, show
them, and write `docs/review/enumerations.md` only on the developer's approval —
the first run's search is improvised by definition, and this is the moment it
becomes the permanent definition of the item's scope. Never write the file
silently.


`--checklist` requires one `## Coverage` row per issue type — including types the
caller did not request, marked `not requested`. An unscanned type otherwise
leaves no trace, which reads exactly like a type that came back clean.

Print its output **above** the findings. A `No <type> issues found.` line whose
scan does not reproduce means that issue type was not actually swept — say so
when presenting, and do not let it narrow the refactor scope you propose in
step 5. `unverified` is the honest outcome and needs no action.

### Step 4 — Present findings and confirm

Read `.claude/state/audit/<audit_id>/findings.md`. Print the full findings to the developer.

If there are **no findings at all** (High=0, Medium=0, Low=0):
```
[agentic-cleanup] No issues found. Codebase looks clean for the scanned scope.
```
STOP.

Otherwise, ask the developer — **one `AskUserQuestion` round**:
1. Which severity levels should be fixed? (all / high+medium / high only)
2. Fix automatically, or review each task before dispatching?

Filter the findings list to the chosen severity. If the filtered list is empty after filtering, print "No findings at the selected severity" and STOP.

### Step 5 — Create a run and refactor tasks

Allocate a run_id:
```bash
python3 .claude/hooks/lib/plan_id.py next
```

Create the feature branch (if `git.auto_branch` is true in `.agentic.yml`):
```bash
python3 .claude/hooks/lib/git_branch.py create <run_id>
```
Refuse if the working tree has uncommitted changes and `git.fail_on_wip` is true — tell the developer to commit or stash first.

Create the run dir:
```bash
mkdir -p .claude/state/plans/<run_id>
```

**Group findings into tasks.** Findings of the same type touching the same area of the codebase should be grouped into one task — do not create one task per finding. Rule of thumb:
- All inconsistencies in a single file or closely related files → one task
- All duplication of the same pattern across files → one task
- Bad practices in the same module or layer → one task
- Each oversized file that needs splitting → one task

For each group, write `.claude/state/plans/<run_id>/t<N>.md` using `.claude/templates/task.md` with:
- `role: refactor`
- `complexity`: `low` for naming/wording fixes, `medium` for extraction/consolidation, `high` for cross-cutting restructuring
- `skills_to_load: ["project"]` — always include `"project"`; add any relevant skill (e.g. `react`, `fastapi`) if the group touches framework-specific patterns
- `intent`: one-line description referencing the specific findings (e.g. "Consolidate duplicate error-handler boilerplate in src/api/")
- `acceptance`: "behavior is identical before and after; all existing tests pass; <concrete improvement from findings>"
- `edge_cases`: enumerate the refactoring risks (callers of renamed symbols, files that import moved modules, etc.)

Also write `_overview.md` using `.claude/templates/overview.md`. Set `type: cleanup` in the goals section. Include a `## Audit source` section referencing `.claude/state/audit/<audit_id>/findings.md`.

### Step 6 — Dispatch

If the developer chose "review each task first": print the task index and STOP with:
```
Tasks created in .claude/state/plans/<run_id>/
Review them, then run: /agentic-build all
```

If the developer chose "fix automatically": dispatch via the standard build algorithm — run:
```bash
printf '%s' '<run_id>' > .claude/state/current_run.txt
```
Then for each task in order (sequential — refactors must not run in parallel):

a. Read `complexity` from the task frontmatter. Determine `selected_model` using complexity routing from `.agentic.yml` (same as `/agentic-build` step 7c).
b. Write state files and log the dispatch:
   ```bash
   printf '%s' '<task-id>' > .claude/state/current_task.txt
   printf '%s' 'refactor' > .claude/state/current_role.txt
   printf '%s' '<selected_model>' > .claude/state/current_model.txt
   python3 .claude/hooks/lib/log_dispatch.py \
     --run_id '<run_id>' --task_id '<task_id>' --role refactor \
     --complexity '<complexity>' --role_baseline '<models.refactor>' \
     --model_selected '<selected_model>' [--routing_applied if model changed]
   ```
c. Invoke the **refactor** agent with the task file path and `selected_model`.
d. Wait for `status: done` or `status: blocked`.
e. `python3 .claude/hooks/lib/state.py clear --agent`
f. If `blocked`: print `cleanup <task-id> blocked: <blocker>` and STOP. A blocked refactor means tests were already failing or a regression was introduced — must be resolved manually.

### Step 7 — Clean up state and report

```bash
python3 .claude/hooks/lib/state.py clear
```

Print:
```
Cleanup complete — <run_id>

Audit: .claude/state/audit/<audit_id>/findings.md
Branch: <branch name>

Tasks:
✓  t1 — <intent>
✓  t2 — <intent>
✗  t3 — blocked: <reason>

Next: git diff to inspect changes, then commit when satisfied.
```

---

## Constraints

- Do NOT fix anything yourself. All code changes go through the refactor agent.
- Do NOT call any MCP tool yourself.
- Do NOT commit. Commits are user-driven.
- Refactor tasks always run sequentially — never dispatch two in parallel.
- Do NOT create more than 8 tasks. If findings exceed 8 groups, ask the developer to pick the highest-priority ones.
