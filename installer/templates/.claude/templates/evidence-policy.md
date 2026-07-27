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
