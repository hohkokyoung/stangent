---
description: Red-team a plan or implemented feature — OWASP Top 10, authz/IDOR, injection, secrets, abuse. Reports a threat model; changes nothing.
argument-hint: "[<run_id> | \"<feature description>\"]"
---

# /agentic-review-security

Adversarial security review by the **security-reviewer** agent. Read-only — it
produces a threat-model report and never edits code, runs migrations, or holds
live credentials.

## Procedure

### Step 1 — Allocate a review ID

```bash
REVIEW_ID=SEC-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/security-review/$REVIEW_ID
```

### Step 2 — Resolve input (YOU do this — do NOT delegate)

- If `$ARGUMENTS` matches a run id and `.claude/state/plans/$ARGUMENTS/` exists →
  pass `run_id=$ARGUMENTS` (and note that its diff, if implemented, is in scope).
- If `$ARGUMENTS` is free text → pass `scope="$ARGUMENTS"`.
- If empty → default to the most recent run under `.claude/state/plans/`. If none
  exists, ask the user for a feature description or run id and STOP.

### Step 3 — Arm the hook and dispatch

Resolve the reviewer's model first: `models.security-reviewer` from
`.agentic.yml` (fall back to `models.default`, then the session default). Then
arm the hook — `current_model.txt` is what puts the model on every logged tool
call, so a run whose cost looks wrong can be traced to the model that actually
ran:

```bash
printf '%s' "$REVIEW_ID" > .claude/state/current_run.txt   # log context: tool calls land in logs/$REVIEW_ID.jsonl
printf '%s' 'security-reviewer' > .claude/state/current_role.txt
printf '%s' '<resolved_model>' > .claude/state/current_model.txt
```

Invoke the **security-reviewer** agent with `review_id`, the resolved
`run_id` / `scope`, and that model — pass it explicitly at invocation, do not
rely on the agent inheriting the session model. Wait for it to write
`.claude/state/security-review/<review_id>/findings.md` and print its summary.
Then clear the state (mandatory):

```bash
sh .claude/py .claude/hooks/lib/state.py clear
```

### Step 4 — Verify the reviewer's clears

Re-run the evidence cited for every category the reviewer marked cleared:

```bash
sh .claude/py .claude/hooks/lib/verify_clears.py \
  .claude/state/security-review/$REVIEW_ID/findings.md --cwd . \
  --checklist .claude/agents/security-reviewer.md \
  --enumerations docs/review/enumerations.md --reviewer security-reviewer
```

If that output reports `unverified — no enumeration declared` for any item, or
any `[FAIL] undeclared` / `search-broken` row, follow **How this file gets
populated** in `.claude/templates/review-enumerations.md`. Propose the rows, show
them, and write `docs/review/enumerations.md` only on the developer's approval —
the first run's search is improvised by definition, and this is the moment it
becomes the permanent definition of the item's scope. Never write the file
silently.


`--checklist` counts the agent's attacker categories and requires one `## Coverage`
row per category. An attack class the review never considered produces no false
claim — just silence, which reads like a class that held. This is the only check
that catches it.

Print its output **above** the findings. A `mismatch`, `failed`, or `uncited`
line means the control cited for that category could not be re-derived — treat
the category as unreviewed and say so when presenting. A cleared category carries
more weight here than in any other review: it asserts an attacker was modelled
and the app held. `unverified` and `scanner unavailable` are the honest outcomes
and need no action.

### Step 5 — Present

Read `.claude/state/security-review/$REVIEW_ID/findings.md` and print it verbatim.

- Verdict `exploitable` → strongly recommend `/agentic-update-plan <run_id>` to
  add mitigations **before** building or shipping. Do NOT auto-gate the build.
- Verdict `hardening-needed` → summarize findings; developer folds mitigations in.
- Verdict `no-blockers` → one-line confirmation, noting any `scanner unavailable`
  gaps so the clean result is understood as partial where tools were missing.

## Constraints

- Do NOT fix anything or edit the plan. Findings are advisory — remediation is
  `/agentic-remediate <review_id>` for findings that are code changes, or
  `/agentic-update-plan <run_id>` when the fix belongs in an unbuilt plan.
- Do NOT call any MCP tool yourself. Do NOT commit.
- The report lives under `.claude/state/` (gitignored) on purpose — exploit
  scenarios should not land in repo history. Do not copy findings into tracked files.
