---
phase: 2b
name: fsd
status: planned
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
