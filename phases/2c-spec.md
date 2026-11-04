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
