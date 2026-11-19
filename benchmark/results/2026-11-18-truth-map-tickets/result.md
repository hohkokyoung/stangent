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

## Third result, after D410 (rules reach a ticket through its flow or screen), 2026-11-19

Only the bundle's matching changed (D410, fresh-checked); same truth map, tickets and key. Bundles rebuilt for all nine
(free): the key's entry is in the bundle for **9 of 9** (was 8), finder misses **0**. KAVR-8 proved again ($0.38, two
calls); the others not re-proved (their bundles gained nothing that bears on their entry, but for KAVR-4's one extra
entry). Scored `score-d410.json`.

| | after D408 | after D410 |
|---|---|---|
| verdict right | 8 of 9 | **8 of 9** |
| key's entry in the bundle | 8 of 9 | **9 of 9** |
| finder misses | 1 | **0** |
| wrong tickets with a test | 0 | 0 |

KAVR-8 now reaches its entry (128, by the Browse Genres screen) and is still judged **unclear**: the planter ruled that
undocumented entry "keep" and gave PM words that say what the ticket expects; the reproducer read "keep" as "the code
is right as it is", saw the words point the other way, and wrote no test. A safe miss (it asks the PM), but not the
key's "backed". The cause is how a "keep" ruling with PM words is read, not the matching. Whole measurement now $2.03.

## Fourth result, after D411 (a "keep" ruling with PM words keeps the feature working as the words say), 2026-11-19

Only the reading of a "keep" entry with PM words changed (the reproducer's input, and the code check that refused a
backed verdict on it; narrowed after the run by its fresh check, and KAVR-8's verdict still passes the narrowed check); same truth map, tickets and key. KAVR-8 proved again ($0.25: the first answer said backed on
entry 128 but its test failed on another assertion; the retry's test failed on the named one). Scored `score-d411.json`.

| | after D408 | after D410 | after D411 | bar |
|---|---|---|---|---|
| verdict right | 8 of 9 | 8 of 9 | **9 of 9** | 8 of 9 |
| backed reproduced | 2 of 3 | 2 of 3 | **3 of 3** | 2 of 3 |
| wrong tickets with a test | 0 | 0 | **0** | 0 |
| replies right | 2 of 2 | 2 of 2 | **2 of 2** | 2 of 2 |
| finder misses | 1 | 0 | **0** | apart |

Caution: D410 and D411 were built after seeing KAVR-8 fail and aimed at it; only KAVR-8 was proved again under them,
so the other eight verdicts date from D408's run. Nine tickets, one repo, one planter. Whole measurement $2.28.

## Fifth result, the other eight re-proved under D410 and D411, 2026-11-19

Owner: "lets do the reprove now", to check that D410 and D411, both built after seeing KAVR-8 fail, didn't change the
other verdicts. KAVR-1..7 and KAVR-9 proved again (`ticket-run.sh --no-pull --prove`, Sonnet 5.5 medium), same truth
map, tickets and key; stangent at `35ad3b0`. Scored `score-reprove-all.json`.

| | after D411 (KAVR-8 only) | all nine under D410 and D411 | bar |
|---|---|---|---|
| verdict right | 9 of 9 | **9 of 9** (every verdict the same as before) | 8 of 9 |
| backed reproduced | 3 of 3 | **3 of 3** (see below) | 2 of 3 |
| wrong tickets with a test | 0 | **0** | 0 |
| replies right | 2 of 2 | **2 of 2** | 2 of 2 |
| finder misses | 0 | **0** | apart |

**Reproductions aren't fresh:** the clone still held the tests earlier runs wrote (left uncommitted, as before), and
KAVR-2, KAVR-3 and KAVR-6 each named one of them; the files are byte-identical to a backup taken before the run. So the
verdicts are fresh, and those reproductions only show the earlier tests still fail. A clean re-run would remove them first.
Cost $0.62 (eight proofs); whole measurement $2.90.

