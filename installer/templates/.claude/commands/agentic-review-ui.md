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

Resolve the critic's model first: `models.design-critic` from `.agentic.yml`
(fall back to `models.default`, then the session default). Then arm the hook —
`current_model.txt` is what puts the model on every logged tool call, so a run
whose cost looks wrong can be traced to the model that actually ran:

```bash
printf '%s' "$REVIEW_ID" > .claude/state/current_run.txt   # log context: tool calls land in logs/$REVIEW_ID.jsonl
printf '%s' 'design-critic' > .claude/state/current_role.txt
printf '%s' '<resolved_model>' > .claude/state/current_model.txt
```

Invoke the **design-critic** agent with `review_id=$REVIEW_ID`, the resolved
`scope`, and that model — pass it explicitly at invocation, do not rely on the
agent inheriting the session model. Wait for it to write
`.claude/state/ui-review/$REVIEW_ID/findings.md` and print its summary. Then clear
the state (mandatory):

```bash
rm -f .claude/state/current_role.txt .claude/state/current_run.txt .claude/state/current_model.txt
```

### Step 5 — Verify the critic's clears

Re-run the evidence the critic cited for everything it marked cleared:

```bash
${PYEXE:-python3} .claude/hooks/lib/verify_clears.py \
  .claude/state/ui-review/$REVIEW_ID/findings.md --cwd . \
  --checklist docs/design/DESIGN-SPEC.md
```

`--checklist` counts the spec's `- [ ]` enforcement items and requires one
`## Coverage` row for each. This is the only check that catches a rule the review
never examined: an unexamined rule produces no false claim, just absence, which
reads identically to a rule that passed.

Print its output **above** the findings. A `mismatch`, `failed`, or `uncited`
line means that item was not actually verified, whatever the report says — the
count has moved, the command errors, or no re-runnable evidence was given. Treat
those items as unreviewed and say so plainly when presenting.

Do not edit the findings file to fix them, and do not re-dispatch the critic
automatically — a critic that cannot reproduce its own clears is a signal about
the review, and hiding it defeats the check. `unverified` entries are the honest
outcome and need no action.

Then check that every finding tags its spec section in the canonical `[§N]` form,
so this report can be grouped with earlier ones:

```bash
grep -nE '^###+[[:space:]]+U[0-9]+' .claude/state/ui-review/$REVIEW_ID/findings.md \
  | grep -vE '\[§[0-9]+([[:space:]]*,[[:space:]]*§[0-9]+)*\]' || true
```

Any line printed is a finding whose section tag will not group across runs — a
titled tag (`[§2 Principles]`) or a missing one. Print them as a note; do not
edit the file. The point of a second review is to ask whether a class of finding
ever closed, and free-form tags make that a manual read of both reports: one
project's ten reviews wrote the same section as `[§2]`, `[§2 Principles]`, and
`[§2 Design principles]`.

### Step 6 — Present

Read `.claude/state/ui-review/$REVIEW_ID/findings.md` and print it.

- Verdict `off-spec` → recommend `/agentic-remediate $REVIEW_ID`, which turns
  *these* findings into fix tasks. Do not recommend `/agentic-review` for this —
  it re-runs all four analysis lanes to rediscover what this report already
  contains, at several times the cost. A wholesale token/a11y breach is worth
  fixing before more UI is built.
- Verdict `drift` → summarize High/Medium findings; the developer picks what to fix.
- Verdict `on-spec` → one-line confirmation.

If the critic reported "no design spec" despite Step 1 (spec unreadable/malformed),
say so and point back to `/agentic-design`.

## Constraints

- Do NOT fix anything or edit the spec. Findings are advisory — remediation is
  `/agentic-remediate <review_id>`.
- Do NOT call any MCP tool yourself. Do NOT commit.
- Always clear `.claude/state/current_role.txt`, `.claude/state/current_run.txt`,
  and `.claude/state/current_model.txt` after the critic returns.
