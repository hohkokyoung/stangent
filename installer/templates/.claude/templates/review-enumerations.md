# Review enumerations — <project>

The declared search behind each site-based checklist item. Reviews enumerate from
these and nowhere else, so two runs measure the same population.

Committed on purpose. It is a project decision which code an item is checked
against, it changes when the codebase is restructured, and it must not live in
gitignored state where a fresh clone loses it. Nothing in this file references
the tooling that produced it — it is a list of shell commands and numbers, so a
CI step, a pre-commit hook, or a person can act on it with no agent involved.

## Columns

| column | meaning |
|---|---|
| `#` | the checklist item's position, in the source's order |
| `item` | short name, for humans |
| `kind` | `violations` or `candidates` — see below |
| `enumerate` | a read-only command, run verbatim |
| `baseline` | the count when the row was last agreed |

**`kind` is the important one.**

- **`violations`** — matches only what is wrong (`circular([0-9]`, `0xFF`,
  `FontWeight.w900`). The count is the remaining work: fix a site and it leaves
  the set for good. Drains to zero, and **zero means closed**.
- **`candidates`** — matches everything that must be judged, compliant or not
  (`circular(` → all 189 radius sites; `rawQuery` → every database call, because
  grep cannot tell a parameterised query from a concatenated one). Compliant
  sites stay forever, so the count never drains and **the item can never be
  closed by count** — only reported as `k of N`.

Prefer `violations`. A `candidates` search guarantees every run finds unexamined
material, which is exactly the "new issues in code we didn't touch" problem.
Where no violating pattern exists — *"every interactive element renders a focus
ring"*, *"contrast meets the floor"* — leave the item undeclared and let the
review report `unverified`. A denominator that cannot shrink is worse than an
honest gap.

**`baseline` reads differently per kind.** For `violations`, above baseline is a
regression (new bad code landed) and below is progress. For `candidates` the
count should be roughly flat; a collapse toward zero means the search broke — a
moved path, a renamed idiom — and must never be read as the rule being finished.

## Editing

Add or correct a row whenever a review reports `unverified — no enumeration
declared`, or when a search stops matching how the code is laid out. Changing a
search resets comparability for that item: counts before and after are not the
same measurement, so reset `baseline` in the same edit.

**Commands must be read-only.** They are re-run by `verify_clears.py`, which
allows the inspection tools (`grep`, `rg`, `find`, `wc`, `sed`, `awk`, …) and
pipes between them, and refuses sequencing (`;`, `&&`), redirection and
substitution. A chained command whose first stage fails silently produces a
confident wrong count — the exact failure this file exists to prevent.

## How this file gets populated

Nothing needs to be written up front. A review that meets an undeclared item uses
its own search, marks the row `unverified — no enumeration declared`, and at the
end offers to add those rows here with the commands it actually used and their
current counts as `baseline`. Review the proposal before accepting: the first
run's search is improvised by definition, and this is the moment it becomes
permanent. Check the `kind` especially — a `candidates` search accepted by
habit is a rule that will never close.

Already-declared items are never modified by that flow. Add a checklist item next
month and only that one is proposed.

## Which reviews use this

Codebase-scoped reviews whose checklist enumerates sites: `design-critic`
(`/agentic-review-ui`), `security-reviewer` (`/agentic-review-security`), and
`auditor` (`/agentic-cleanup`).

Deliberately not:

- **`architect`** — its dimensions are questions about a design (*who owns this
  entity, what breaks at 100×, can one tenant reach another's data*). No
  population to search.
- **`/agentic-review-pr`** and the per-task `reviewer` — their population is a
  diff, already bounded and already stable. A repo-wide declaration would be the
  wrong scope.

---

## design-critic

Items are `docs/design/DESIGN-SPEC.md` §13, in order.

| # | item | kind | enumerate | baseline |
|---|------|------|-----------|----------|
| 1 | <item, abbreviated> | violations | `<read-only command>` | <n> |

## security-reviewer

The eight attacker categories in `.claude/agents/security-reviewer.md`, in order.
These are usually `candidates`: a search finds sites to judge, not confirmed
defects, so the count is a denominator and the judging is still yours.

| # | category | kind | enumerate | baseline |
|---|----------|------|-----------|----------|
| 1 | Broken access control (authz) | candidates | `<read-only command>` | <n> |
| 2 | Injection | candidates | `<read-only command>` | <n> |
| 3 | Authentication & session | candidates | `<read-only command>` | <n> |
| 4 | SSRF / CSRF / CORS | candidates | `<read-only command>` | <n> |
| 5 | Secrets & logging | candidates | `<read-only command>` | <n> |
| 6 | Input trust | candidates | `<read-only command>` | <n> |
| 7 | Rate-limiting / abuse / DoS | candidates | `<read-only command>` | <n> |
| 8 | Supply chain | candidates | `<read-only command>` | <n> |

## auditor

Items are the smell classes in `.claude/agents/auditor.md`, in order.

| # | item | kind | enumerate | baseline |
|---|------|------|-----------|----------|
| 1 | <item> | violations | `<read-only command>` | <n> |
