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

Resolve the architect's model first: `models.architect` from `.agentic.yml` (fall
back to `models.default`, then the session default). Then arm the hook —
`current_model.txt` is what puts the model on every logged tool call:

```bash
printf '%s' "$REVIEW_ID" > .claude/state/current_run.txt   # log context: tool calls land in logs/$REVIEW_ID.jsonl
printf '%s' 'architect' > .claude/state/current_role.txt
printf '%s' '<resolved_model>' > .claude/state/current_model.txt
```

Invoke the **architect** agent with `review_id`, the resolved `run_id` /
`scope`, and that model — pass it explicitly at invocation, do not rely on the
agent inheriting the session model. Wait for it to write
`.claude/state/design-review/<review_id>/findings.md` and print its summary.
Then clear the state (mandatory):

```bash
python3 .claude/hooks/lib/state.py clear
```

### Step 3b — Verify the architect's citations

```bash
${PYEXE:-python3} .claude/hooks/lib/verify_clears.py \
  .claude/state/design-review/$REVIEW_ID/findings.md --cwd . \
  --checklist .claude/agents/architect.md
```

No `--enumerations`: the architect's seven dimensions are questions about a
design — who owns this entity, what breaks at 100×, can one tenant reach
another's data — with no population of sites to search. Declaring one here
would be cargo-culting the mechanism.


`--checklist` counts the agent's design dimensions and requires one `## Coverage`
row per dimension, so a dimension that was never interrogated fails rather than
passing as silence.

Print its output **above** the findings. `mismatch` / `failed` / `uncited` means
a dimension listed under "Dimensions with no issues" could not be re-derived —
treat it as unreviewed and say so. `unverified` is the honest outcome and needs
no action. Do not edit the findings or re-dispatch to make it pass.

### Step 4 — Present

Read `.claude/state/design-review/$REVIEW_ID/findings.md` and print it verbatim.

- Verdict `reconsider` → recommend `/agentic-update-plan <run_id>` (or
  `/agentic-adr new` if a decision needs recording) **before** any build.
- Verdict `concerns` → summarize the High/Medium findings; the developer decides
  what to fold into the plan.
- Verdict `sound` → one-line confirmation.

## Constraints

- Do NOT fix anything or edit the plan. Findings are advisory — remediation is
  `/agentic-remediate <review_id>` for findings that are code changes. Design
  findings that are *decisions* (tenancy model, retention policy) are not
  auto-fixable; those go to `/agentic-adr new` or `/agentic-update-plan`.
- Do NOT call any MCP tool yourself. Do NOT commit.
