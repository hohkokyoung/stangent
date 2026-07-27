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

## Coverage
<!-- EXACTLY one row per enforcement-checklist item in the spec, in spec order,
     whether or not anything was found. Run each search first, then write the
     findings from what came back — findings that come from noticing rather than
     from searching are why two runs over the same code report different subsets.
     An item you could not check is `unverified — <why>`, which is a real result.
     Never omit a row: the row count is verified against the spec, and an omitted
     row is the one outcome that reads as "fine" while meaning "never looked". -->
| # | checklist item | search | inspected | result |
|---|----------------|--------|-----------|--------|
| 1 | <item, abbreviated> | `<single command>` -> <N> matches | <k> of <N> | <U01, U04> or none |
| 2 | <item> | — | — | unverified — <why> |

<!-- `inspected` = how many of the search's hits you actually opened and judged.
     A search returning 38 and a reading of 12 must not look like a sweep of 38.
     The honest number is never penalised; a concealed gap is a false clear one
     level down. -->

## Findings
<!-- One per deviation from the spec. Order by severity. Only cite rules the spec
     actually states — never invent a standard the spec doesn't declare.

     TAG THE SPEC SECTION BY NUMBER ALONE: [§9], or [§9, §3] when a finding
     breaks two. No title after the number.

     The number is the finding's only identity across runs, and a title attached
     to it drifts immediately: one project's ten UI reviews wrote the same three
     sections as [§2], [§2 Principles], and [§2 Design principles], and [§5],
     [§5 Spacing], [§5 Radius] — so no one could group two reports and ask "is §9
     closed yet?" without reading both by hand. That question is the whole point
     of reviewing twice. The section numbers are fixed by DESIGN-SPEC.md's own
     headings (1-13) and are stable; the words after them are not. -->

### U01 — [HIGH] [§9] <short title>
**Where:** <file:line or component / screen>
**Spec rule:** <the exact line from DESIGN-SPEC.md or tokens.md it breaks>
**Observed:** <what the code/screenshot actually does>
**Suggested fix:** <the smallest change that brings it on-spec>

### U02 — [MEDIUM] [§5] ...

## Consistency scan
<!-- Cross-cutting drift the per-element findings miss: the same component styled
     two different ways in two places, ad-hoc values that should be tokens, a
     colour used outside its declared role. This is the "flag inconsistencies"
     lane — where the built UI is internally inconsistent, not just off-spec. -->
- ...

## Sections cleared
<!-- List each enforcement-checklist item (spec §13) that passed — never silently
     omit one. Each entry cites the concrete observation that cleared it (a
     file:line, a computed value, a command and its result); "no issues detected"
     is not evidence. Scope each claim to what you actually searched, and mark an
     item you could not check as `unverified — <why>` rather than clearing it.
     This section is read as "these were checked and are fine", so an entry you
     cannot back is worse than leaving it out. -->
- **<item>** — cleared by: `<single read-only command>` -> <N> matches
- **<item>** — cleared by: <path>:<line> "<exact snippet from that line>"
- **<item>** — unverified — <why>

## Severity guide
<!-- High   — a11y floor breached, tokens ignored wholesale, or an unusable state
              (no focus on an interactive element).
     Medium — real, checkable drift with a clear fix (raw hex, off-scale spacing).
     Low    — polish / future-proofing. -->
