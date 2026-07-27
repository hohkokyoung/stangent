# Review enumerations — <project>

The declared search behind each site-based checklist item. Reviews enumerate from
these and nowhere else, so two runs measure the same population and an item is
closed when its count reaches zero — not when a run happens to report nothing.

Committed on purpose. It is a project decision which code an item is checked
against, it changes when the codebase is restructured, and it must not live in
gitignored state where a fresh clone loses it.

**Editing.** Add or correct a row whenever a review says
`unverified — no enumeration declared`, or when a declared search stops matching
how the code is laid out. Changing a search resets comparability for that item —
the counts before and after are not the same measurement, so note it in the row.

**Commands must be read-only.** They are re-run by `verify_clears.py`, which
allows the inspection tools (`grep`, `rg`, `find`, `wc`, `sed`, `awk`, …) and
pipes between them, and refuses sequencing (`;`, `&&`), redirection and
substitution. A chained command whose first stage fails silently produces a
confident wrong count — the exact failure this file exists to prevent.

**Not every checklist belongs here.** Only items that enumerate sites. Items
phrased as questions about a design — data ownership, blast radius, what breaks
at 100× — have no population to search and are answered from the design itself.
`architect` therefore has no section here.

---

## design-critic

Items are `docs/design/DESIGN-SPEC.md` §13, in order.

| # | checklist item | enumerate |
|---|----------------|-----------|
| 1 | <item, abbreviated> | `<read-only command>` |
| 2 | <item> | `<read-only command>` |

## security-reviewer

The eight attacker categories in `.claude/agents/security-reviewer.md`, in order.
A category's search finds *candidate sites to judge*, not confirmed defects —
`grep` cannot tell a parameterised query from a concatenated one, so the count is
the denominator and the judging is still yours.

| # | category | enumerate |
|---|----------|-----------|
| 1 | Broken access control (authz) | `<read-only command>` |
| 2 | Injection | `<read-only command>` |
| 3 | Authentication & session | `<read-only command>` |
| 4 | SSRF / CSRF / CORS | `<read-only command>` |
| 5 | Secrets & logging | `<read-only command>` |
| 6 | Input trust | `<read-only command>` |
| 7 | Rate-limiting / abuse / DoS | `<read-only command>` |
| 8 | Supply chain | `<read-only command>` |

## auditor

Items are the smell classes in `.claude/agents/auditor.md`, in order.

| # | item | enumerate |
|---|------|-----------|
| 1 | <item> | `<read-only command>` |
