# UI Design Critique — <review_id>
Date: <ISO 8601>
Reviewing: <scope description — run_id, dir, or "the built UI">
Spec: docs/design/DESIGN-SPEC.md (authored <date>)
Visual check: <playwright screenshots | static-only — no screenshot MCP available>

## Verdict
`on-spec` | `drift` | `off-spec`
<!-- on-spec = no findings above Low.
     drift   = the design language is intact but individual elements diverge.
     off-spec = a load-bearing rule (tokens ignored wholesale, a11y floor breached) is broken. -->

## Findings
<!-- One per deviation from the spec. Order by severity. Tag the spec section it
     violates, e.g. [§3 Color] or [§9 A11y]. Only cite rules the spec actually
     states — never invent a standard the spec doesn't declare. -->

### U01 — [HIGH] [§9 A11y] <short title>
**Where:** <file:line or component / screen>
**Spec rule:** <the exact line from DESIGN-SPEC.md or tokens.md it breaks>
**Observed:** <what the code/screenshot actually does>
**Suggested fix:** <the smallest change that brings it on-spec>

### U02 — [MEDIUM] [§5 Spacing] ...

## Consistency scan
<!-- Cross-cutting drift the per-element findings miss: the same component styled
     two different ways in two places, ad-hoc values that should be tokens, a
     colour used outside its declared role. This is the "flag inconsistencies"
     lane — where the built UI is internally inconsistent, not just off-spec. -->
- ...

## Sections cleared
<!-- List each enforcement-checklist item (spec §13) that passed — never silently
     omit one, so the developer sees the whole surface was checked. -->
- ...

## Severity guide
<!-- High   — a11y floor breached, tokens ignored wholesale, or an unusable state
              (no focus on an interactive element).
     Medium — real, checkable drift with a clear fix (raw hex, off-scale spacing).
     Low    — polish / future-proofing. -->
