---
phase: 2c
name: spec
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 2, cli-conventions: 3, benchmark: 1, code-graph: 4, impact-report: 5, reference-page: 1, source-document: 3, trace-links: 4, ticket-bundle: 2, bug-proof: 1 }
provides: { fsd-claims: 1 }
---

# Phase 2c · The structured spec

**Grilled (D22) 2026-11-03; build order approved by the owner 2026-11-03; being built.** The design is D99, placed before Phase 4 by D109.
This file holds what is settled for the build; D99 keeps the reasons.

## What it adds
The FSD stays the record. On top of it: requirements with stable ids, each quoting the FSD word for word, linked to reference page
statements and through them to code. The BA approves each one. Then a ticket can be matched to an approved requirement, which gives
"bug, per R-0042" or "looks intended (R-0042)".

## Settled
- **Everything in D99:** the items, ids, files, several documents and `fsd reconcile`, Jira and as-built sources, BA approval by tick boxes.
- **Matching a ticket (owner, 2026-11-02):** candidates by code through links; an AI picks one or none, with quotes code checks; a fresh
  AI checks without seeing the pick; **a person always confirms.** Until then it reads "suggested: R-0042".
- **A requirement no statement covers (owner, 2026-11-02):** it can still match, after linked ones, labelled "no code found for this rule".
- **Scoring the match (owner, 2026-11-02):** a key written by a fresh agent and frozen before anything is built, on the planted FSD:
  tickets that clearly fit a requirement and tickets that clearly don't. It's our own test set, so a real FSD stays the judge (D56).
- **Bars for the match (owner, 2026-11-02):** on 30 tickets (15 with a requirement and code, 5 with a requirement and no code, 10 with none):
  at least 90% of suggestions right; at least 8 of 10 "none"; at least 14 of 20 found. If either of the first two fails, matching ships off;
  if only the third fails, it ships on and the report says so.
- **A fix against an approved requirement (owner, 2026-11-02):** the agent stops and asks; the rule changes through the BA first. The diff check is Phase 4's.
- **Recheck triggers (owner, 2026-11-02):** a stale page's links are judged again, here. The PR list of requirements with no test waits for Phases 5–6. None after a failed proof.
- **Phase 3 when this lands (owner, 2026-11-03):** `ticket match` as its own step; `ticket-bundle` v3; `bug-proof` v2 with the verdict; drafts may link a requirement.
- **Adoption (owner, 2026-11-03):** `/stangent:adopt` and a driver script (names not final) run the existing steps in order and resume; no separate audit (replaces D14's).

## Build order (owner approved, 2026-11-03)
The core comes first and is scored before the rest, since matching can ship off if it fails its bars.
0. A new branch from `main`. A fresh agent writes and freezes the matching key, with no view of how matching works.
1. **Extraction:** requirements from the planted FSD, with quotes checked by code, every sentence cited or `background`, and a fresh checker.
2. **Links:** requirement ↔ page statement, scored on the 30-item key (the bar below).
3. **Matching:** `ticket match`, scored on the frozen key.
4. **Approval:** tick boxes in a spec-only PR, listing each section's background sentences too; `decided_by` from git; the setting benchmarks use to skip it.
5. **Phase 3's follow-up** (D20): `ticket-bundle` v3, `bug-proof` v2, drafts; Phase 3 goes `needs review`, then its tests pass again.
6. **Staying current:** a stale page's links judged again; code carries items forward across imports, so ids and approvals hold; `fsd reconcile` and its CI run; one pass over a document for clashes between sections.
7. **Other sources:** Jira change requests; as-built drafts.
8. **`/stangent:adopt`,** with the driver script.

## Done when (from D99)
- [ ] Requirement-level links find at least 27 of the planted FSD's 30 items, with false alarms at or below 30% (2b: 27/30, 31.3%).
- [ ] Every item cites word-for-word quotes, checked by code; every FSD sentence is cited or marked `background`.
- [ ] A fresh AI checks each item says no more than its quotes.
- [ ] Matching passes its bars on the frozen key, or ships off as its rule says.
- [ ] A real-FSD run (D56), when a project with one is found; until then the report says it's owed.
- [ ] Benchmark runs skip BA approval only through a setting off by default, and every result made with it says so (D99).
- [ ] End-of-phase report, its claims checked by a fresh agent (D61).

## Contracts
Provides a new contract beside `source-document` (name not decided). Phase 3 goes `needs review` when it lands (D20).

### Needs review 2026-11-04 · `code-graph` v3 → v4 (overhaul 1.1, 1.2; D115)
v4 added `opens-screen` lines, kept apart from the walked lines, and `meta.dirtyFiles`. Follow-up: confirm nothing here depends on v3, re-run this phase's tests, then move `consumes` to `code-graph: 4`.
Checked 2026-11-04 (D20): by grep, `Stangent.Spec` reads none of the new values. All tests pass, so this now consumes `code-graph: 4`.

Checked 2026-11-04 (D20, D117): `source-document` v1 → v2. By grep, `Stangent.Spec`'s link and requirement runs read each section's `id`, `title`, `text` and `parent`, never `permanentId` or the document's `schemaVersion`. All tests pass, so this now consumes `source-document: 2`. On a promotion, `FsdCarry` moves this phase's section references to the new numbers; no file changes shape.

Checked 2026-11-04 (D20, D118): `trace-links` v1 → v3. By grep, requirement links are written through `LinkRun.Entry`, which didn't change, and read by field, with `requirement` and the id's hash as they were. `ApplyEntries` now also carries a finding's new fields across a re-run. All tests pass, so this consumes `trace-links: 3`.

Checked 2026-11-04 (D20, D119): `trace-links` v3 grew `page-wrong`, `verdict`, `disproof` and `disproof_hash` in place, because v3 hasn't reached `main`. Requirement links and matching read `state` and `check`, never these. `Match.IsLink` is unchanged, so a refuted gap still reads "no code found" until a person says otherwise. All tests pass.

## The disprove step's re-score: prepared, not run (2.5, D119)
**It isn't shown to help yet.** The owner OK'd the run on 2026-11-04. It stopped at 11 of 62 calls on Claude Code's weekly limit, so it covers 4 of 16 sections and 16 of 90 findings, about $1.29. `benchmark/results/2026-11-04-phase2c-disprove-partial-score.json` is that partial result: **no bar can be called on it.** So far: 5 stands, 3 refuted, 2 page-gap, 6 cant-tell; of the hand-checked 20, 3 of 12 false alarms left the BA's queue and 0 of 8 real ones did; no planted item lost. Re-running the same command picks up where it stopped.

**Before (2c, `benchmark/results/2026-11-03-phase2c-sonarr-links-score.json`):** 24 of 30 found, 44% false alarms (95% range 29-59%), from 90 findings and a hand check of 20 (12 false, 8 real).

**Bars, unchanged:** at least 27 of 30 found; false alarms at or below 30%.

**What to expect from the bars.** The step can only take findings out, so it **can't raise found above 24 and can't meet the 27 bar**. The most it can do is meet the noise bar without losing planted items. `plantedLost` names each one lost. Meeting 27 needs more found, which is the gap finders, not built. Also, 23 of the 90 are `undocumented` screens found by code, which the step can't test, so they stay in the BA's queue.

**Estimate, from the real bundles on a temp copy of the clone (`fsd disprove estimate sonarr-fsd`, no AI):**

| | |
|---|---|
| Findings | 90: 62 calls, 23 not testable, 5 with no code so no call |
| Characters sent | 4.50M in all, median 89K, largest 101K; 44 of 62 hit the 60K code limit and name what they left out |
| Input tokens | about 1.2M, at 3.5 to 4 characters a token |
| Output tokens | about 40K, at the saved judge and checker's 630 a call |
| Cost | **$4 to $6** on Sonnet 5.5, with a tenth for retries. The saved judge and checker cost $1.31 for 300K in and 20K out, so this scales from what's been paid, not from a price list |
| Time | not measured: no call of this size has run. The saved calls took 5 seconds at a third of the size |

Because the copy's graph is pre-1.1, the estimate marked it clean by hand in the copy. Its only dirty file was `global.json`. On the real clone, `graph build` does this properly, and the first command below says the numbers again.

**The command, for the owner (from this repo's root):**
```bash
B=$HOME/stangent-bench; REPO=$PWD
dotnet build src/Stangent.Cli && rm -rf $B/stangent-bin-disprove && cp -R src/Stangent.Cli/bin/Debug/net10.0 $B/stangent-bin-disprove
export STANGENT="dotnet $B/stangent-bin-disprove/stangent.dll"
(cd $B/sonarr && cp -R specs ../sonarr-specs-before-disprove && cp $REPO/benchmark/configs/sonarr.spec-2c-disprove.yaml stangent.yaml \
  && $STANGENT graph build && $STANGENT fsd disprove estimate sonarr-fsd)   # free: stop here if the numbers surprise you
(cd $B/sonarr && $REPO/plugin/scripts/spec-run.sh --jobs 4 --model sonnet --disprove sonarr-fsd)   # PAID: $4-6. Re-judges nothing
dotnet run --project benchmark/Stangent.Benchmark -- --score-disprove $B/sonarr benchmark/fsd/sonarr/answer-key.json \
  --hand-check benchmark/results/2026-11-03-phase2c-handcheck.json --out benchmark/results/<date>-phase2c-disprove-score.json
```
Stopped partway, it picks up where it was: an answered finding with an unchanged bundle makes no call. After it, a fresh agent should check what's still in the BA's queue by hand before a bar is called met, since `falseAlarmShare` here rests on the 20 sampled before the step (D61). `specs/` goes in `benchmark/results/`, as the other runs' did.

**Checked without paying.** `--score-disprove` on the saved Sonarr trace, with no verdicts, gives 24 of 30 and 44%, the numbers 2c reported, before and after alike. `DisproveScoreTests` scores a hand-written key and output with the answers worked out by hand. On the same temp copy, every one of the 16 sections reads `current` to `fsd prepare`, and `trace check` passes all 134 entries.

**The key.** The score uses `answer-key.json` and the frozen matching rule in `FsdScore`, reused, not copied. `match-key.json` isn't read.

## The disprove re-score, finished (2026-11-07, owner: "ok go")

Run with this branch's build at `277b853` (the disprover's instructions unchanged since Nov 4; `disprove-small`, `disprove-callers`, `security-flag` off), Sonnet, cap $10. **61 calls, $7.30 today; $8.59 with Nov 4's 11.** The 10 calls past the 51 left are most likely retries after a failed code check (3 of 14 sections' last answers used one), the tenth the estimate allowed; nothing from Nov 4 was paid twice. Score: `benchmark/results/2026-11-07-phase2c-disprove-score.json`; `specs/` and the run's lines in `benchmark/results/2026-11-07-phase2c-disprove-sonarr/`.

| | Before (2c) | After |
|---|---|---|
| Findings in the BA's queue | 90 | 52 |
| False alarms, from the 20 hand-checked before | 44% (29-59%) | **21%**: noise bar (30%) met, not yet called (D61) |
| Planted found | 24 of 30 | **19 of 30**: 5 planted items lost |
| Hand-checked false alarms that left the queue | | 9 of 12 |
| Hand-checked real findings that left the queue | | 2 of 8 |

Verdicts: 21 stands, 19 refuted, 19 page-gap, 8 cant-tell, 23 not tested (undocumented screens).

**The 5 lost are all `page-gap`, and each answer's own reason says the code shown can't settle it** ("the bundle can't prove no digest exists elsewhere", "the server-side validator isn't in the bundle"). That is `cant-tell`'s meaning, which stays with the BA; `page-gap` goes to a developer. So the step isn't wrongly refuting real findings here: it files doubt as a page gap. G6 and G10 are gaps, C5, C9 and C10 conflicts.

**Not shown:** a fresh hand check of the 52 left (D61), so no bar is called; the 2 real ones that left are among the 5 above or not, unchecked; one run, so run-to-run difference is unmeasured.

## Four more cyberbullying use cases (2026-11-07, counts only)

Stream Tweets, Vectorize Tweets, Predict Cyberbullying Tweets and Logout (7.2.1.2.1-3, 5), the rest of the owner's first five, on a copy with `shared-through` and `disprove` on, headless Opus. 6 calls, $0.20. **Nothing to read from it:** code gave none of the four a candidate, the finder could pick only from the 3 written pages (Login's), and the judge wrote 11 gaps with no page, all `cant-tell` to the disprover. Why, read in the code: three run from `back_end/twitter_stream.py`, a script that is neither a screen nor an endpoint, so the graph has no page for it; Logout is in `front_end/src/components/navigation.js`, a component no page was written for. Open question in `questions.md`.

## Login with `claim-links` (2026-11-07, D145, one use case, counts only)

A fresh copy of the owner's project with `shared-through`, `fsd-claims`, `claim-links` and `disprove` on, `page-verifier` off as on Nov 5, headless Opus: the pop-up's page (1 call), then judge and checker (2 calls), $0.32. Judged a second time: no call, inputs unchanged, as built. 88 claims, 1 not cut cleanly.

| | Before (owner's rulings) | After |
|---|---|---|
| Judge's links | 36 | 34, grouped to 68 agree and 1 undocumented by claim |
| Findings open | 13 (all ruled) | 1 |
| Rulings | 13 | 1 carried (`id-moved`); 12 went `gone`, a history line each, as D145 says when the new judging doesn't find the link |

**What it shows:** the id part works on a real judge: a ruling moved by ids, and the rest went the documented way. The checker confirmed every agree.

**What's lost:** the judge found no gap and no conflict. Of the 5 that Nov 5's run kept and the disprover upheld, at least 4 aren't raised: the Twitter-ID conflict and three undocumented limits. The conflict sits in a flow's title, which D143 sets aside as `flow-label`, so the judge saw it only as a flow name. **Two things changed at once** (headless one-turn against the app's agents on Nov 5, and `claim-links`), so the drop can't be put on `claim-links` alone. Not run: the same judge headless with `claim-links` off.

## 2.6: 2c's leftovers (2026-11-04)
Read at the lines noted, which still line up.
- **`LinkRun.cs:181`, a real defect, fixed.** After the finder ran, the judge was prepared with no run record, so the same pages paid the judge again. It now reads the record. If the inputs match, there's no call, and the finder's cost is still recorded.
- **`LinkRun.cs:256`, a real defect, fixed.** A dispute could turn a requirement's link into `undocumented`, which can't be true of something quoted from the FSD. It stays `agree`, `disputed`, and never bulk-approved.
- **Near it, a real defect, fixed.** A requirement the judge left out even after its retry got no entry and no mention. The run record now names it in `unanswered`. Sonarr has none.
- **`LinkRun.cs:35`, not a defect.** Retired requirements live in their own list, so they aren't judged, and a changed text falls back to sentences.
- **`LinkRun.cs:337`, not fixed, and small.** An entry's id leaves out the statement, so two statements with the same quote would share one id and one entry would be lost. Fixing it changes every saved id, which the hard bar forbids.
- **"56 of 109 requirements read 'no code found'": not a code bug.** On the saved data, all 109 have a link. 53 have one that shows code. Exactly 56 have only gaps, and `Match.IsLink` treats those as no code, as designed. It's what the failed links said. It's also loosely worded: it means no page statement says this, not that no code does. Left as it is; the requirements layer's future is the owner's (D99).

**Not built, on purpose:** gap finders, the per-chapter report, Word comments as questions, anything on the requirements layer's future.

Checked 2026-11-04 (D20, D121): `source-document` v2 → v3. By grep, `Stangent.Spec/Requirements` reads sections by field and never `problems`. Moved to `source-document: 3`.

Checked 2026-11-04 (D20, D121): `trace-links` v3 → v4. By grep, 2c reads `fsd` by field and ignores `sectionId`. Moved to `trace-links: 4`. A feature now has `sectionId`, filled by code.

Checked 2026-11-04 (D20, D121): `telemetry-event` v1 → v2 and `cli-conventions` v2 → v3. By grep, nothing here reads telemetry, and results are read by name, so `components` beside `result` changes nothing. Moved.

## §9's batch 1: what wasn't run (D126, 2026-11-05)

Built under D120, so no fresh check and no fix round. `dotnet test`, the whole-flow test and `phases check` passed before each commit.

**Not run, on purpose:**

- No real AI call of any kind, and no Jira. `spec-run.sh`, `ticket-run.sh`, `benchmark/run.sh`, `benchmark/tickets/run.sh` and `agent_baseline.py` ran only against fakes.
- **So these have met nothing real:** `--max-budget-usd` on one-turn calls; `stream-json` on one-turn calls, though the file finder has used it for real; hooks through `--settings`; `plugin/hooks/hooks.json` and the `agent_type` field it depends on; a real usage-limit message. No real one was saved, so the phrase list is from memory.
- Sonarr's clone at `~/stangent-bench/sonarr` was only read. Its FSD was imported into a throwaway repo: `document.md` byte-identical, section numbers and text hashes the same. `SavedWorkTests`' pins hold.
- Bitwarden wasn't read. `suragms/warehouse-assistant-erp` wasn't fetched.

**The caching check (I.9).** From saved files only: pages' `generated` usage, `.stangent` run records and transcripts in Sonarr's clone. No call was made.

| What | Calls | Written to the cache | Read from it |
|---|---|---|---|
| Page writers | 223 | 3.92M | 0.54M |
| Page verifiers | 194 | 3.70M | 0.18M |
| Link steps, in `.stangent/fsd` | 31 | 0.28M | 0.02M |
| Requirement steps, in `.stangent/req` | 38 | 0.14M | 0.05M |
| File finder, the 40 newest transcripts | 40 runs, many turns | 0.34M | 1.50M |

- Caching is on, automatically, with no setting of ours. These saved calls show one-hour entries.
- **One-turn calls mostly pay for it.** Each call's input is new and nothing runs after it, so what's written is never read. What's read, about 2.4K tokens a writer call, is most likely the shared system prompt, which `--exclude-dynamic-system-prompt-sections` keeps the same between calls. Input outside the cache is almost nothing: 850 tokens across 417 page calls.
- **Multi-turn calls gain,** as each turn reads the last.
- **What could change, neither done:** turning caching off for one-turn calls, if Claude Code allows it (not checked), would stop paying the extra for writes, but it needs a paid test to see the bill and it loses the 2.4K. Putting the bundle first, so a page's verifier reuses what its writer wrote, changes a prompt, so it would have to be built off and re-run. Neither saving is estimated here, as that needs prices this repo doesn't hold.

## First run on a real document (D127, 2026-11-05)

The owner's own project, section by section, counts only: the document holds a name and a student id and this repo is public. Results are in the project's clone, uncommitted.

Headless calls were refused on a weekly limit, so this ran through the app's agents on Opus, one fresh agent per role: writer, verifier, judge, agree checker. One use case so far, the first with candidates in document order.

| | |
|---|---|
| Pages | 2, 23 statements, verifier found 0 problems |
| Links | 11 agree after the checker sent 1 back, 14 gap, 2 conflict, 2 undocumented |
| Findings | 18 |
| Assistant's first read, not the owner's ruling | About 14 of 18 look like false alarms, all gaps; 4 look real |
| Agent tokens | 278K for 6 steps, about 46K a step |
| Plan usage | 5-hour 48 to 55, weekly 65 to 66 |

**Why the gaps:** the use case describes a screen, and the only candidates were two backend endpoints. The screen is rendered directly from the app's root, not through a route, so the graph lists no page for it, though its files exist. Spot-checked in the code: the modal, cancel, tooltip and redirect are all there. This is a miss in what the graph treats as a screen, not a disagreement.

**The fix (off by default, on in that clone):** a new switch, `root-screens`, adds a page for what the app's root shows with no route. Screens 3 to 5, pages 36 to 38, ❓ 0. The use case's section now gets the new screen plus 2 endpoints; before, 2 endpoints. Sections with candidates 113 to 117. The 2 recorded pages still read current and the links still pass. Of the 2 new screens, 1 is a real screen and 1 is a spinner the rule can't tell apart by code. The judge hasn't been rerun.

**Not shown yet:** the owner's rulings; whether fixing it lowers the false alarms; anything beyond one use case. One use case is too small for a rate. The earlier "3 screens" matched the route lines only, so it undercounted the app's screens.

**Re-run with `root-screens` on (same day, one use case, Opus, app's agents):**

| | First run | Re-run |
|---|---|---|
| Agree, after the checker | 11 | 23 |
| Gap | 14 | 8 |
| Conflict | 2 | 1 |
| Undocumented | 2 | 4 |
| Findings | 18 | 13 |
| Assistant's first read, not the owner's ruling: looks real | 4 of 18 | 5 of 13 |

**What's left of the false alarms:** 6 of the 8 gaps are about a shared pop-up. Once a component is shared it gets its own page, and a screen's bundle only links to it, so the judge didn't see it. Same kind of miss, one step along. **Not settled by the fix:** two findings first read as real didn't come back, a conflict and an undocumented item, with the code unchanged, so run-to-run difference in the AI steps is real and unmeasured. One use case still gives no rate. **New and real-looking:** a tooltip and the code disagree on a minimum length, which a section-to-page check caught although no one asked. **Tokens:** the fix 183K against 100K estimated, the re-run 289K against 150K. One agent per item is about 70K; a batch of two in one agent is about 35K each.

**Render routes and shared components (same day, counts only, no AI).** Switches `route-render` and `shared-candidates`, off by default, on in that clone. `shared-candidates` was removed afterwards (D131); these counts are why.

| | Before | After |
|---|---|---|
| Screens planned | 5 | 6, the new one a `render=` route |
| Pages planned | 38 | 39 |
| Sections with candidates | 117 of 300 | 117 of 300 |
| Sections gaining a shared component | | 0 |
| The use case's section | 3 candidates | the same 3 |
| Recorded pages stale, stale quotes | 0, 0 | 0, 0 |

**This doesn't fix the false gaps.** The shared pop-up is two steps from the screen: the screen renders a component that renders it. The rule stops at one step, as asked, so it adds nothing here. Open question in D127.

**A second repo: Actual, a TypeScript app, on a copy.** 12 projects read, 71 routed screens, 92 shared component pages.

| | |
|---|---|
| Screens with no route | 2 |
| Hand-checked, really screens | 0 of 2: both tooltips, mounted by a code editor's hover through their own `createRoot` |
| ❓ from `root-screens` | 8: 4 components it took as picked at runtime, 4 shown by a condition; none checked |
| Routes with `render=` or `children=` | 0, and a search of the source agrees |
| Routed screens directly rendering a shared component | 57 of 71, at most 10, mean 1.3 |

**What doesn't fit:** `root-screens` assumes a `createRoot` is an app's root. Here one mounts a tooltip, so the rule shaped on one app is wrong on the next. Off by default, so nothing else changes. Not fixed; said here. `route-render` has nothing to do on this repo, so it's been tested on one real app only.

**Comparing directly with the code, no pages (owner's idea, same day, one use case, counts only).** `improvements.md` §6 asked for this and it had never been tried. One fresh agent on Opus, not given the pages or the 13 findings. It got the section and six code files, picked by hand by following the screen's imports: 34K characters. No checker ran after it. Nothing was built.

| | Through pages (the re-run above) | Direct |
|---|---|---|
| Agree | 23 | 25 |
| Gap | 8 | 1 |
| Conflict | 1 | 1 |
| Undocumented | 4 | 3 |
| Findings | 13 | 5 |
| Tokens | 289K, with pages written and a checker | 82K, against 50-70K estimated |

**Read against the code by the assistant, not freshly checked and not the owner's rulings:**

| | Through pages, 13 | Direct, 5 |
|---|---|---|
| Nothing there | 7, all the shared pop-up and the PIN step | 1, the error colour, in a CSS file it wasn't given |
| Part real, part not | 1 | 0 |
| Real difference or rule the section leaves out | 4 | 3 |
| Outside the section, covered elsewhere in the document | 0 | 1 |
| For the owner | 1, the Twitter id | 0 |

**What this does and doesn't show:**
- The 7 are confirmed false by reading the code, replacing "about 8".
- **It doesn't separate pages from what the AI was given.** The direct agent saw the pop-up because a person fed it. A judge given the pop-up's page might do as well. Not tested.
- Choosing the code stayed the weak point: the one false gap is a file left out of the hand-picked set.
- **Direct lost things.** It left out one undocumented item the pages route found, and it marked the Twitter id `agree`, calling it the saved id. The check compares the id sent in the request, which reaches the browser from the database. So in normal use it holds, but the server doesn't read the database at that step.
- The 25 agrees were read, not each proven. The pages route's 23 agrees haven't been checked against the code.
- One section, one run each, and the instructions let scenarios share an item, so 5 against 13 isn't a rate. 25 and 23 count different things for the same reason.

**Then pages with the pop-up's page added (owner's OK, same day, one use case, counts only).** Same section and the judge's real instructions. The pop-up had no page, so one fresh agent wrote it (59K tokens) from Stangent's own writer input, and it was added by hand to the judge's saved input. Another fresh agent judged it (74K). No verifier or checker ran, and nothing was written to the clone's `specs/`.

| | Pages, today | Pages + pop-up | Direct |
|---|---|---|---|
| Agree | 23 | 27 | 25 |
| Gap | 8 | 0 | 1 |
| Conflict | 1 | 1 | 1 |
| Undocumented | 4 | 5 | 3 |
| Findings | 13 | 6 | 5 |

**Read against the code by the assistant, not freshly checked and not the owner's rulings:**

| | Pages, today | Pages + pop-up | Direct |
|---|---|---|---|
| Nothing there | 7 | 0 | 1 |
| Part real, part not | 1 | 0 | 0 |
| Real difference or rule the section leaves out | 4 | 5 | 3 |
| Outside the section, covered elsewhere | 0 | 0 | 1 |
| For the owner | 1 | 1 | 0 |

**What this shows:** on this section, the false gaps came from the missing pop-up, not from going through pages. With it added, the pages route had no false alarm, kept the Twitter id question direct lost, and found more.

**What it doesn't show:**
- **A person chose the page,** knowing it was the cause. Nothing in code picks it yet, since the pop-up is two steps from the screen. That's D127's open question, and `shared-candidates` was removed (D131) because its one step didn't reach this.
- **No checker ran.** In the earlier run the checker sent 2 agrees back, on the PIN step and on the colour red, so this judge's 2 matching agrees might not have stood. The colour is one: the page doesn't name it, though the code's colour is red.
- Whether pages hide real differences behind `agree`, which is direct's main promise. Neither list shows one here, and one section can't rule it out.
- **On accuracy this is about a tie, not a win for pages.** Direct's one false alarm is a file left out by hand, and the pages route's `agree` on the colour had no words on the page behind it.
- One section, one run each, so no rate.

**Login's 13 decided (same day), not as a person's judgement.** The owner ruled X-0021 (`code-wrong`, minor), then handed over the rest: "this cyberbullying is just for testing anyway, and im not doing this project anymore so we can just say whatever, u can go ahead". The other 12 are the assistant's reading of the code, written in the clone's `specs/trace.yaml`. `trace check` passes. Not committed there and `trace attribute` not run, so `decided_by` stays empty and no name sits on the assistant's calls.

| Finding | Decision | Severity | Why |
|---|---|---|---|
| X-0021 username minimum | `code-wrong` | minor | Owner. The hint and both account forms say 5; login accepts 4 |
| X-0025 password rules | `accepted` | minor | The section leaves them to the hint; the hint doesn't mention spaces or the symbol list |
| X-0008 7-day sign-in | `accepted` | minor | True, and not in the section |
| X-0014 Twitter id | `accepted` | ungraded | The server compares the id sent in the request, not the saved one. Same in normal use. Security isn't graded here (D132) |
| X-0029 contact details | `not-a-difference` | ungraded | True, but page text, not part of this use case |
| X-0019, 20, 22, 23, 24, 26, 28 | `not-a-difference` | ungraded | The PIN box and the pop-up's Continue and close are in `twitter_login.js` and `pop_up.js`, which the judge never saw |
| X-0027 red highlight | `not-a-difference` | ungraded | The field border is `#FF1414` and the hint shows. The hint's wrong minimum is X-0021 |

**What this does and doesn't show:** 8 of 13 closed as nothing there, matching the earlier reading (7 false, 1 part real). It's still one reader, the same assistant that wrote those readings, so it isn't an independent check and doesn't count toward a bar (D137).

## §9's batch 2 (2026-11-05, D131): what wasn't run

All built with fakes. Tests pass and `SavedWorkTests`' pins hold, untouched. No AI call, no Jira, nothing pushed, and Sonarr's clone wasn't changed: its document was copied to a scratch folder to check the hand-edit warning, which said nothing. The import code didn't change, so the document imports as it did; it wasn't re-imported.

| Item | Tested with | Not run |
|---|---|---|
| D130, bundles store what's unique | Fake drivers; the full text hashed against the pins; `spec assemble` run by hand once on a scratch file | A real call with an assembled bundle. Its bytes are pinned, so a difference would be a bug |
| J.4, `schema-once` (off) | Text with and without the schema | Whether the AI does as well without it in the text |
| T.1, T.3, T.7 | Fake writers, the real state module and content check | A real page corrected; the tooltips on Actual excluded |
| I.1 | Fake drivers; a small app's candidates | `--for-fsd` on a real document. Its saving isn't measured |
| I.2 (off) | A fake writer returning part of a page | Whether a real writer follows the note, and how often a kept statement should have changed |
| I.3 (off) | Bundle contents and the count | **Its effect on `cant-tell`**, the point of the item. Sizes come from `fsd disprove estimate`, free; the rest needs a paid re-score |
| I.4 (off) | Lines shifted in a test graph | A real commit's moves, and how many stale pages it would save |
| I.6 | Two small documents | A real doubtful section's ranking read by a person |

## `shared-through` (2026-11-05, D140): counts, and what wasn't run

Counts only, from the built binary, on copies. No AI call. The switch's tests use fakes. Every existing test passes and `SavedWorkTests`' pins hold; the one existing test changed is `ComponentTests`' list of what's off by default, which has to name a new switch.

| The first real document | Off | On |
|---|---|---|
| Sections with text | 300 | 300 |
| Sections with a candidate | 117 | 117 |
| Candidates in all | 514 | 575 |
| Sections gaining a candidate | | 61, one each |
| Different pages added | | 1, the pop-up, with no page written |
| Left out | | 0 |
| Section 7.2.1.2.4 gets the pop-up | no | yes, `recorded: false` |
| Sections needing the pop-up's page | 4, by its name | 65 |
| Pages `spec needed` lists | 30 of 39 | the same 30 |
| `fsd prepare` on 7.2.1.2.4 | `current`, with its saved links | `judge`, told the pop-up isn't shown, with a warning |

So the page to write was already on the list. What grows is that 61 sections have new inputs and would be judged again, each reading the pop-up once it's written.

| Actual, screens only (it has no FSD) | One step, the old rule | Through unshared ones |
|---|---|---|
| Routed screens gaining a candidate | 50 of 71 | 52 of 71 |
| Most for one screen | 10 | 22 |
| Mean, of the screens gaining | 1.7 | 8.7 |
| Mean, of all 71 | 1.2 | 6.4 |
| Median, of all 71 | | 5 |
| Different pages reached, of 92 | | 56 |
| Screens reaching more than fit beside them (over 9) | | 26 |

- **The old rule gave 57 of 71 when it was measured, and 50 here.** This table reran its code on a graph built on a copy with `root-screens` and `route-render` on, giving the same 71 screens and 92 component pages. The difference wasn't traced.
- **Two more screens gain, but each gains far more.** The added reach is mostly depth: the 454 links found sit at depths 2 to 6, 86 of them at 2 and 239 at 3. All 454 are `certain` lines.
- **Actual's figures are per screen, not per section,** counted by a throwaway program over the same library code, since there's no document to run `fsd candidates` on. A section can reach several screens' components, and can't hold more than `MaxPerSection`.

**What wasn't run:** any judge. Whether the pop-up's page brings the false gaps down when code picks it, as it did by hand, is unmeasured, and so is whether a judge heeds "left out, not evidence of a gap". The pop-up's page hasn't been written. No other use case, no fresh check (D120).

## `shared-through` on Login, with a real AI (2026-11-05, D140, one use case, counts only)

On a copy of the clone, through the app's agents on Opus, with `shared-through` on. Code chose the pop-up's page: no person picked it, and nothing was hand-added to the judge's input. The clone itself wasn't changed.

| | Pages, before (13 findings) | Judge only | After the agree checker |
|---|---|---|---|
| Agree | 23 | 24 | 20 |
| Gap | 8 | 0 | 4 |
| Conflict | 1 | 1 | 1 |
| Undocumented | 4 | 4 | 4 |
| Findings | 13 | 5 | 9 |

**Read against the code by the assistant, not freshly checked and not a person's ruling (D137):**

| | Before | After the checker |
|---|---|---|
| Nothing there | 8 | 4 |
| The five read as real earlier (X-0008, X-0014, X-0021, the password rules, the contact details) | 5 | 5 |

**The 4 left are all gaps the checker made from agrees.** Each one holds in the code, and each page just doesn't say it in words:
- X-0027: the highlight is red (`constant.css`, `#FF1414`); the page says "marked as in error".
- X-0030: the Twitter API gives the link (`organisation.py:358`); the page says the response contains a URL.
- X-0032: the wrong-PIN message is on the endpoint's page (S2), and the judge had linked the Login page's A10.
- X-0034: Continue closes the pop-up (`pop_up.js:11`, `setModal` is passed at `twitter_login.js:89`); the page says what the click sets. Its writer may not have had its instructions, below.

**What this shows:** code picking the page removed the false gaps about what the judge couldn't see, the same as the hand test. The checker read only pages, so it can't tell "the page doesn't say so" from "the code doesn't do so". That's D137's split, seen in one section. It caught nothing real here, on this reading.

**What it doesn't show:**
- One section, one run, so no rate. The 61 sections that would gain the pop-up, most probably unrelated, weren't run, so whether it adds noise there is still open. Actual's 26 screens over `MaxPerSection` weren't run either.
- **No verifier ran:** `page-verifier` was off on the copy.
- **The writer probably lacked its instructions:** its bundle was given unassembled, the assistant's slip (`spec assemble --in-place`, which `plugin/commands/fsd-link.md` says to run). Its five statements match `pop_up.js`, and the code check passed. The judge's and checker's inputs were assembled.
- The judge's first call stopped on the same slip and was resumed, so it wasn't a clean fresh context, though its second read followed the real instructions.

**Found by this run: a decision is lost when a re-judge quotes less of the same statement.** X-0025's and X-0029's decisions dropped, and they came back as `gone` with new undecided findings (X-0033, X-0031), because the judge quoted the first part of the same statement and a link's id follows the quote. X-0008, X-0014 and X-0021 quoted it the same and kept theirs. 2 of 5. This is what batch 2 of §8 said to look at first (D122). Not fixed; in `questions.md`.

**Tokens:** writer 60K, judge 82K, checker 72K, about 215K against 130K estimated; the checker wasn't in the estimate, and the slip added more to the judge. The 5-hour limit moved 0 to 8, weekly 84 to 86.

## The disprove step on Login's 9 findings (2026-11-05, D137, D119, one use case, counts only)

The first run on real findings from a real FSD, on the same copy: the step reads code, not pages. Decisions were cleared on the copy so all 9 went through. One agent on Opus, 147K tokens against 70K estimated: the step makes one call per finding, which the estimate missed.

| | Stands | Refuted |
|---|---|---|
| The 5 read as real (X-0008, X-0014, X-0021, X-0031, X-0033) | 5 | 0 |
| The 4 the checker made (X-0027, X-0030, X-0032, X-0034) | 0 | 4 |

**What this shows:** on this section, with the code in front of it, the step did what D137 asks: the 4 false alarms went and the 5 real ones stayed. The cited lines were read against the code by the assistant and hold.

**What it doesn't show:**
- **One section, one run, no rate,** and "real" and "false" are the assistant's reading, not an independent one. The agent wasn't told that reading.
- **Not independent between findings:** the step is built for one fresh call per finding, and this was one agent for all nine, at about a quarter of the cost. It read bundles 3 to 9 from the claim onward and assumed what came before was the same. It also ran `wc -l` against its brief, which changed nothing.
- **X-0027 was refuted without proving "red":** the CSS wasn't in its bundle, only the error class. The answer is right, by a reading of `constant.css`, but this is a code bundle's gap.
- **X-0034's close is inferred:** the parent that hides the pop-up wasn't in its bundle.
- **X-0014 stands on a bundle that left out the caller:** it says nothing makes the caller send the saved value, and `twitter_login.js:35` does, from the login response. So in normal use it's checked against the saved id, and only a direct call can get around it. Standing is still right, and the wording overstates it.
- **Nothing here catches a real finding wrongly refuted,** the cost D137 names, since none was.

## Claims in a section (2026-11-05, D143): counts, and what wasn't run

`stangent fsd claims` on copies, with `fsd-claims` on. Code only, no AI. Sections with text only.

| | Sections | Claims | Not clean | Words check mismatches |
|---|---|---|---|---|
| The test app's document | 300 | 6,877 | 366 | 0 |
| Sonarr's planted FSD | 16 | 73 | 0 | 0 |

**Not clean on the test app:** `maybe-sentence-end` 166, `continues-above` 83, `ends-open` 74, `starts-mid-step` 43. The rest are 0. By kind: 3,484 sentences, 1,421 table rows, 1,294 steps, 373 figures, 218 list items, 87 flow labels. Its Login use case: 93 claims, 11 not clean. 847 steps have no flow, all in its test-case tables, where each row is its own record. No step in its use-case chapter lacks one.

**Sonarr's:** 56 sentences, 10 list items, 6 table rows, 1 figure. It has no numbered steps, so it doesn't test flows at all.

**Joining a step cut across a page looks possible by a general rule, and isn't built.** Of the 74 cells ending open, in 59 the same column of the next row starts with unnumbered text (57 of them in lower case), and in the other 15 the text was pushed out of the table into a paragraph, which a rule for rows won't reach. No open cell lacked a partner of one kind or the other. Column position is what makes it general. Whether the joins read right hasn't been checked by a person.

**What it doesn't show:**
- Whether the claims read well: that's for a person, and the output was only sampled by the assistant while the rules were written. The flow rules were corrected after reading this document (test-case tables, a label on its own steps row), so its labels aren't an independent test. They stayed general, with no words and no tuned numbers.
- Half a plain sentence, and a continuation row with several values, aren't marked, so the not-clean count is a floor. `continues-above` also lists a few real paragraphs that start in lower case.
- Whether claims help a judge: none has read one. No paid or AI run, and no fresh check (D120).

**Login's 93 claims read by the assistant, all of them (2026-11-05), not freshly checked.** Every flow label is right, and the repeated "closes the pop-up modal" sits under its own three flows, which is what the split was for. **Not claims:** about 10 rows of column headings, and the table's id, name, actors and so on. **Broken:** 5 steps cut in half by a page break, 10 of the 11 not clean. **Many repeat:** "1. The user enters the correct credentials" appears in several flows, so 93 is about three times the judge's 29 links, and the assistant's "about 30" was wrong. **So "code lists every claim left unanswered" would be noisy as first designed:** it needs headings set aside, and one answer for the same words across flows, or the judge's work roughly triples. To settle before the id slice.

**Claim ids and links by ids (D145, `claim-links`, off), 2026-11-06. Counts only: no judge, no AI, no fresh check.**

| | Claims | Set aside | To judge | Lines the judge reads |
|---|---|---|---|---|
| Test app, Login (7.2.1.2.4) | 93 | 16 (8 flow labels, 6 column headings, 2 label rows) | 77 | 50 |
| Test app, whole document | 6,877 | 570 (373 figures, 87 flow labels, 55 column headings, 55 label rows) | 6,307 | 5,549 |
| Sonarr's planted FSD | 73 | 1 (a figure) | 72 | 72 |

- **Ids stable on a second run:** yes on both documents, with 0 issued, 0 retired, and `claims.json` and `ids.json` byte-identical. The words check still shows 0 mismatches.
- **Login's judge input, whole, in characters:** 18,451 on against 19,249 off, or 4% smaller. Without the instructions and schema, 14,549 against 16,065. The list falls by almost half and the input doesn't, since the candidate pages are most of it. A first version naming each flow on every line was 24,281, larger than off, so flows are now named once.
- **Against the judge's 29 links on Login:** 50 lines is still more than 29, and whether that's noise or claims the old judge skipped is something only a real judge can show. Stripping a step's number before grouping would give 42; not built.
- **Sonarr shows nothing about repeats:** it has no numbered steps.

**What wasn't run:** any judge, checker or disprove step on either document. Both are fakes in tests. Nothing shows a ruling surviving on a real run, or that a judge answers a line for all its flows correctly. The bug is reproduced in a test only: the same statement judged twice, with the ruling carried by the same id.

**Small fixes (D146), 2026-11-06. Counts only: no judge, no AI, no fresh check. Only the test app's document was measured, on a scratch copy; Sonarr's has no numbered steps and wasn't re-run.**

| The test app's document | Before | Numbers aside (`claim-links`) | And joins (`fsd-claims`) |
|---|---|---|---|
| Lines the judge reads, Login | 50 | 42 | 36 |
| Lines the judge reads, whole document | 5,549 | 5,504 | 5,455 |
| Claims, whole document | 6,877 | 6,877 | 6,840 |
| Claims, Login | 93 | 93 | 88 |
| Not clean, whole document | 366 | 366 | 290 |
| Not clean, Login | 11 | 11 | 1 |

- **Not clean by reason, before and after:** `ends-open` 74 to 33, `starts-mid-step` 43 to 14, `continues-above` 83 to 77, `maybe-sentence-end` 166, unchanged. Words check mismatches: 0 both times.
- **41 joined,** against the earlier 59 cells with a partner, 57 in lower case. So 16 lower-case partners were left apart by the strict rule, by subtraction, not counted one by one. All 41 now end in a stop.
- **What's left on Login** is one `continues-above`, the row "Governmental Organisation User"; none of the 5 cut steps remains. Ids were stable on a second run, with 0 issued, 0 retired and `claims.json` byte-identical.
- **Read by the assistant, not a person:** Login's 5 in full, and the start and end of the other 36. A person hasn't read them.
- **`disprove-callers` wasn't measured on anything.** The copy has no findings, so `fsd disprove estimate` is 0 with it on or off. The graph does hold the line from `twitter_login.js`'s component to `POST /api/twitter_login`. Fakes only.

**What wasn't run:** any judge, checker or disprove step. Nothing shows the shorter lines or the joined claims help a judge, or that the instruction moves a verdict.

**Workflow files (D144, D147, `workflow-files`, off), 2026-11-06. Counts only: no judge, no AI, no fresh check. Only the test app's document was run, on a scratch copy.**

| The test app's document | |
|---|---|
| Flows | 87, with 538 steps; 87 flow labels, none without steps |
| Sections with flows | 28 of 340, so 312 have none; 23 of the 28 hold a use-case table |
| Statuses | 87 `not judged`; 0 of everything else; 0 open questions |
| Claims in those sections in no flow | 219 |
| Links on no step | 38, all on Login, all older links by quote |
| Folder | 88 files, 100,064 bytes, 352K on disk |
| Step order | 68 flows by their own numbers, 14 kept in place because numbers repeat or some are missing, 5 with no numbers |
| Login (7.2.1.2.4) | 8 flows, 0 match, 8 not judged |

- **A rebuild gave byte-identical files,** and `specs/fsd` and `specs/trace.yaml` didn't change. With the switch off, the command said so and wrote nothing.
- **Read by the assistant, not a person:** Login's folder. Its out-of-order flow now reads 1 to 12.
- **5 of the 28 "use cases" aren't use cases:** a sentence and a list that look like a flow. They're counted as they are, in D147's cost.

**What wasn't run:** any judge, checker or disprove step, and Sonarr's planted FSD, which has no numbered steps; a test covers a document with no flows. Nothing but fakes has produced `matches`, `open`, `differs` or `not answered`, so whether the count helps a PM is unshown.

**Security flag (D141, D148, `security-flag`, off), 2026-11-06. Fakes only: no AI, no judge, no disprover, no fresh check.**

- **Built:** the disprove step's variant instructions and `finding-disprover-security-output` v1; the citation check and its retry; `stangent trace security`; `finding-security` on the state module; the `security` queue, `trace links`, the ticket bundle and workflow files.
- **Not a security review:** it only looks at findings, so a hole that matches the FSD is never flagged. Phase 6's review stays (D132).
- **Contracts (D20):** `trace-links` v4 and `ticket-bundle` v2 grew optional fields in place, since neither is on `main`. No phase goes `needs review`.
- **With the switch off:** existing tests pass and the hash pins hold. The only edits to them were the two lists D148 names.

**What wasn't run:** any real disprover, so no AI has read the new instructions, and nothing shows how many flags are false, or how many real ones it misses. Nothing was run on the test app or Sonarr, and no finding has been re-asked. The cost of turning it on, one paid call per undecided finding that can be tested, is by count of the rule, not measured. No person has answered a flag outside a test.

## Section approval (2026-11-06, D150): what wasn't run
- **Built, off (`section-approval`), fakes only:** 19 tests on hand-built Word files (`FsdSectionApprovalTests`): approve, reject, a mix, an answer dropping on changed text and holding on the same, a typed answer ignored, a doubtful section settled first, promote refusing while unanswered, a rejected removal kept with its id and link, a rejected addition never given an id, links on a rejected change keeping their decision, a mix that doesn't fit refused, `--partial`, a stale candidate naming its sections, section files, the review view, the switch off. All 2107 tests pass.
- **Not run:** any real FSD, two real versions of one, a real BA or PM's review, a PR with the section files or the review pasted, a fresh check (D120). No AI, no paid run.
- **Known limits:** a rejection touching a section with attached files is refused; a changes-only file must number its headings right on its own; the merged version's `original.hash` is the candidate file's, so `fsd reread` can offer the rejected changes again.

