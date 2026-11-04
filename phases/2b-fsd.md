---
phase: 2b
name: fsd
status: done
consumes: { store: 1, artifact-base: 1, telemetry-event: 2, cli-conventions: 3, benchmark: 1, code-graph: 4, impact-report: 5, reference-page: 1, reference-lines: 1, evidence: 1 }
provides: { source-document: 3, trace-links: 4 }
---

# Phase 2b · FSD and discrepancies

**Approved 2026-10-20 (as part of the Phase 2 plan). Split from `2-spec.md` when building started (D59); it builds on
2a's reference pages (`2a-reference.md`).** See 2a for the three layers (D52) and where the files live.

## What gets built

### 1. Import (code)
- `stangent fsd import <file.docx>`: a deterministic `.docx` → Markdown conversion in C# (Open XML SDK, no external
  tool), keeping headings, numbered lists and tables. Images are kept as files with their captions; no AI reads them.
- **Section IDs:** the FSD's own numbering (`4.2.3`); a heading path where a section has none. FSDs follow no standard,
  so both are first-class, even within one document. Duplicate or missing numbers are reported, not guessed.
- **The original** is archived and cited by archive name, path and content hash (D16). Phase 2's archive backend is a
  folder or a second git repo set in `stangent.yaml`.
- The same file always gives byte-identical text; a file with a known content hash is imported once.

### 2. Candidate pairs (code, D53)
- `stangent fsd candidates`: names that match across the two sides: screen names ↔ routes, components and page titles;
  field labels ↔ DTO properties; endpoint names. Case and word order are normalised.
- An FSD section with no candidate goes to the AI with the **list of page titles only** (not the pages), which
  proposes candidates for the same checks. The share found by code vs by the AI is reported.

### 3. Judging (AI, D53, D60)
- A plugin subagent, `stangent-link-judge`, gets one FSD section and its candidate pages. It returns a state (agree /
  gap / conflict / undocumented) with the **exact FSD sentence and the exact page sentence** it relies on.
- **Pages no section links to** are listed as undocumented by code, not by the AI.
- The judge is its own subagent with a fresh context, never the page writer's (D61): it sees the FSD section and the
  finished pages, nothing about how they were written.
- **"Agree" verdicts get a second fresh check** before they can be bulk-approved (D62): a separate subagent sees the
  same section and pages and either confirms or raises a finding with quotes. A disagreement makes the link a finding,
  reviewed one by one. This is the guard on D53's weak spot.

### 4. Check (code)
- Both quotes exist word for word; both IDs exist; every symbol the quoted page sentence cites is in the graph.
  Anything failing is rejected before it reaches the queue.

### 5. The decisions file (D58)
- `specs/trace.yaml`: one entry per link or finding, with both ends, the state, both quotes, `decision` (agree /
  code-wrong / fsd-wrong / defer) and `decided_by`.
- **CI checks:** the schema and allowed values; both quotes still match the current FSD and page (the D11 hash),
  otherwise the decision drops back to undecided; `decided_by` comes from the PR's approver or merger through the
  code-host adapter (D24), never typed.
- Bulk approval of "agree" is one edit over a chapter (D53).
- **Impact answers reach FSD sections** through the links: a new group in `impact-report` v5. Per D20, building it
  reopens 1a (the provider) and bumps the version there.

### 6. Proving it on Sonarr (D56, D57)
- `benchmark/fsd/sonarr/`: an FSD in our own words for 10–15 screens (`.docx`; some chapters numbered, some plain prose), and an answer key with
  **30 planted items: 10 gaps, 10 conflicts, 10 undocumented**. Both are committed and frozen before the first run;
  the answer key never goes into a bundle.
- A scoring command compares findings with the answer key; 20 unplanted findings are hand-checked once.

## Done when (D57's bars)
- [x] **Finds what's planted:** ≥ 80% of planted items overall, and ≥ 60% of each kind.
- [ ] **Not too noisy:** ≤ 30% of findings are false alarms. *Failed: 31.3% (95% range 14–45%), from a fresh agent's
      hand check (D67). Findings ship off by default (D68).*
- [x] **Grounded:** every link and finding in the queue passes the checks; tests show a made-up quote is rejected.
- [x] **Repeatable:** the same `.docx` gives byte-identical text; a re-run with unchanged inputs makes zero AI calls.
- [x] **Cost:** tokens and time per section and per page reported; candidates found by code vs AI reported.
- [x] Each share has its 95% range (fixed seed). If "finds" or "not too noisy" fails, discrepancy findings ship off
      by default and the report says why (D57).
- [x] End-of-phase report written; its claims checked against the results files by a fresh agent (D61); CI green.
      *CI green on GitHub ([run](https://github.com/hohkokyoung/stangent/actions/runs/40114060098)).*

## Contracts
| Contract | v1 holds |
|---|---|
| `source-document` | document ID, archive name, path, content hash, section IDs and their text |
| `trace-links` | links and findings: both ends, state, quotes and their hashes, decision, `decided_by` |
| `impact-report` v4 → v5 | an FSD-sections group (provided by 1a, reopened per D20) |

The roadmap's earlier `spec` and `discrepancy` contracts are replaced: the spec is now the reference pages plus the
links (D52), and a discrepancy is a link's state (D58).

## Not in this phase
- PDF, Excel and Confluence import; D15's document versions and supersession (one version per document until a
  project has several).
- **E2E tests as an input to pages.** The company's pipeline uses them; Playwright links wait for 1d (D45).
- The dashboard (Phase 8, D58); "code-wrong" → a Jira ticket (Phase 3); tests tagged with FSD section IDs (Phase 5).
- The dry run on a real FSD from one of the owner's projects (D56): after 2b. Formats other than `.docx` get their own
  converter when a real FSD needs one.

## Progress

### Step 1 · import, candidates, judging, the decisions file · 2026-10-20
- **`src/Stangent.Spec/Fsd/`**: the `.docx` converter (Open XML SDK; headings with Word's automatic numbering, typed
  numbers or none; lists, tables, images with captions), the import with its archive, the candidate pairs, and the
  link run (judge → code check → agree check → record). **`src/Stangent.Spec/Trace/`**: `specs/trace.yaml`, its CI
  check, bulk approval and `decided_by` from git; impact answers reach FSD sections (`impact-report` v5, 1a reopened).
- **Commands:** `stangent fsd import | sections | candidates | prepare | check | undocumented`, `stangent trace check |
  approve | attribute`. **Plugin:** `stangent-candidate-finder`, `stangent-link-judge`, `stangent-agree-checker`,
  `/stangent:fsd-link`.
- **The planted FSD** (`benchmark/fsd/sonarr/`): 15 screens, 30 planted items, frozen before the first judging run.
  A fresh review (D61) found the scope named the 3 planted screens; changed to the areas the FSD's introduction names,
  before any run (2a Progress step 2).

### Step 2 · the Sonarr run · 2026-10-24
- First score, on a page set of the 82 candidate pages plus the 10 sampled handlers (92): 25 of 30 found, undocumented 5 of 10, so "finds"
  failed (`2026-10-24-phase2b-sonarr-score-first.json`). Two misses had no page: one planted screen (U3,
  `/system/tasks`, which `fsd undocumented` can only see once it is recorded) and one behaviour on an unrecorded screen
  (U9, colour-impaired mode on `/settings/ui`, found by the judge once the page existed). The set had left out the
  in-scope screens no section names.
  That set broke D66's definition ("the FSD's in-scope screens and the endpoints and components they link to"); the 21
  screens were generated and the score taken again on D66's set (113 pages). No bar, key or matching rule changed.

## End-of-phase report

*2026-10-25 · first report.* "Finds what's planted" **passes**; "not too noisy" **fails narrowly** (31.3% false
alarms against 30%; the 95% range, 14–45%, sits on both sides of the bar), so discrepancy findings ship **off by
default** (D57, D68); pages and links ship. CI green on GitHub
([run](https://github.com/hohkokyoung/stangent/actions/runs/40114060098)); phase **done**, with the noise bar failed as recorded.

### Conclusion of Phase 2 (2a and 2b together)
- **Works:** stangent writes accurate, grounded descriptions of what code does: 2 wrong statements in 88 on the
  hand-read (so Sonnet is enough), every statement cites code inside its slice, about $0.20 a page ($85 for all 718
  Sonarr pages), and a re-run with nothing changed costs nothing. It keeps up per commit (median 1 stale page).
- **Half-works:** it finds where the FSD and the code differ (27 of 30 planted, every gap and conflict) but raises
  too many false alarms (31%, bar 30%), mostly from the second check turning partial agreements into gaps.
- **So:** pages and FSD ↔ page links ship and are ready as context for Phases 3 and 4; the drift list ships off by
  default (`fsd.findings`, D68) and needs a person's review when turned on.
- **Caveats:** a planted FSD is tidier than a real one, and both hand checks were an AI's (D67), so real results will
  likely be worse until a person checks them.
- **Next, worth doing before relying on the findings:** cut the noise (the second check's bar for partial agreement;
  redirect-only routes as screens) and re-run Sonarr as a new benchmark; then the dry run on a real FSD from one of
  the owner's projects, checked by a person (D56).

### 1. What was built
See Progress step 1. Contracts: `source-document` v1 (`schemas/source-document.v1.json`), `trace-links` v1
(`schemas/trace-links.v1.json`), and `impact-report` v4 → v5 (the FSD-sections group, provided by 1a, reopened per
D20). AI-step contracts: `candidate-finder-output.v1.json`, `link-judge-output.v1.json`, `agree-checker-output.v1.json`.
Benchmark: `--score-fsd`, `--fsd-docx`; tests with canned outputs in `FsdTests.cs` and `FsdLinkTests.cs`.

### 2. Benchmark results (Sonarr at 0bffefe7, Sonnet 5.5; `benchmark/results/2026-10-24-phase2b-sonarr-score.json`)
Sonarr only (D56): it is the one benchmark repo with an FSD and an answer key.

**Finds what's planted** (bar: ≥ 80% overall and ≥ 60% of each kind; 95% ranges from 1,000 resamples, seed 42)
| | Found | Share (95% range) | Same kind as planted |
|---|---|---|---|
| gaps | 10 / 10 | 1.00 (1.00–1.00) | 10 / 10 |
| conflicts | 10 / 10 | 1.00 (1.00–1.00) | 5 / 10 (5 reported as gaps) |
| undocumented | 7 / 10 | 0.70 (0.40–0.90) | 7 / 10 |
| **overall** | **27 / 30** | **0.90 (0.77–1.00)** | 22 / 30 |
| **bar** | **passes** | | |

The bar uses the frozen matching rule, under which a gap reported as a conflict (or back) counts as found; the
same-kind column is reported beside it. The three misses are judge misses, not missing pages: each behaviour is on its
page ("Search … Cutoff Unmet" on Add New, "Import Selected" on the queue, "Unmonitor Selected" on Missing). One (U5)
was raised, as a conflict on the right statement rather than as undocumented.

**Not too noisy** (bar: ≤ 30% of findings are false alarms): 89 findings, 62 unplanted. Worst case (every unplanted
one false) 0.70. Of the 20 sampled (seed 42), a fresh agent checking against Sonarr's source (D67,
`2026-10-24-phase2b-handcheck.json`) found **11 real drift and 9 false alarms**: false-alarm share 62/89 × 9/20 =
**0.313 (95% range 0.14–0.45)**. **The bar fails.** The 9 false alarms: 7 gaps where the FSD statement is in fact true
of the code (6 of them agrees the second check disputed; of the 7 disputed agrees in the sample, 6 were false), and 2
routes that only redirect to `/series` (`/`, `/seasonpass`), listed as undocumented screens. One "real" one (A7, the
Cutoff Unmet search option on Add New) is planted item U5 reported as a conflict; the frozen matching rule needs the
undocumented state, so it counts as unplanted. Where the 62 unplanted findings come from: 25 are "agree"
links the second check disputed (D62 turns each into a gap; most say the page shows a field but not its meaning), 19
are in-scope screens no section links to (by code), 8 are undocumented items the judge raised, and 10 are other gaps
(6) and conflicts (4).

**Grounded:** every one of the 107 entries in `specs/trace.yaml` passes `trace check` (quotes word for word, ids,
cited symbols; 0 problems, 0 stale quotes: `2026-10-24-phase2b-sonarr-trace-check.json`); tests show a made-up quote and a 4-character fragment are rejected.

**Repeatable:** the same `.docx` imports to the same hash (`unchanged`); a re-run of all 16 sections made zero AI
calls and left `specs/` byte-identical (`2026-10-24-phase2-sonarr-rerun.json`).

**Cost** (per section, single-turn): candidates found by code for 14 sections, by the AI for 2 (none came back empty).
| Step | Calls | Input tokens per section | Time per section | Cost |
|---|---|---|---|---|
| candidate finder | 2 | 9.4k | 3.4 s | $0.07 |
| link judge | 16 | 9.2k | 5.9 s | $0.65 |
| agree checker | 15 | 9.4k | 5.5 s | $0.58 |
| **all** | 33 | | | **$1.30** |
Per page: see 2a's report ($0.20 mean). The recorded Sonarr proof cost about $24: pages $22.48, 2b $1.30 (each
section's latest run, which includes the two sections re-run when their candidate screens were added) plus about
$0.19 for those two sections' first runs. Not counted: the 10-page single-turn measurement and the first run whose
files were lost (2a Progress step 3).

### 3. Decisions made or changed
- [D63](../decisions.md): 2b gives an FSD section its screen pages plus the endpoints those screens call.
- [D58](../decisions.md) refined: a decision drops back to undecided when a quote no longer matches, not on any edit.
- [D62](../decisions.md): the agree checker, applied as decided; its effect on noise is in Known gaps.
- [D67](../decisions.md): the hand check was made by a fresh agent, not the owner.
- [D68](../decisions.md): findings ship off by default (`fsd.findings`), as D57 says when a bar fails.

### 4. Known gaps
- **Findings are off by default (D68):** the noise bar failed; a project opts in with `fsd.findings: true`.
- **The second check is strict:** 25 of 62 unplanted findings are disputed agrees (the page covers only part of the
  FSD statement, often a field without its meaning); 6 of the hand check's 9 false alarms are disputed agrees.
- **Redirect routes count as screens:** `/` and `/seasonpass` only redirect, yet were listed as undocumented.
- The hand check is an AI's (D67); a person's check is owed before findings are relied on.
- **Same-kind accuracy:** half the planted conflicts came back as gaps (the page lacks the rule rather than stating a
  different one), so a reviewer sees the right place but sometimes the wrong label.
- A planted FSD is tidier than a real one (D56): the score overstates quality; the dry run on a real FSD is next.
- One version per document; PDF, Excel and Confluence import wait for a real need.
- `fsd undocumented` only sees screens with a recorded page, so a project must generate every in-scope screen first.

### 5. Impact on other phases
- Provides `source-document` v1 and `trace-links` v1 (new). `impact-report` v5 is provided by 1a (reopened); its
  consumers were checked by `phases check`.

### 6. Follow-ups
- The dry run on a real FSD from one of the owner's projects (D56).
- "code-wrong" decisions → draft Jira tickets (Phase 3); tests tagged with FSD section ids (Phase 5); the dashboard
  over `trace.yaml` (Phase 8).
- Cut the noise, then measure again as a new benchmark run (D68): the agree checker's bar for "partial" agreement,
  and leaving redirect-only routes out of the screen list.
- A person's hand check of the findings on the real-FSD dry run (D56, D67).


### Fresh check of this report (D61) · 2026-10-24
A separate agent checked both reports' claims against the results files: about 80 claims, 7 wrong or unsupported plus
2 minor. All were fixed: the 2b cost table's tokens and times (taken from the first score file), the breakdown of the
62 unplanted findings (it had counted all 89), "26" → "25" disputed agrees, the cost of the 605 pages left ($85 was
for all 718), which first-score misses lacked a page (one screen, one behaviour), the 92-page set's make-up, and the
`trace check` claim (its output is now committed); minor: "of 718" → the 717 pages at that commit, and what the ~$24
total includes. None was rejected.

A second fresh agent checked the lines added once the hand checks were in (2026-10-25): about 50 claims, 5 wrong,
unsupported or misleading, all fixed: a "judgement call" remark with no evidence in the results files (dropped), the
writer-model decision missing from decisions.md (D66 refined), what most false alarms are (6 of 9 are disputed
agrees, not "a field without its meaning"), the hand-read's verifier comparison (A3's error is an endpoint link,
which the verifier isn't asked to judge), and 0.70 × 9/20 written for 62/89 × 9/20. It also confirmed 121 tests pass.

### Needs review 2026-11-04 · `code-graph` v3 → v4 (overhaul 1.1, 1.2; D115)
v4 added `opens-screen` lines, kept apart from the walked lines, and `meta.dirtyFiles`. Follow-up: confirm nothing here depends on v3, re-run this phase's tests, then move `consumes` to `code-graph: 4`.
Checked 2026-11-04 (D20): by grep, `Stangent.Spec` reads none of the new values. All tests pass, so this now consumes `code-graph: 4`.

## Overhaul (2026-11-04)

Slice 2.1 of the overhaul, D116: the Word importer stops failing silently. Code only, no AI, and the same file gives the same bytes.

**Built.** Details are in `docs/spec.md`.
- Tracked changes stop the import with `docx_tracked_changes` and a count. `--accept-changes` imports the accepted view, and moved text comes in once.
- Hidden text and hidden table rows are left out and listed. A hidden paragraph mark alone hides no words, so its text stays and is listed.
- Comments are listed one per line, with section, author and state, in a shape `DocxComment.Parse` reads back.
- Footnotes and endnotes come in labelled at the end of their section.
- Embedded files, charts, SmartArt, text boxes, content controls, fields, equations and symbols are named by kind and section; readable text stays.
- A nested table keeps its words and is listed. A cell merged down rows repeats its value and is listed.
- A completeness check runs on every import: the file's words, read apart from the converter, per section, against the words rendered.
- Causes the check would have flagged, fixed: rows and cells inside a content control were lost, a text box in a table cell doubled, and a text box's words ran into the words around it. Also fixed: a row starting part-way across the grid slid its cells left.
- Leftovers: a dangling `*.spec.ts` link no longer crashes `AppSession.RelatedSpecs`, and a command name with a hyphen no longer loses its telemetry event.

**Byte check.** Sonarr's planted FSD, against `HEAD` (9580ebd) before the change: `document.md` and `document.json` are byte-identical, `problems` is still empty, and a test pins the hash. The completeness check flags nothing on it, and nothing on any `DocxWriter` file in the tests. There's no other `.docx` in the repo.

**Tested.** Each rule has a hand-built file in `FsdDocxTests`. With the rules removed together in a temporary build, 22 tests failed, at least one for each rule removed. The accepted view's handling of moved text wasn't part of that run.

**Known gaps.**
- No file saved by Word itself has been through this: Sonarr's was written by a script. A real one may flag something new.
- A merge across columns leaves blanks, not its value. The brief assumed it repeats; changing it would change imported text, so it stays.
- `w:cellMerge` counts as a tracked change but isn't applied. White text isn't listed. Table styles aren't read for hidden or struck text.
- A file imported earlier stays `unchanged` with no new lines until its bytes change, even if tracked changes had been resolved silently.
- Ticket attachments read the accepted view with nothing listed: the bundle has no place for it without a contract change.

**Impact on other phases.** `source-document` stays v1, with only its `problems` description widened, so no phase goes to `needs review`. `ticket pull` passes `acceptChanges`, so a tracked change can't stop a pull, and an attachment's text now follows these rules too: moved text once, hidden text out, footnotes in. 2.2 can match against the same text. 2.5 can read comments through `DocxComment.Parse`. Telemetry's event name for `graph screen-tests` is now `stangent.graph.screen_tests`; `stangent.command` keeps the hyphen.

### Slice 2.2: stable section ids and a version diff (D117)

Code only, no AI. Details are in `docs/spec.md`.

**Built.**
- Each section gets a permanent id (`S-0001`) beside its number, from a ledger per document. `id` stays what it was, because saved hashes use it. Never reused; a removed id stays marked removed.
- A document imported earlier gets its ids on its next import, the same file included, which stays `unchanged`. There's no first-read change: a read never writes.
- A changed file waits in `specs/fsd/<doc>/candidate/` with `SECTION_DIFF.md` and `section-diff.json`, and the current version keeps serving. A first import goes straight in.
- Matching uses exact signals only, with no scores. A shared sentence never matches anything: it makes a section doubtful. A sentence counts with at least 4 words, the same floor `Sentences.Cited` uses.
- `fsd diff`, `fsd resolve` and `fsd promote`. `decided_by` comes from git, and `promote` refuses with `doubtful_sections_remain`. Two new lifecycles are on the state module, `fsd-candidate` and `fsd-section-match`, with history in `specs/fsd/history.jsonl`.
- Promotion carries by code: trace entries (with their new id, decision kept), feature files, quotes, questions, parents, run keys. Each removed section's items are listed and kept. The existing drop-back (D58) runs at once.
- `TraceCheck` and `Attribute` find a moved entry's copy in the base file by its link without the section, so a promotion doesn't look like a typed `decided_by` or a new decision.
- `resolve --removed` is beyond the brief: in a merge, a section of the current version has no candidate section to settle it from.

**The hash bar.** Met. Against the build before this slice (6b3c9d4), on Sonarr's FSD: `document.md` byte-identical, `document.json` identical once the ids are taken off, all 15 requirement input hashes the same, measured by running both builds. A test pins the two file hashes, upgrades a saved document in place, and shows the saved run, link id and judge hash all unchanged and `req prepare` answering `current`.

**The measure.** A planted edit of `benchmark/fsd/sonarr/`, **not a real version change**, so "proven on a real version change" stays open (D56). Planted: one chapter inserted, one section moved, one reworded, one split in two.

| Outcome | Planted | Found |
|---|---|---|
| Unchanged | 18 | 18 |
| Moved | 1 | 1 |
| Renamed | 0 | 0 |
| Modified | 1 | 1 |
| Added | 1 | 1 |
| Removed | 0 | 0 |
| Doubtful | 3 (the split: its old section and two new ones) | 3 |

7 sections changed number. The numbers follow from the edit script, written before the first run, so it's a check that the rules do what they say, not of how a real editor's changes look.

**Tested.** 41 tests in `FsdVersionTests`, including one reviewed pair pinned whole. 22 single changes to the code, each removing a rule or leaking the new id into a bundle, each failed at least one test.

**Known gaps.**
- No real document with two versions has been through it.
- Body text is compared exactly. A section whose text mentions another section's number ("see 4.2") counts as modified after a renumbering, and so does one whose image file name shifted. That's a person's read and possibly a paid re-run, never a wrong match.
- A sentence repeated across a document makes a new section doubtful with each section holding it. That's noise for a person, never a wrong match.
- A renamed section's links and items follow it, but its runs go again at the next prepare, because the AI read its title. `runAgain` lists them.
- `finderInputs` isn't carried: working it out needs the page set, which promotion doesn't load. A renumbered section with no candidates by name runs the finder again.
- A promotion isn't one atomic write. A crash part-way leaves references moved and the old document in place, and running it again would move them twice. The files are committed, so `git checkout` undoes it.
- A `same_as` keeps the id, so a split's second half is a new section. Its share of the old section's links isn't told apart.
- D15's version dates and draft or approved status aren't built: a candidate is promoted by a person, whichever is newer.
- Trace history is by entry id, so a moved entry's old lines stay under the old id. `promotions.jsonl` holds the pairs.
- `S-0001` shares its shape with a requirement scenario's id. They live in different files per document and no command takes both.
- A deleted ledger catches up with the live ids only, as requirement ids do. Don't delete it.

**Impact on other phases.** `source-document` went to v2 in this file. 2c and 3 were each checked by grep and moved to `source-document: 2`, each with its line. Neither reads what changed, and neither goes to `needs review`. `fsd import`'s `replacedSections` and `replaced` status are gone; nothing outside the tests read them. Later slices can cite the permanent id; links and requirements still cite the number.

### Fix round after the fresh check (2026-11-04)

**Found, one line each, all fixed.**
- A removed section's trace entry kept its old number, so it read as the new section with that number, and promotion's `trace check` then dropped its decision. Now it takes the removed mark, keeps its decision and `decided_by`, is reported in `carried.removed[].decisions`, and is flagged by `trace check`. While there: a resolution draft crashed on a section it couldn't find, and a removed section's link made a screen count as documented.
- Text in an `mc:Fallback` with no `mc:Choice` was dropped with `problems` empty. Now the converter and the completeness check read it, inline and as blocks.
- A candidate couldn't be thrown away, and re-importing the current file left it waiting silently. Now there's `stangent fsd discard <doc>`, through the state module, and the `unchanged` result says plainly that a candidate is waiting and how to discard it.
- A rewritten section under a repeated heading came out as `added` plus `removed`, setting its id aside silently. Now a section left on both sides with the same heading path is doubtful, even with no sentence shared; a unique path is still `modified`.
- A `.docx` ticket attachment with no headings stopped the whole pull, as the note above said: confirmed by a test. Now it reads flat, and a damaged one is listed with `notConverted`. A PDF attachment never stopped it: it's listed by name, type and size (D72).

**Still untested.** No file saved by Word itself has been through any of this, the `mc:Fallback` rule included.

### Reopened 2026-11-04 · Findings carry evidence and a severity (overhaul 2.4, D118)

**Built.** `TraceFindings`: `trace check` gives each finding an `X-0001`, a first severity of `ungraded`, and `evidence` records filled by code, then validates them with `EvidenceValidator` and the rules it can't know. `Severity` in Core, one scale. `finding-severity` on the state module: only a person grades, seen at `trace attribute`, with `severity_by` from git. `trace check` lists ids and severity in `findings`.

**Separation (D53).** Checked, and it held: a page statement comes from the page's JSON only, and a writer can't cite `X-0001`, because it isn't in the graph. New: `trace check` rejects evidence citing a finding. Each has a test.

**The hash bar.** `SavedWorkTests` passes with a finding stamped and graded in the file. The judge's input files for that section hold no `X-` id, severity or note. An entry's id is unchanged. Tickets reads named fields of an entry only, none of them new.

**Measures, no AI.** A plain `trace check` on a temp copy of Sonarr's real `trace.yaml`, written by v2 (never written to that clone): 134 entries, 90 findings, each issued an id and valid evidence on the first try, no problems. All 134 ids, decisions and `decided_by` unchanged. A second check passes. The old build and this one give the same 755 input hashes on that copy.

**Contracts (D20).** `trace-links` v2 → v3, additive. This file still said v1, though v2 shipped with 2c; it says v3 now. 2c and 3 were checked by grep and moved, each with its line. This phase now consumes `evidence` v1 and `reference-lines` v1.

**Fresh check (2.4).** Found: evidence was written once and never refreshed, hand-typed evidence passed as fact, a partial type's note was lost, and a decision survived a rephrase that reversed its statement.

**This round fixed them (D118).** Evidence is derived again on every check and refused by name when typed, it carries the partial type's note, and `decided_on` drops a decision whose statement's text changed. No input hash moved.

**Known gaps.**
- Every finding is `ungraded` until a person edits the file. There's no command for it, and no AI suggestion until 2.5.
- Findings are still off by default (D68), and the false-alarm rate behind that hasn't changed.
- ~~Evidence isn't refreshed.~~ Fixed in this slice's fix round. As first built: a code citation stays at the commit it was stamped with, and a document's `contentHash` at the version read, after a page or a document moves on. A new record is added only where one is missing.
- `fsd undocumented` and `fsd check` write findings with no id: the id comes at the next `trace check`. `fsd undocumented` still returns a count, with no ids.
- A finding whose link is judged again with other quotes has a new entry id, so it's a new finding with a new `X-` id and `ungraded`, as a decision drops back today.
- No per-chapter report: on `roadmap.md`'s list, still to do.
- `severity_by` shows who merged it, the same limit `decided_by` has.

**Impact on other phases.** 2c and 3 read entries by field and ignore what's new, so both move to `trace-links: 3` with no `needs review`.

### 2.5 The disprove step · 2.6 Leftovers (2026-11-04, D119)
**Built.** Details are in `docs/spec.md`; the re-score, its estimate and 2.6's outcomes are in `phases/2c-spec.md`.
- `fsd disprove prepare|check|estimate`, `finding-disprover-output` v1, `stangent-finding-disprover`, a stage in `spec-run.sh --fsd` and `spec-run.sh --disprove`. Off unless `fsd.disprove` is on, set like `findings` (D68).
- A bundle built by code: the claim in a fixed sentence, the section, the page's statements, the code at numbered lines, what couldn't be shown, the entry points. No finder, no note, no check (D61).
- Five verdicts, moved only by code on the state module, each routed: `developer-confirm` (then `closed` once a person confirms), `developer`, `ba`.
- `page-wrong`, `suggestedSeverity`, and the listing's `goesTo`, `verdict`, `label`, `quote`, `at` and `queues`.

**The hash bar: met.** `SavedWorkTests.Saved` has the same 25 values, and `DisproveTests` compares them again after a verdict is written. On a temp copy of Sonarr's real data, all 16 sections read `current` to `fsd prepare`, with no AI. A `trace.yaml` from before this step reads and checks, and `trace check` passes on the real 134 entries.

**It isn't shown to help.** No paid run has happened. 2c's numbers (24 of 30, 44%) still stand as the only measured ones.

**Contracts (D20).** `trace-links` v3 grew in place, unshipped, so no bump. New: `finding-disprover-output` v1, `finding-verdict`, `finding-review` and `disprove-run` on the state module. Readers of `decision` were checked by grep: `Bundle`, `Drafts` and `FsdCarry` carry it as text; `FsdImpact.verified` now reads `page-wrong` as not verified; `trace approve` skips anything decided. A closed finding leaves the ticket bundle, a draft and impact, only when the setting has made one, so Phase 3 has no `needs review`.

**Known gaps.**
- The step can only remove findings, so it can't raise found to 27.
- `undocumented` screens found by code can't be tested: 23 of Sonarr's 90.
- A verdict can be older than the code until the next `disprove prepare`. `trace check` doesn't see that, and a closed finding stays closed meanwhile.
- A person who works out `disproof_hash` for a typed verdict passes, on the terms of `evidence_hash` (D118).
- `graph where` gives a type's first part only, and 44 of Sonarr's 62 bundles hit the code limit, so a refuting line can be out of sight. The bundle names what it left out, and the likely answer is then `cant-tell`.
- Regenerating a `page-wrong` page sends the finding back even if the page came out the same.
- Sonarr's saved graph is too old to cite lines, so the real run needs `graph build` first.

**Fresh check of 2.5 and 2.6 (2026-11-04).** What it found, and what this round did:
- A `refuted` quote only proved the text exists, so any real line closed a finding. Now `refuted` and `unreachable` wait in `developer-confirm` until a person confirms or sends them back (D119's note).
- A dropped decision let the old verdict close the finding again, for good with `fsd.disprove` off. Now the finding goes to the BA, labelled with why.
- `"verdict":"weird"` twice wrote a `claimed` outside the schema and broke `trace check`. Now `claimed` is kept only if it's a verdict.
- `[]`, `5`, `null` and `{"verdict":5}` crashed `disprove check`. Now each is a failed answer: one retry, then `cant-tell`.
- A broken `trace.yaml` stopped page generation, and `spec stale` the same way. Now each reports it and carries on.
- The scorer counted decided and `page-owner` findings as lost. Now only what the step took out counts, including `developer-confirm`.
- `counts.undecided` included closed findings. Now it holds only the BA's.
- Two graph tests that failed in a copy without `.git` pass in the real tree: only the missing `.git`.

## §8 batch 1, under D120 (2026-11-04): what this phase's contracts gained

**Not checked, under D120:** no fresh check of any of this. `dotnet test`, the whole-flow test and `phases check` pass.

**S.2 · `source-document` v2 → v3 (D121).** `problems` become objects: `kind`, `detail`, and `sectionNumber`, `section`, `page`, `line`, `author`, `state`, `text` where they apply. `ConvertedDocument.Problems` stays lines, so the converters and their tests didn't move, and `ImportProblems` reads lines for old documents and new imports alike. Sonarr's `document.md` keeps its hash, and `document.json` with its ids taken off keeps its hash too, since it has no problems. 2c and 3 were checked by grep (they read sections by field, never `problems` or `schemaVersion`) and moved, each with its line.

**S.3 · One set of import steps.** `IImportFormat` and `ImportPipeline` (D121); `fsd import`'s result has `steps`. Word's and Sonarr's `document.md` are byte-identical (the pinned hash) and the PDF tests didn't change.

**S.1 · `trace-links` v3 → v4.** `fsd.sectionId`, by code at `trace check`. 2c and 3 were checked by grep and moved, each with its line. A build before this one can't read a v4 file. `requirements` v1 grew `sectionId` in place: 2c is still being built, and it has provided nothing.

Checked 2026-11-04 (D20, D121): `telemetry-event` v1 → v2 and `cli-conventions` v2 → v3. By grep, nothing here reads telemetry, and results are read by name, so `components` beside `result` changes nothing. Moved.

## §8 batch 2, under D120 (2026-11-04)

**Not checked, under D120:** no fresh check of any of this. `dotnet test`, the whole-flow test and `phases check` pass. Sensitivity labels and locked files have only been run on hand-built files, never one saved by Word or a real labelling tool. `fsd reread`'s candidate path has only been run by making the saved text look older, never with two real builds.

**`source-document` v3 grew in place (D20, D122).** `tables`, each picture's `id` and `section`, and `labels`, all optional, all in no hash. v3 is on this branch only, not on `main`, so it isn't shipped and takes no bump. A document without them gets `tables` and picture ids on its next import.

**A.10, A.11, E.3.** `fsd outline`, `fsd show`, `fsd export` and `--chapter` are read-only over `document.json` and `document.md`. They've only run on hand-built documents and the fake `claude`. Export's 1500 lines isn't measured against a real FSD.
