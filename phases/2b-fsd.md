---
phase: 2b
name: fsd
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3, impact-report: 5, reference-page: 1 }
provides: { source-document: 1, trace-links: 1 }
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
- [ ] **Finds what's planted:** ≥ 80% of planted items overall, and ≥ 60% of each kind.
- [ ] **Not too noisy:** ≤ 30% of findings are false alarms.
- [ ] **Grounded:** every link and finding in the queue passes the checks; tests show a made-up quote is rejected.
- [ ] **Repeatable:** the same `.docx` gives byte-identical text; a re-run with unchanged inputs makes zero AI calls.
- [ ] **Cost:** tokens and time per section and per page reported; candidates found by code vs AI reported.
- [ ] Each share has its 95% range (fixed seed). If "finds" or "not too noisy" fails, discrepancy findings ship off
      by default and the report says why (D57).
- [ ] End-of-phase report written; its claims checked against the results files by a fresh agent (D61); CI green.

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
- First score, on a page set with only the candidate pages (92): 25 of 30 found, undocumented 5 of 10, so "finds"
  failed (`2026-10-24-phase2b-sonarr-score-first.json`). Two of the misses were planted screens that had no page:
  `fsd undocumented` only sees recorded screens, and the set had left out the in-scope screens no section names.
  That set broke D64's definition ("the FSD's in-scope screens and the endpoints and components they link to"); the 21
  screens were generated and the score taken again on D64's set (113 pages). No bar, key or matching rule changed.

## End-of-phase report

*2026-10-24 · first report.* "Finds what's planted" **passes**; "not too noisy" is **waiting for the owner's hand
check** of 20 unplanted findings. Build, tests (120) and `phases check` pass locally; the branch isn't pushed yet, so
CI hasn't run on it.

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
one false) 0.70. The 20 sampled for the hand check (`2026-10-24-phase2-owner-check.md`): **waiting for the owner**;
the bar passes if about 12 or more of the 20 are real drift. Where the unplanted findings come from: 26 are "agree"
links the second check disputed (D62 turns each into a gap; most say the page shows a field but not its meaning), 22
are in-scope screens no section links to (by code), 12 are undocumented items the judge raised, the rest gaps and
conflicts.

**Grounded:** every one of the 107 entries in `specs/trace.yaml` passes `trace check` (quotes word for word, ids,
cited symbols; 0 stale quotes); tests show a made-up quote and a 4-character fragment are rejected.

**Repeatable:** the same `.docx` imports to the same hash (`unchanged`); a re-run of all 16 sections made zero AI
calls and left `specs/` byte-identical (`2026-10-24-phase2-sonarr-rerun.json`).

**Cost** (per section, single-turn): candidates found by code for 14 sections, by the AI for 2 (none came back empty).
| Step | Calls | Input tokens per section | Time per section | Cost |
|---|---|---|---|---|
| candidate finder | 2 | 8.6k | 2.7 s | $0.07 |
| link judge | 16 | 9.4k | 5.7 s | $0.65 |
| agree checker | 15 | 9.5k | 5.2 s | $0.58 |
| **all** | 33 | | | **$1.30** |
Per page: see 2a's report ($0.20 mean). The whole Sonarr proof cost about $24 (pages $22.48; 2b $1.30 plus the two
sections re-run when their candidate screens were added).

### 3. Decisions made or changed
- [D63](../decisions.md): 2b gives an FSD section its screen pages plus the endpoints those screens call.
- [D58](../decisions.md) refined: a decision drops back to undecided when a quote no longer matches, not on any edit.
- [D62](../decisions.md): the agree checker, applied as decided; its effect on noise is in Known gaps.

### 4. Known gaps
- **The second check is strict:** 26 of 62 unplanted findings are disputed agrees where the page shows a field but
  not the FSD's explanation of it. Whether that is noise or useful is what the owner's hand check measures.
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
- Depending on the hand check: tune the agree checker's bar for "partial" agreement (a new benchmark run, reported as such).

