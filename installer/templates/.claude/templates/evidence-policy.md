# Evidence policy

**Read this before writing any report that clears, covers, or enumerates.**
Every reviewing role follows it; the mechanics below are identical whether you
are checking a design spec, an attacker checklist, or a task's acceptance
criteria. Your role prompt adds only what is specific to *your* checklist.

## Why any of this exists

The most damaging thing a review produces is not a missed finding. It is a
checklist item reported as **cleared**. A miss leaves the reader still looking; a
clear tells them the item was checked and stops them.

An item you could not check is not a failure. An item you *claimed* to check and
did not is the one that ships defects.

## Two citation forms

Everything you clear, and every finding's site list, cites evidence in one of
these. Freehand prose does not count — these are re-run after you finish.

```
- **<item>** — cleared by: `<single read-only command>` -> <N> matches
- **<item>** — cleared by: <path>:<line> "<exact snippet from that line>"
**Where:** enumerated by: `<command>` -> <N> sites
```

**Commands.** May pipe between read-only tools (`grep … | grep -v …`,
`… | wc -l`). Must not chain with `;` or `&&`, redirect, or substitute — a
chained command whose first stage fails silently produces a confident wrong
count, which is exactly the failure that motivated this policy. State the count
you actually saw; an approximate one is caught by the re-run.

**The quotes mean verbatim.** The ref form takes the exact characters on that
line, not a description of them. `path:37-46 (disabled drops the gradient)` is
prose about the code and cannot be checked against anything;
`path:37-46 "gradient: _disabled ? null : buttonGradient"` can. You may elide
with `...` between two verbatim anchors when a comment sits between them.

## The Coverage table

One row per checklist item, in order, **always** — whether or not the item
produced a finding. Work the table top-down: run each item's check first, then
write findings from what came back.

| # | item | what you checked | inspected | result |
|---|------|------------------|-----------|--------|
| 1 | <item> | `<command>` -> <N> matches | 38 of 38 | <F01> |
| 2 | <item> | — | — | unverified — <why> |

Findings that come from noticing rather than from working the table are why two
runs over identical code report different subsets: the items you happen to look
at vary, and the ones you skip leave no trace.

**`inspected` is what you actually opened, over what the search returned.** These
come apart. A search returning 38 sites with 12 read produces a row shaped
exactly like a full sweep. Write `12 of 38`.

**Never omit a row.** An item with no row is indistinguishable from one that
passed — it is the only outcome that reads as "fine" while meaning "never
looked", and the row count is verified against the checklist.

## The search is declared, not invented

The "what you checked" column decides which code the item was checked *against*.
Improvise it and each run enumerates a different population, so findings are not
comparable between runs and an item can never be shown closed.

This is not hypothetical. Three reviews of one project checked the same item —
*"all spacing/radius picks come from the token scale"* — with three searches of
their own devising:

```
BorderRadius.circular(        -> 189 sites   every radius site
BorderRadius.circular([0-9]   ->  24 sites   raw literals only
AppRadius\.                   -> 203 sites   token uses
```

None was wrong. They answer different questions, and no one had decided which
question the item asks — so the numbers were not comparable, and "is this closed?"
had no defined answer. That is the failure: not a bad search, an undeclared one.

So:

- **Use the item's declared search verbatim.** For a checklist that lives in a
  project file, the declaration sits with the item. Otherwise it is in
  `docs/review/enumerations.md`, keyed by reviewer and item number.
- **Never substitute your own** because it looks better scoped, faster, or more
  precise. A better search that differs is still a different population, and the
  comparison across runs is worth more than the improvement.
- **If an item has no declared search**, do not invent one silently. Report
  `unverified — no enumeration declared`, and propose a command in the row so it
  can be added. One honest run beats a confident wrong denominator.
- **If the declared search is wrong**, say so in the row and still run it. Fixing
  it is a change to the declaration, not something to route around mid-review.

`N` in `inspected: k of N` is the declared search's count. That is what makes
`k of N` mean the same thing twice.

**Scope narrower than the whole repo.** Run the declared command **verbatim** and
filter its results to your scope — never edit the command to add the scope. The
citation stays comparable, and the row reports the in-scope subset:
`6 of 24 (scope: features/auth)`.

## Shape decides whether a rule can ever close

Each declaration carries a `kind`, and it is the difference between a rule that
finishes and one that cannot.

**`violations`** — the search matches only what is wrong: `circular([0-9]`,
`0xFF`, `FontWeight.w900`. The population **is** the remaining work. Fix a site
and it leaves the set permanently; it cannot reappear next run. The count drains
24 → 14 → 4 → 0, and zero means closed.

**`candidates`** — the search matches everything that must be judged, compliant or
not: `circular(` returning all 189 radius sites, or `rawQuery` returning every
database call because grep cannot tell a parameterised query from a concatenated
one. A site judged compliant **stays in the population forever**, so the count
never drains and **`candidates` items can never be reported closed by count**.
They report `k of N` honestly and nothing more.

That difference is why reviews stop converging. With a `candidates` search there
is always unexamined material left however diligent each run is, so every pass
turns up findings in code nobody touched — not regressions, just the part of the
set this run happened to reach.

So: **prefer `violations`.** Search for what is wrong, not for everything that
might be. Where no violating pattern is expressible — *"every interactive element
renders a focus ring"*, *"contrast meets the floor"* — do not invent a
`candidates` search to look thorough. Those are `unverified`, and an honest gap
beats a denominator that cannot shrink.

A `violations` search that reaches zero is the goal. A `candidates` search that
reaches zero is a **broken search** — the path moved or the pattern stopped
matching — and must be reported as such, never as a completed rule.

**Only for checklists that enumerate sites.** Items phrased as questions about a
design — *who owns this entity, what breaks at 100×, can one tenant reach
another's data* — have no population to search. Answer those from the design
itself; nothing here applies to them.

## Honesty is never penalised

Load-bearing, not encouragement:

- `unverified — <why>` **counts as coverage**. It never fails a run.
- A partial `inspected` count is reported as a note, never a failure.
- An *uncited* clear does fail. So does a citation that no longer reproduces.

The asymmetry is deliberate. If admitting a gap cost you anything, the cheap move
would be to inflate the number or invent a clear — which is the behaviour this
whole policy exists to remove. The cheap move must always be the honest one.

## Two ways to clear something that is not true

- **Never clear a rule from the presence of its correct usage.** Finding that the
  canonical component uses the safe value does not clear "the unsafe value is
  never used" — that requires searching for the *violating* pattern.
- **Never narrow the rule you are clearing.** Verify the claim as written.
  Quietly adding a qualifier — clearing "never under a white label" by checking
  only "never under *a filled button's* label" — reads as having checked the rule
  while checking something weaker. If you can only verify the narrower claim,
  clear that one by name and mark the rest `unverified`.

## Your citations are re-run

`verify_clears.py` re-runs every command and re-reads every `path:line` you
cite, and a `SubagentStop` hook does it again where nobody can skip it. A count
that has moved, a command that errors, or a snippet that is not there is
reported against your report.

So cite what you actually ran, with the number you actually saw. The checker
knows nothing about the project or what any rule means — it only checks whether
your claim still holds.
