---
phase: 2c
name: spec
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 2, cli-conventions: 3, benchmark: 1, code-graph: 4, impact-report: 5, reference-page: 1, source-document: 3, trace-links: 4, ticket-bundle: 2, bug-proof: 1 }
provides: {}
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

