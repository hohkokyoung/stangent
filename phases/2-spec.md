---
phase: 2
name: spec
status: planned
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3, impact-report: 4 }
provides: { source-document: 1, reference-page: 1, trace-links: 1 }
---

# Phase 2 · Spec

**Approved 2026-10-20. Not built until the Phase 1d split check reports (D45).**

Builds on D10–D12, D18 and D50–D60. stangent is a standalone rebuild modelled on the company's spec pipeline (D50),
with one difference that matters: the agreed FSD is read alongside the code, so stangent can show where the code
drifted from what the client agreed (D51).

"The spec" is three layers (D52):

| Layer | What it is | Written by | Checked by |
|---|---|---|---|
| **FSD** | the agreed documents, converted to text, never rewritten by AI | the client / BA | conversion is code |
| **Reference** | pages describing what the code does, one per screen or backend entry point, every statement citing symbols | AI (Claude Code, D60) | code: citations exist and sit inside the page's slice |
| **Links + decisions** | FSD section ↔ page, and every gap, conflict and undocumented item, with a decision | AI proposes, people decide | code: quotes, hashes, `decided_by` from git |

Phase 2 is split (D59): **2a** builds the reference and is useful by itself (any project with no FSD). **2b** adds the
FSD and the discrepancies. Both live in this file until building starts, then split into `2a-…` and `2b-…` as Phase 1 did.

## What waits on Phase 1d
- **Nothing is built until 1d's split check reports** (D45).
- **The parallel-vs-sequential verdict doesn't change this plan.** Writing a page and judging a link only read code;
  they're independent per page and run side by side whatever 1d says. 1d is about parallel *code edits* (Phase 4).
- **Left open: the slice rule.** Each page's input is its graph slice within impact's depth limit (D54). 1d part 2
  (context scoping, D45 job 2) tests whether a graph slice is enough context for an agent. If it finds slices miss
  what agents need, the slice rule (depth, what's included) changes before 2a is built.

## Where the files live
```
specs/
  fsd/<doc-id>/          converted FSD text, one file per document, plus its images     (source-document, 2b)
  reference/<page-id>.md page, rendered by code from its JSON; .json beside it         (reference-page, 2a)
  trace.yaml             links, findings and decisions                                 (trace-links, 2b)
```
Committed: everything above (D52: AI output is stored, not regenerated). Not committed: the graph (D25) and the
bundles sent to the AI (regenerable from the graph).

## 2a. Reference from code

### 1. The page list (code)
- `stangent spec pages` lists every page from the graph (D54):
  - **screens:** one per frontend route, ID `page:/series/:id`;
  - **backend pages:** each entry point no screen reaches (endpoints only other services call, event handlers,
    scheduled jobs), grouped per project, ID'd by the entry point;
  - **shared components** used by 3 or more screens, ID'd by the component.
- Server-rendered screens: the controller action that returns the view is the screen (D54's default).

### 2. The bundle (code)
- `stangent spec prepare <page>` writes one bundle file: the slice (the walk from the page's entry point within the
  impact depth limit, D27), the source text of each method in it with its ✅/🟡 labels, the instructions, the output
  schema, and **the page's previous version**, so a regeneration edits the affected statements instead of rewording all of it (D55).
- A slice over the size cap is reported, not silently cut.
- The bundle's hash is the page's input hash (D18).

### 3. Writing (AI, D60)
- A plugin subagent, `stangent-page-writer`, reads the bundle and writes the page as JSON. Its tools are limited to
  reading the bundle and writing the result.
- The page's sections are **Shows**, **Actions** (each action → the endpoint it calls), **Rules** (validations,
  limits, permissions) and **Side effects** (events, emails, tables written). Every statement cites one or more symbol IDs.

### 4. Check and record (code)
- `stangent spec check <page>`: the schema; every statement cites at least one symbol; every cited symbol is in the
  graph **and in this page's slice**. The slice's ❓ unknowns are appended by code ("couldn't see"), never by the AI.
- A failed check returns one specific error for **one retry**; a second failure marks the page failed and lists it.
- Recorded: the page's JSON and Markdown, the commit, the bundle hash, the model and the tokens.
- **Reuse:** an unchanged bundle hash means no AI call.

### 5. Keeping current (D55)
- `stangent spec stale --diff <base>..<head>` (code): pages whose bundle hash changed, as Markdown for the PR.
- Thin CI workflow (D24): on a PR, post the stale list; on merge to main, regenerate the stale pages with headless
  Claude Code and open a **spec-only PR from the bot** (D12), merged by a person once its checks pass.
- With AI switched off (D24), only the stale list runs.

### 6. Plugin
- `/stangent:spec-generate <page> | --stale | --all` and the `stangent-page-writer` subagent.

### 7. Proving it on Sonarr
- Generate every page at Sonarr's pinned commit (D6).
- Measured: page count; check failures on the first try and after the retry; tokens and time per page; zero AI calls
  on a re-run; on Sonarr's last 50 main-line commits, how many pages each commit flags as stale (median and worst).
- The owner hand-reads 5 pages; the report counts wrong statements. No bar: it tells 2b whether the pages capture rules.

### Done when (2a)
- [ ] Every Sonarr screen and backend entry point has a page, or is listed as failed with its reason.
- [ ] **Grounded:** every statement cites symbols inside its slice; tests plant a made-up citation and an out-of-slice
      citation, and both are rejected.
- [ ] **Repeatable:** a re-run with no code change makes zero AI calls and leaves the files byte-identical.
- [ ] **Cost:** tokens and time per page in the report.
- [ ] Stale detection works on a fixture repo; Sonarr's stale counts per commit are in the report.
- [ ] 5 pages hand-read, wrong statements counted.
- [ ] Tests run without an LLM (canned subagent outputs); CI green.

## 2b. FSD and discrepancies

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

### Done when (2b), D57's bars
- [ ] **Finds what's planted:** ≥ 80% of planted items overall, and ≥ 60% of each kind.
- [ ] **Not too noisy:** ≤ 30% of findings are false alarms.
- [ ] **Grounded:** every link and finding in the queue passes the checks; tests show a made-up quote is rejected.
- [ ] **Repeatable:** the same `.docx` gives byte-identical text; a re-run with unchanged inputs makes zero AI calls.
- [ ] **Cost:** tokens and time per section and per page reported; candidates found by code vs AI reported.
- [ ] Each share has its 95% range (fixed seed). If "finds" or "not too noisy" fails, discrepancy findings ship off
      by default and the report says why (D57).
- [ ] End-of-phase report written; CI green.

## Contracts
| Contract | v1 holds | Sub-phase |
|---|---|---|
| `reference-page` | page ID and kind, sections, statements with cited symbol IDs, the commit, bundle hash, model, tokens | 2a |
| `source-document` | document ID, archive name, path, content hash, section IDs and their text | 2b |
| `trace-links` | links and findings: both ends, state, quotes and their hashes, decision, `decided_by` | 2b |
| `impact-report` v4 → v5 | an FSD-sections group (provided by 1a, reopened per D20) | 2b |

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
