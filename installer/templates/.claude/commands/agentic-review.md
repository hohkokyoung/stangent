---
description: Full-spectrum review over one scope — hygiene (auditor) + design (architect) + security (security-reviewer) — consolidated, then remediate everything via role agents.
argument-hint: "[commits:<N> | dir:<path> | all]"
---

# /agentic-review

The umbrella review. Runs all three analysis lenses over a single code scope,
prints **one consolidated report**, then — because remediation is the goal here —
turns every actionable finding into a fix task and dispatches it. This is the
"do everything" command; the focused siblings (`/agentic-cleanup`,
`/agentic-review-design`, `/agentic-review-security`, `/agentic-review-pr`) each
run one lens if you want a narrower pass.

Analysis is **read-only** (agents write only their own report). Remediation
**changes code** and is gated behind one explicit confirmation — nothing is
edited until you approve the fix-plan.

> **Cost + behavior note.** The architect and security-reviewer run on Opus and
> reason over the whole scope — a full `all` pass on a large repo is slow and
> not free. Prefer `commits:<N>` or `dir:<path>`. And unlike hygiene refactors,
> **design and security fixes intentionally change behavior** (adding authz
> checks, validation, RLS; restructuring a boundary). They must still pass all
> existing tests, but they warrant human review before merge — this command
> never commits.

## Procedure

### Step 1 — Allocate one review id

```bash
REVIEW_ID=REVIEW-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/audit/$REVIEW_ID \
         .claude/state/design-review/$REVIEW_ID \
         .claude/state/security-review/$REVIEW_ID
```

The same id is reused as each agent's sub-id, so the three reports are easy to
correlate.

### Step 2 — Resolve scope (YOU do this — do NOT delegate)

Parse `$ARGUMENTS`:
- `commits:<N>` → `scope=commits:<N>`
- `dir:<path>`  → `scope=dir:<path>`
- `all`         → `scope=all`

If `$ARGUMENTS` is empty, ask the developer — **one `AskUserQuestion` round**:
1. What to review? (recent commits / a directory / full codebase) — default `commits:10`.

Build a one-line human description of the scope for the review agents, e.g.
`"the working codebase, files touched in the last 10 commits"` or
`"the working codebase under backend/"`.

### Step 3 — Run the three analysis agents (SEQUENTIALLY)

Only one role may be armed at a time — the pre-tool hook reads a single
`current_role.txt`. Run them in this order, each: arm role → invoke → wait for
the report → clear role. **Never arm two roles at once.**

**3a. Auditor (hygiene).**
```bash
printf '%s' 'auditor' > .claude/state/current_role.txt
```
Invoke **auditor** with `audit_id=$REVIEW_ID`, `scope` (from Step 2),
`types=all`, `size_threshold` default (300 code / 200 docs). Wait for
`.claude/state/audit/$REVIEW_ID/findings.md`, then:
```bash
rm -f .claude/state/current_role.txt
```

**3b. Architect (design).**
```bash
printf '%s' 'architect' > .claude/state/current_role.txt
```
Invoke **architect** with `review_id=$REVIEW_ID` and
`scope="<scope description from Step 2>"` (free text — there is no plan here;
the architect reviews the existing code). Wait for
`.claude/state/design-review/$REVIEW_ID/findings.md`, then clear the role.

**3c. Security-reviewer (threat model).**
```bash
printf '%s' 'security-reviewer' > .claude/state/current_role.txt
```
Invoke **security-reviewer** with `review_id=$REVIEW_ID` and the same `scope`.
Wait for `.claude/state/security-review/$REVIEW_ID/findings.md`, then clear the role.

### Step 4 — Consolidate and present

Read all three reports and print ONE dashboard:

```
Full review — REVIEW-<ts>   scope: <scope>

HYGIENE (auditor)          High: N  Medium: N  Low: N
DESIGN (architect)         verdict: sound | concerns | reconsider
SECURITY (security-reviewer) verdict: no-blockers | hardening-needed | exploitable
```

Then print the findings grouped by lens. For security, print counts +
categories + the per-finding detail to the **terminal** (that is fine), but see
the constraint below about not copying verbatim exploit scenarios into tracked
files.

If **all three are clean** (hygiene High=Med=Low=0, design `sound`, security
`no-blockers`), print `No actionable findings across any lens.` and STOP.

### Step 5 — Plan remediation

Ask the developer — **one `AskUserQuestion` round**:
1. Which severities to fix? (all / high+medium / high only) — applied across all lenses.
2. Fix automatically, or review each task before dispatching?

Filter findings to the chosen severity. If nothing remains, print
`No findings at the selected severity.` and STOP.

Create a run + branch:
```bash
python3 .claude/hooks/lib/plan_id.py next          # -> run_id
python3 .claude/hooks/lib/git_branch.py create <run_id>   # if git.auto_branch
mkdir -p .claude/state/plans/<run_id>
```
Refuse if the working tree is dirty and `git.fail_on_wip` is true — tell the
developer to commit or stash first.

**Turn findings into tasks (grouped; ≤ 8 total).** Map each finding to the right
role and acceptance:

| Finding source | role | complexity | acceptance |
|---|---|---|---|
| Hygiene (dup / inconsistency / bad-practice / oversized) | `refactor` | low→high by scope | **behavior identical**, all existing tests pass, <concrete improvement> |
| Design (architect) that needs a code change | `implementer` | medium/high | design concern <X> addressed; existing tests pass; new behavior covered |
| Security (security-reviewer) mitigation | `implementer` | medium/high | mitigation for <category> implemented; existing tests pass; regression test added where feasible |

- Group related findings (same file/module/pattern) into one task — never one task per finding.
- `skills_to_load`: always `["project"]`; add relevant skills (`fastapi`, `react`, `owasp`, `supabase`, …) when the group touches that surface. Include `owasp` for every security task.
- Some design findings are **decisions, not code** ("pick a tenancy model", "record a retention policy"). These are NOT auto-fixable — do not manufacture a code task. List them under a `## Manual follow-ups` section and recommend `/agentic-adr new` or `/agentic-update-plan`.
- If findings exceed 8 groups, ask the developer to pick the highest-priority ones (do not silently drop).

Write each `.claude/state/plans/<run_id>/t<N>.md` from `.claude/templates/task.md`,
and `_overview.md` from `.claude/templates/overview.md` with `type: review` and a
`## Review sources` section linking the three report paths.

### Step 6 — Dispatch

If the developer chose **review each task first**: print the task index and STOP:
```
Tasks created in .claude/state/plans/<run_id>/
Review them, then run: /agentic-build all
```

If the developer chose **fix automatically**: dispatch sequentially (fixes must
not run in parallel). For each task in dependency order:
```bash
printf '%s' '<run_id>'  > .claude/state/current_run.txt
printf '%s' '<task-id>' > .claude/state/current_task.txt
printf '%s' '<role>'    > .claude/state/current_role.txt      # refactor | implementer
printf '%s' '<model>'   > .claude/state/current_model.txt     # complexity routing, per /agentic-build 7c
python3 .claude/hooks/lib/log_dispatch.py \
  --run_id '<run_id>' --task_id '<task_id>' --role '<role>' \
  --complexity '<complexity>' --role_baseline '<models.role>' \
  --model_selected '<model>' [--routing_applied if changed]
```
Invoke the task's role agent with the task file and selected model. Wait for
`status: done` or `status: blocked`, then:
```bash
rm -f .claude/state/current_task.txt .claude/state/current_role.txt .claude/state/current_model.txt
```
If `blocked`: print `review <task-id> blocked: <blocker>` and STOP — a blocked
fix means tests were already failing or a regression was introduced; resolve
manually.

### Step 7 — Clean state and report

```bash
rm -f .claude/state/current_run.txt .claude/state/current_task.txt \
      .claude/state/current_role.txt .claude/state/current_model.txt
```

Print:
```
Full review complete — <run_id>

Reports:
  hygiene:  .claude/state/audit/<REVIEW_ID>/findings.md
  design:   .claude/state/design-review/<REVIEW_ID>/findings.md
  security: .claude/state/security-review/<REVIEW_ID>/findings.md
Branch: <branch name>

Fixed:
✓  t1 (refactor)    — <intent>
✓  t2 (implementer) — <security: intent>
✗  t3 (implementer) — blocked: <reason>

Manual follow-ups (not auto-fixable):
•  <design decision> → /agentic-adr new "<title>"

Next: run your test suite, `git diff` to review — security & design fixes change
behavior — then commit when satisfied.
```

## Constraints

- **Sequential role handshakes only.** Never arm two roles at once; always clear
  `current_role.txt` between agents.
- **Analysis changes nothing.** The three review agents only write their own
  reports; all code edits happen in Step 6 via role agents, behind the Step 5
  confirmation.
- **Do NOT auto-commit.** Commits are user-driven — especially important since
  security/design fixes change behavior and need review.
- **Do NOT copy verbatim exploit scenarios into tracked files.** Security detail
  stays in the gitignored `.claude/state/` report. Task `intent`/`acceptance`
  reference the finding by category ("broken access control on the orders
  endpoint"), not the exploit steps.
- **Do NOT call any MCP tool yourself.**
- **Cap at 8 remediation tasks.** More than that → ask the developer to prioritize.
- **Fixes run sequentially**, never two in parallel.
