---
description: Author (or amend) the project's UI design specification. Auto-detects greenfield (interview + recommend a stack) vs brownfield (extract the existing design + flag inconsistencies). Promotes an approved spec to committed docs/design/.
argument-hint: "[\"<optional direction, e.g. 'calm, editorial, dark'>\"]"
---

# /agentic-design

Produce the durable house style under `docs/design/` — the spec the **design-critic**
enforces (`/agentic-review-ui`) and the **sketcher** honours when drawing mockups.

The **designer** agent drafts to gitignored state; THIS command runs the interview,
gets your approval, and promotes the draft to the committed `docs/design/`. (Agents
can't prompt you and can't write to `docs/` — that split is deliberate.)

## Procedure

### Step 1 — Allocate a spec id

```bash
SPEC_ID=DS-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/design-spec/$SPEC_ID
```

### Step 2 — Detect mode, then confirm (YOU do this — do NOT delegate)

Decide **brownfield** if any hold, else **greenfield**:
- `.claude/.agentic.yml: enabled_skills` intersects `{react, html-css, flutter, mobile}`
- `.claude/state/project.yml: test_framework` is `playwright` or `maestro`
- frontend source exists: `*.tsx`/`*.jsx`/`*.vue`/`tailwind.config.*`/`*.css` under
  the repo, or `pubspec.yaml` (Flutter). Use Glob.

Also: if `docs/design/DESIGN-SPEC.md` already exists, this is an **amendment** — say
so and treat it as brownfield (re-extract against current code + the existing spec).

Confirm with **one `AskUserQuestion`** round (the detection is a guess, not a verdict):
1. Mode — "Detected **<mode>**. Author the spec this way?" Options: the detected
   mode (recommended), the other mode, cancel.

If the developer cancels: `rmdir .claude/state/design-spec/$SPEC_ID` (it's still
empty here) and stop. (Don't use `rm -rf` — the safety hook blocks it.)

### Step 3a — Greenfield: interview + recommend (YOU do this)

Walk this checklist. Batch related questions into `AskUserQuestion` rounds (≤3 per
round, ≤3 rounds). Always give a suggested answer so the developer can confirm fast.
Skip a dimension only if `$ARGUMENTS` already answers it.

| Dimension | Confirm |
|---|---|
| **Vibe** | 3 adjectives for how it should feel (calm/precise, bold/energetic, playful, editorial, …) + one reference product if any |
| **Colour direction** | light / dark / both; a base hue or "neutral"; accent temperature |
| **Typography** | modern-sans / geometric / serif-display / mono-accent; one face or a display+body pair |
| **Density** | airy vs compact; touch-first or pointer-first |
| **Motion appetite** | none / subtle-feedback / expressive / immersive-3D — this drives the stack recommendation |
| **Platforms** | web desktop, web mobile, native mobile; primary breakpoint |
| **Accessibility bar** | WCAG AA (default) or stricter; reduced-motion required (default yes) |
| **Framework** | confirm from `enabled_skills`, or ask (React / vanilla / Flutter) |

Fold the answers into a `## Brief` block (one `- Q → A` line each). Then, from the
motion appetite + framework, note 1–2 stack directions you'll ask the designer to
detail (e.g. "React + subtle-feedback → Framer Motion; shadcn/ui + Tailwind tokens").
Do NOT install anything.

### Step 3b — Brownfield: no interview

Nothing to collect up front — the designer extracts from code. (If `$ARGUMENTS`
carries a direction, pass it as a one-line `## Brief` so the designer knows which
inconsistencies matter most.)

### Step 4 — Arm the designer and dispatch

```bash
printf '%s' 'designer' > .claude/state/current_role.txt
```

Invoke the **designer** agent with: `spec_id=$SPEC_ID`, `mode`, the `## Brief`
block (if any), and the template paths. Use model `models.designer` from
`.agentic.yml` (fall back to `models.default`, then session default). Wait for it to
write the draft under `.claude/state/design-spec/$SPEC_ID/` and print its summary.
Then clear the role (mandatory):

```bash
rm -f .claude/state/current_role.txt
```

### Step 5 — Present the draft

Read the draft and show the developer:
- The filled `DESIGN-SPEC.md` (or a tight section-by-section summary if long).
- `tokens.md` — the concrete palette / scales.
- `plugins.md` — the recommended stack, each with its install pointer.
- **Brownfield:** print `drift.md` in full — this is the critique of the existing
  design. Group by severity.

Then **one `AskUserQuestion`** round:
1. Approve & commit the spec to `docs/design/`? Options: approve; revise (collect
   what to change, then re-run the designer from Step 4 with the notes appended to
   the brief); discard.

### Step 6 — Promote (only on approval — YOU do this, no role armed)

Copy the approved draft to the committed location:
```bash
mkdir -p docs/design
cp .claude/state/design-spec/$SPEC_ID/DESIGN-SPEC.md docs/design/DESIGN-SPEC.md
cp .claude/state/design-spec/$SPEC_ID/tokens.md      docs/design/tokens.md
cp .claude/state/design-spec/$SPEC_ID/plugins.md     docs/design/plugins.md
```
Do NOT copy `drift.md` — it's a point-in-time critique of the OLD code, not part of
the durable spec. It stays in gitignored state; you've already shown it.

### Step 7 — Report

```
Design spec written — docs/design/
  DESIGN-SPEC.md   tokens.md   plugins.md
Mode: <greenfield | brownfield | amendment>

Recommended stack (not installed — install yourself):
  • <pick> — <install pointer>
  • ...

Next:
  • Install the stack above if you want it.
  • /agentic-review-ui all   → check the built UI against this spec
  • Future /agentic-plan sketches will honour this spec automatically.
  • Re-run /agentic-design to amend.
```

## Constraints

- Do NOT call any MCP tool yourself (the designer has its own). Exception: none.
- Do NOT install packages, edit project code, or touch `.mcp.json` — recommendations
  are documentation. The developer installs.
- Do NOT commit — writing `docs/design/` is the only file change; commits are
  user-driven.
- Always clear `.claude/state/current_role.txt` after the designer returns, even on
  a revise loop.
