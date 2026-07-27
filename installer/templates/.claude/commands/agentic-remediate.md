---
description: Turn an existing review's findings into fix tasks and dispatch them. Consumes a report you already paid for — /agentic-review-ui, -security, -design, -pr, or an audit — instead of re-running analysis.
---

# /agentic-remediate

`/agentic-review-ui`, `/agentic-review-security`, `/agentic-review-design` and
`/agentic-review-pr` are deliberately advisory: they end at a report and change
nothing. `/agentic-review` remediates, but only from lanes it runs itself — it
takes a *code scope*, never a review id, so it cannot read a report that already
exists.

That gap is what this command closes. It reads a finished `findings.md`, verifies
the evidence still holds, turns findings into tasks, and hands them to the same
dispatch path `/agentic-build` uses. Re-running four analysis lanes to rediscover
findings you already have is the alternative, and it costs several times more.

## Preconditions

- A completed review under `.claude/state/{ui-review,security-review,design-review,pr-review,audit}/<review_id>/findings.md`
- Clean working tree if `git.fail_on_wip` is true

## Procedure

### Step 1 — Resolve the review

Parse `$ARGUMENTS`:
- a review id (`UIR-…`, `SEC-…`, `DR-…`, `PR-…`, `AUD-…`) → find its `findings.md`
  across the review dirs
- empty → list the five most recent reports with their date, verdict, and finding
  count, and ask **one `AskUserQuestion`** round to pick one

If the id matches nothing, say so and stop — do not fall back to running a fresh
review. The developer asked to remediate a specific report; silently reviewing
something else would bill them for work they did not request.

Read the report and extract each finding: its id, severity, spec/category tag,
`**Where:**` sites, and suggested fix.

### Step 2 — Verify the evidence still holds (mandatory)

```bash
${PYEXE:-python3} .claude/hooks/lib/verify_clears.py <findings_path> --cwd .
```

A report is a snapshot. Between the review and now, the code may have moved,
someone may have fixed half of it, or line numbers may have drifted — and
remediating from stale evidence means writing tasks against sites that no longer
exist, or worse, "fixing" something already fixed.

Act on the result:
- **`mismatch` / `failed` on a finding's site list** → that finding's scope is
  wrong. Re-derive it by re-running its `enumerated by:` search yourself and use
  the current results, or drop the finding and say so. Never write a task from a
  site list that no longer reproduces.
- **`partially inspected`** → carry the caveat into the task: the fix covers the
  sites listed, not necessarily the class. Note it in the task's `## Goal`.
- **`uncited` clears** → irrelevant here; you are remediating findings, not clears.

Print the verifier output before proceeding.

### Step 3 — Choose scope

**One `AskUserQuestion`** round:
1. Which severities to fix? (all / high+medium / high only)
2. Fix automatically, or review each task before dispatching?

Filter to the chosen severity. If nothing remains, print
`No findings at the selected severity.` and STOP.

### Step 4 — Create the run

```bash
python3 .claude/hooks/lib/plan_id.py next                  # -> run_id
python3 .claude/hooks/lib/git_branch.py create <run_id>    # if git.auto_branch
mkdir -p .claude/state/plans/<run_id>
```

Refuse if the working tree is dirty and `git.fail_on_wip` is true — tell the
developer to commit or stash first.

### Step 5 — Turn findings into tasks

Map each finding to a role and acceptance criteria:

| Finding source | role | complexity | acceptance |
|---|---|---|---|
| UI drift — token/colour/spacing/state | `refactor` | low/medium | on-spec: raw values replaced with tokens, missing states added; **look/behaviour otherwise unchanged**; tests pass |
| UI structural — layout/component restructure | `implementer` | medium | spec rule <X> satisfied; existing tests pass |
| Hygiene — duplication/inconsistency/oversized | `refactor` | low→high by scope | **behavior identical**, all existing tests pass |
| Security mitigation | `implementer` | medium/high | mitigation for <category> implemented; existing tests pass; regression test added where feasible |
| Design (architect) needing code change | `implementer` | medium/high | design concern <X> addressed; tests pass; new behavior covered |

Rules:
- **Group related findings** (same file/module/pattern) into one task — never one
  task per finding.
- **A token-level defect is one task, not one per call site.** When the finding is
  that a *token value* is wrong, fixing the token repairs every usage at once;
  writing a task per site both misses the point and multiplies the work.
- `skills_to_load`: always `["project"]`, plus the relevant stack skill. Include
  `owasp` for every security task, and the frontend skill for every UI task.
- **Decisions are not code.** "Pick a retention policy", "choose a tenancy model",
  and a spec that contradicts the code are not auto-fixable. List them under
  `## Manual follow-ups` and recommend `/agentic-adr new` or `/agentic-design`.
- **A reviewer task needs a tester after it**, or nothing can finalize it.
- Cap at 8 tasks. Beyond that, ask the developer to prioritize — never silently
  drop findings.

Write each `.claude/state/plans/<run_id>/t<N>.md` from `.claude/templates/task.md`,
and `_overview.md` from `.claude/templates/overview.md` with `type: remediation`
and a `## Review sources` section citing the review id and its `findings.md` path,
so the plan records what it came from.

### Step 6 — Dispatch

If the developer chose **review each task first**, print the index and STOP:
```
Tasks created in .claude/state/plans/<run_id>/
Review them, then run: /agentic-build all
```

If **fix automatically**, dispatch sequentially (fixes must not run in parallel).
For each task in dependency order:
```bash
printf '%s' '<run_id>'  > .claude/state/current_run.txt
printf '%s' '<task-id>' > .claude/state/current_task.txt
printf '%s' '<role>'    > .claude/state/current_role.txt
printf '%s' '<model>'   > .claude/state/current_model.txt
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
If `blocked`: print `remediate <task-id> blocked: <blocker>` and STOP.

### Step 7 — Clean state and report

```bash
rm -f .claude/state/current_run.txt .claude/state/current_task.txt \
      .claude/state/current_role.txt .claude/state/current_model.txt
```

Print:
```
remediated <review_id> -> <run_id>   tasks: N done, M blocked
manual follow-ups: <count>
re-run <the original review command> to confirm the findings are closed
```

Recommending the re-review is the point: this command fixes what a report *said*,
and only a fresh review establishes that the class is actually closed. A previous
remediation pass fixed all eleven sites it was given and left a twelfth the
original review never found.

## Constraints

- Do NOT re-run any analysis agent. If the report is too stale to use, say so and
  recommend re-running the original review command — do not quietly replace it.
- Do NOT edit the findings report. It is the record of what was true then.
- Do NOT commit. The developer reviews the diff.
- Findings whose evidence no longer reproduces are re-derived or dropped, never
  used as-is.
