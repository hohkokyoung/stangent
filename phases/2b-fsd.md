---
phase: 2b
name: fsd
status: done
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
