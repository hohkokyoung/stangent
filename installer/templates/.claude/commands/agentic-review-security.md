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

```bash
printf '%s' 'security-reviewer' > .claude/state/current_role.txt
```

Invoke the **security-reviewer** agent with `review_id` and the resolved
`run_id` / `scope`. Wait for it to write
`.claude/state/security-review/<review_id>/findings.md` and print its summary.
Then clear the state (mandatory):

```bash
rm -f .claude/state/current_role.txt
```

### Step 4 — Present

Read `.claude/state/security-review/$REVIEW_ID/findings.md` and print it verbatim.

- Verdict `exploitable` → strongly recommend `/agentic-update-plan <run_id>` to
  add mitigations **before** building or shipping. Do NOT auto-gate the build.
- Verdict `hardening-needed` → summarize findings; developer folds mitigations in.
- Verdict `no-blockers` → one-line confirmation, noting any `scanner unavailable`
  gaps so the clean result is understood as partial where tools were missing.

## Constraints

- Do NOT fix anything or edit the plan. Findings are advisory.
- Do NOT call any MCP tool yourself. Do NOT commit.
- The report lives under `.claude/state/` (gitignored) on purpose — exploit
  scenarios should not land in repo history. Do not copy findings into tracked files.
