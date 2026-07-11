---
description: Open a GitHub pull request from a completed run's feat/<run_id> branch, with a body summarizing tasks and review verdicts.
argument-hint: "[run_id]"
---

# /agentic-open-pr

Open a PR from a finished `/agentic-build` run. The command owns all GitHub I/O.

## Preconditions

- github MCP enabled + `GITHUB_PERSONAL_ACCESS_TOKEN` filled (same check as
  `/agentic-review-pr`). If missing, print the fix and STOP.
- A GitHub `origin` remote exists (`git remote get-url origin`). If not, STOP —
  this command is GitHub-specific.

## Procedure

### Step 1 — Resolve the run and branch

- If `$ARGUMENTS` names a run id → use it.
- Else read `.claude/state/current_run.txt`; if absent, infer from the current
  branch if it matches the `feat/<run_id>` template.
- Compute the branch from `.agentic.yml: git.branch_template` (default
  `feat/{run_id}`). Confirm you are on it (`git rev-parse --abbrev-ref HEAD`); if
  not, check it out.
- Determine the base branch from `.agentic.yml: git.base_branch` (empty → the
  repo's default branch).

### Step 2 — Build the PR body (YOU do this — do NOT delegate)

Read `.claude/state/plans/<run_id>/_overview.md` and each `t*.md` for titles and
final `status`. Assemble:

```markdown
## Summary
<one-paragraph goal from _overview.md>

## Tasks
- ✅ t1 — <intent>
- ✅ t2 — <intent>
- ⛔ t3 — <intent> (blocked: <reason>)

## Reviews
<Only if reports exist for this run under .claude/state/design-review/ or
security-review/. Include the VERDICT and finding counts only — never exploit
scenarios or full design findings. e.g. "Design: concerns (1 High). Security:
hardening-needed (2 Medium)." If none were run, write "No design/security review
recorded — consider /agentic-review-design and /agentic-review-security.">

🤖 Generated with agentic /agentic-open-pr
```

### Step 3 — Confirm, push, open

Opening a PR is outward-facing. Show the resolved base←head, the title, and the
full body, then ask the user to confirm.

On confirmation:
```bash
git push -u origin <branch>      # normal push only — never --force
```
Then create the PR via the github MCP (base, head, title, body). Print the PR URL.

## Constraints

- Only the command calls the github MCP.
- Confirm before pushing or opening. Never force-push.
- Do NOT include security exploit detail or full design findings in the PR body —
  verdicts and counts only. Those reports stay in gitignored `.claude/state/`.
- Do NOT open a PR for a run whose tasks are all blocked — say so and STOP.
