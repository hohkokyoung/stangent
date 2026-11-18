# Does the truth map judge tickets right? D375 as adapted by D406, run 2026-11-18

Plan: `benchmark/tickets/kavita-fsd-rules/plan.md` (frozen 2026-11-17, adapted before the planter ran, D406).
Truth map: Kavita, D404's live review, 117 entries; matches ruled by Claude blind (D405: 23 confirmed, 3 no), the rest by
the blind Opus planter (89 rulings, 3 against the FSD key, 2 left blank; clone commit `b546314`); KAVR-3's reply ruling
committed as Planter PM (`b47a67d`). Tickets KAVR-1..9 replayed; Sonnet 5.5 medium for the finder, reproducer and
question writer. Scored by `score.py` against `sealed/key.json` (`score-as-is.json`).

## Result (as built): the bar says **doesn't**

| | got | bar |
|---|---|---|
| verdict right | **5 of 9** (5 of the 8 whose entry reached the bundle) | 8 of 9, or every one with the entry |
| contradicted or unclear tickets with a test run | **0 of 4** checked (and both reply tickets' first tries: none); `fix start` refused all | 0 |
| backed tickets reproduced | **2 of 3** | 2 of 3 |
| replies leading to the right verdict | **1 of 2** | 2 of 2 |
| finder misses | 1 (KAVR-8, a backed ticket: shown as unclear "not covered", no test) | reported apart |
| cost | $0.88 in proofs; $1.36 in all with finders and questions | $20 cap |

## Why: one cause decides it

Three of the four wrong verdicts (KAVR-5, KAVR-7, KAVR-9) and the failed reply (KAVR-7) have **no verdict at all**:
`ticket prove` checks "can a test be written here" (`BugProof.cs`, `Targets`) **before** asking whether the rulings back
or contradict the ticket. These tickets' files have no unit-test project and `tickets.app` isn't set for Playwright, so
the proof stopped as `not-provable` with the reason "no unit-test project covers the code" and no AI call. Their entries
were in the bundle and usable. With no verdict, the questions draft asked the PM nothing, so KAVR-7's reply loop
couldn't start. Every ticket the verdict step reached was judged right (5 of 5, including KAVR-3 after its reply).

What held: no wrong ticket got a test, and `fix start` refused each one. Matches were used: KAVR-1 was contradicted
on its entry; KAVR-5 rested on a not-a-match entry and never reached the verdict.

## Notes
- The session running this printed the drafted ruling of KAVR-3's reply while checking the pull (the reply's own
  words, which the key also holds). Scoring is by code against the sealed key, so no number changes.
- `ticket-run.sh` line 273 had a `case` inside `$(…)` that macOS bash 3.2 can't parse: only the usage stamp's mode
  was lost. Fixed (`(prove|points)`).

## Second result, after D408 (verdict before the test check): the bar says **works**

Only `ticket prove`'s order changed (D408, fresh-checked, no bugs); the same truth map, tickets and key. Re-run:
KAVR-5, 7, 9 proved again, then KAVR-7's reply loop (question drafted, `reply.py`, pull drafted the ruling, committed as
Planter PM `9989939`, proved again). Scored `score-verdict-first.json`.

| | first (as built) | after D408 | bar |
|---|---|---|---|
| verdict right | 5 of 9 | **8 of 9** (8 of the 8 whose entry reached the bundle) | 8 of 9, or every one with the entry |
| wrong tickets with a test | 0 | **0** (`fix start` refused all 4) | 0 |
| backed reproduced | 2 of 3 | **2 of 3** | 2 of 3 |
| replies right | 1 of 2 | **2 of 2** (KAVR-7: entry unusable before, usable after) | 2 of 2 |
| finder misses | 1 | 1 (KAVR-8: a backed ticket shown as "not covered", no test) | apart |

The one miss is the file finder's, not the verdict's. The not-a-match ticket (KAVR-5) was judged unclear on its entry,
as the rulings say. Re-run cost $0.29; the whole measurement $1.65.

Caution: D408 was built after seeing the first result, aimed at the failure it showed, so the second result is not
blind to it; and it is 9 tickets on one repo. The first result stands beside it.
