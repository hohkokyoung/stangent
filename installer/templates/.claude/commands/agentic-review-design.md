---
description: Run a system-level design review of a plan (or a described feature) — data ownership, privacy, tenancy, compliance, scalability. Reports findings; changes nothing.
argument-hint: "[<run_id> | \"<feature description>\"]"
---

# /agentic-review-design

Adversarial design review by the **architect** agent. Read-only — it produces a
findings report and never edits code or the plan.

## Procedure

### Step 1 — Allocate a review ID

```bash
REVIEW_ID=DR-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/design-review/$REVIEW_ID
```

### Step 2 — Resolve input (YOU do this — do NOT delegate)

- If `$ARGUMENTS` matches a run id (e.g. `FEAT-003`) and
  `.claude/state/plans/$ARGUMENTS/` exists → pass `run_id=$ARGUMENTS`.
- If `$ARGUMENTS` is free text → pass `scope="$ARGUMENTS"`.
- If `$ARGUMENTS` is empty → default to the most recent run under
  `.claude/state/plans/` (highest FEAT id). If none exists, tell the user to run
  `/agentic-plan` first and STOP.

### Step 3 — Arm the hook and dispatch

Write role state first so the pre-tool hook enforces the architect's write-scope
(`.claude/state/design-review/` only):

```bash
printf '%s' 'architect' > .claude/state/current_role.txt
```

Invoke the **architect** agent with `review_id` and the resolved `run_id` /
`scope`. Wait for it to write
`.claude/state/design-review/<review_id>/findings.md` and print its summary.
Then clear the state (mandatory):

```bash
rm -f .claude/state/current_role.txt
```

### Step 4 — Present

Read `.claude/state/design-review/$REVIEW_ID/findings.md` and print it verbatim.

- Verdict `reconsider` → recommend `/agentic-update-plan <run_id>` (or
  `/agentic-adr new` if a decision needs recording) **before** any build.
- Verdict `concerns` → summarize the High/Medium findings; the developer decides
  what to fold into the plan.
- Verdict `sound` → one-line confirmation.

## Constraints

- Do NOT fix anything or edit the plan. Findings are advisory.
- Do NOT call any MCP tool yourself. Do NOT commit.
