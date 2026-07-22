---
description: Critique the built UI against docs/design/DESIGN-SPEC.md — flag drift and internal inconsistencies (raw values that should be tokens, components styled two ways, missing focus/disabled states, contrast below the a11y floor). Reports findings; changes nothing.
argument-hint: "[run_id:<FEAT-###> | dir:<path> | all]"
---

# /agentic-review-ui

UI design-adherence review by the **design-critic** agent. Read-only — it produces a
findings report and never edits code or the spec. The system-design sibling is
`/agentic-review-design` (data/tenancy/compliance); this one is purely visual.

## Procedure

### Step 1 — Hard gate: the spec must exist

If `docs/design/DESIGN-SPEC.md` is absent, stop immediately:
```
No design spec found. Run /agentic-design first to author docs/design/DESIGN-SPEC.md,
then re-run /agentic-review-ui.
```
There is nothing to critique against — do NOT invent a standard.

### Step 2 — Allocate a review id

```bash
REVIEW_ID=UIR-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/ui-review/$REVIEW_ID
```

### Step 3 — Resolve scope (YOU do this — do NOT delegate)

Parse `$ARGUMENTS`:
- `run_id:<FEAT-###>` → the files touched by that run (from its task `## Design` sections)
- `dir:<path>` → components/styles under that path
- `all` → the whole UI surface
- empty → ask the developer **one `AskUserQuestion`** round (recent run / a
  directory / whole UI) — default `all`.

Build a one-line human description of the scope for the critic.

### Step 4 — Arm the critic and dispatch

```bash
printf '%s' 'design-critic' > .claude/state/current_role.txt
```

Invoke the **design-critic** agent with `review_id=$REVIEW_ID` and the resolved
`scope`. Use model `models.design-critic` from `.agentic.yml` (fall back to
`models.default`, then session default). Wait for it to write
`.claude/state/ui-review/$REVIEW_ID/findings.md` and print its summary. Then clear
the role (mandatory):

```bash
rm -f .claude/state/current_role.txt
```

### Step 5 — Present

Read `.claude/state/ui-review/$REVIEW_ID/findings.md` and print it.

- Verdict `off-spec` → recommend `/agentic-review` (to fold the fixes into a
  remediation run) or targeted `/agentic-plan` tasks. A wholesale token/a11y breach
  is worth fixing before more UI is built.
- Verdict `drift` → summarize High/Medium findings; the developer picks what to fix.
- Verdict `on-spec` → one-line confirmation.

If the critic reported "no design spec" despite Step 1 (spec unreadable/malformed),
say so and point back to `/agentic-design`.

## Constraints

- Do NOT fix anything or edit the spec. Findings are advisory — remediation is
  `/agentic-review` or `/agentic-plan`.
- Do NOT call any MCP tool yourself. Do NOT commit.
- Always clear `.claude/state/current_role.txt` after the critic returns.
