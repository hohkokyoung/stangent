---
phase: 2a
name: reference
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3, impact-report: 5 }
provides: { reference-page: 1 }
---

# Phase 2a · Reference from code

**Approved 2026-10-20 (as part of the Phase 2 plan). Building started 2026-10-20, before the Phase 1d split check
reports (D45, changed 2026-10-20). Split from `2-spec.md` when building started (D59); 2b is in `2b-fsd.md`.**

Builds on D10–D12, D18 and D50–D64. stangent is a standalone rebuild modelled on the company's spec pipeline (D50),
with one difference that matters: the agreed FSD is read alongside the code, so stangent can show where the code
drifted from what the client agreed (D51).

"The spec" is three layers (D52):

| Layer | What it is | Written by | Checked by |
|---|---|---|---|
| **FSD** | the agreed documents, converted to text, never rewritten by AI | the client / BA | conversion is code |
| **Reference** | pages describing what the code does, one per screen or backend entry point, every statement citing symbols | AI (Claude Code, D60) | code: citations exist and sit inside the page's slice |
| **Links + decisions** | FSD section ↔ page, and every gap, conflict and undocumented item, with a decision | AI proposes, people decide | code: quotes, hashes, `decided_by` from git |

Phase 2 is split (D59): **2a** builds the reference and is useful by itself (any project with no FSD). **2b** adds the
FSD and the discrepancies (`2b-fsd.md`).

## What waits on Phase 1d
- **Nothing here waits for the split check (D45, changed 2026-10-20).** Its parallel-vs-sequential verdict shapes
  Phase 4. Writing a page and checking it only read code; they're independent per page whatever 1d says.
- **Left open: the slice rule.** 1d part 2 (context scoping, D45 job 2) tests whether a graph slice is enough context
  for an agent. If it finds slices miss what agents need, the slice rule (depth, what's included) changes, and every
  page goes stale through its input hash.

## Where the files live
```
specs/
  fsd/<doc-id>/          converted FSD text, one file per document, plus its images     (source-document, 2b)
  reference/<page-id>.md page, rendered by code from its JSON; .json beside it         (reference-page, 2a)
  trace.yaml             links, findings and decisions                                 (trace-links, 2b)
```
Committed: everything above (D52: AI output is stored, not regenerated). Not committed: the graph (D25) and the
bundles sent to the AI (regenerable from the graph).

## What gets built

### 1. The page list (code)
- `stangent spec pages` lists every page from the graph (D54):
  - **screens:** one per frontend route, ID `page:/series/:id`;
  - **backend pages:** each entry point no screen reaches (endpoints only other services call, event handlers,
    scheduled jobs), grouped per project, ID'd by the entry point. *Changed by D63 while building: every endpoint
    gets a page, and screens stop at the endpoints they call;*
  - **shared components** used by 3 or more screens, ID'd by the component.
- Server-rendered screens: the controller action that returns the view is the screen (D54's default).

### 2. The bundle (code)
- `stangent spec prepare <page>` writes one bundle file: the slice (the walk from the page's entry point within the
  impact depth limit, D27; *made concrete by D63: frontend depth 6, backend depth 4, points with 10+ callers are leaves*), the source text of each method in it with its ✅/🟡 labels, the instructions, the output
  schema, and **the page's previous version**, so a regeneration edits the affected statements instead of rewording all of it (D55).
- A slice over the size cap is reported, not silently cut.
- The bundle's hash is the page's input hash (D18).

### 3. Writing (AI, D60)
- A plugin subagent, `stangent-page-writer`, reads the bundle and writes the page as JSON. Its tools are limited to
  reading the bundle and writing the result.
- The page's sections are **Shows**, **Actions** (each action → the endpoint it calls), **Rules** (validations,
  limits, permissions) and **Side effects** (events, emails, tables written). Every statement cites one or more symbol IDs.
- The writer never verifies its own page (D61); that's 4b.

### 4. Check and record (code)
- `stangent spec check <page>`: the schema; every statement cites at least one symbol; every cited symbol is in the
  graph **and in this page's slice**. The slice's ❓ unknowns are appended by code ("couldn't see"), never by the AI.
- A failed check returns one specific error for **one retry**; a second failure marks the page failed and lists it.
- Recorded: the page's JSON and Markdown, the commit, the bundle hash, the model and the tokens.
- **Reuse:** an unchanged bundle hash means no AI call.

### 4b. Fresh-agent verification (D61, D62)
- After the code check passes, a **separate subagent, `stangent-page-verifier`**, starts from a fresh context with the
  bundle and the finished page only (not the writer's output log). It flags each statement the slice's code doesn't
  support, quoting the source line that contradicts it or saying nothing in the slice supports it.
- Code checks each finding's evidence: the quoted line exists in the slice. A finding without evidence is dropped.
- The writer gets the findings for its one retry; each one is fixed or rejected with a stated reason, recorded on the page.

### 5. Keeping current (D55)
- `stangent spec stale --diff <base>..<head>` (code): pages whose bundle hash changed, as Markdown for the PR.
- Thin CI workflow (D24): on a PR, post the stale list; on merge to main, regenerate the stale pages with headless
  Claude Code and open a **spec-only PR from the bot** (D12), merged by a person once its checks pass.
- With AI switched off (D24), only the stale list runs.

### 6. Plugin
- `/stangent:spec-generate <page> | --stale | --all` and the `stangent-page-writer` subagent.

### 7. Proving it on Sonarr
- Generate every page at Sonarr's pinned commit (D6).
- Measured: page count; check failures on the first try and after the retry; verifier findings per page, and how many
  were fixed vs rejected; tokens and time per page (writer and verifier apart); zero AI calls
  on a re-run; on Sonarr's last 50 main-line commits, how many pages each commit flags as stale (median and worst).
- The owner hand-reads 5 pages; the report counts wrong statements. No bar: it tells 2b whether the pages capture rules.

## Done when
- [ ] Every Sonarr screen and backend entry point has a page, or is listed as failed with its reason. *Changed by D64:*
      every page 2b needs plus a sample of 10 handlers; all 718 checked against the size cap by code.
- [ ] **Grounded:** every statement cites symbols inside its slice; tests plant a made-up citation and an out-of-slice
      citation, and both are rejected.
- [ ] **Repeatable:** a re-run with no code change makes zero AI calls and leaves the files byte-identical.
- [ ] **Cost:** tokens and time per page in the report.
- [ ] Stale detection works on a fixture repo; Sonarr's stale counts per commit are in the report.
- [ ] Every page passed the fresh verifier (D62), each finding fixed or rejected with a reason; a test plants a wrong
      statement and the verifier's evidence check is exercised with canned outputs.
- [ ] 5 pages hand-read, wrong statements counted, and compared with what the verifier caught.
- [ ] Tests run without an LLM (canned subagent outputs); CI green.

## Contracts
| Contract | v1 holds |
|---|---|
| `reference-page` | page ID and kind, sections, statements with cited symbol IDs, the commit, bundle hash, model, tokens |

## Not in this phase
- **E2E tests as an input to pages.** The company's pipeline uses them; Playwright links wait for 1d (D45).
- The dashboard (Phase 8, D58).

## Progress

### Step 1 · page list, slices, bundles, the run, checks · 2026-10-20
- **`src/Stangent.Spec/`**, a new library: `PageList` (screens, server-rendered screens, shared components, backend
  pages), `SliceBuilder` (the forward walk), `Bundles` (the writer's input and its hash), `PageRun` (prepare → write →
  check → verify → fix → record, as a small state machine), `PageChecks` (schema, citations, verifier evidence),
  `PageRender` (Markdown from the JSON) and `Staleness`.
- **Commands:** `stangent spec pages | bundle | prepare | check | stale`. `prepare` and `check` return the next step
  (`write`, `verify` or `none`) with the subagent to run and its prompt, so drivers stay a few lines.
- **Drivers:** the plugin's `/stangent:spec-generate` (one page in the session, or the batch script) and
  `plugin/scripts/spec-run.sh` (headless `claude -p`, parallel, for CI and the benchmark).
- **Contracts:** `schemas/reference-page.v1.json`, plus the two AI-step contracts `page-writer-output.v1.json` and
  `page-verifier-output.v1.json`.
- **Sonarr's handlers:** `benchmark/configs/sonarr.spec.yaml` adds Sonarr's `IHandle<T>`, `IHandleAsync<T>` and
  `IExecute<T>` to `publish-handle`, so its 200 event and command handlers are entry points (D54's "event handlers,
  scheduled jobs"). It's a separate file so the graph Phases 1b–1d scored stays the same.
- **First real pages** (Sonarr, Sonnet 5.5 through `claude -p`): the tag API endpoint (22k-character bundle) took 25 s
  and $0.13 to write and $0.10 to verify, passed the code check first time, and the verifier found nothing; a re-run
  made no AI call.

**The slice rule didn't hold as planned** (bundle characters, ≈ 4 per token, Sonarr at the pinned commit):

| Rule | Screens: median / largest | Screens reaching an endpoint | Pages | Bundles over 200k | All bundles |
|---|---|---|---|---|---|
| plan: one walk, depth 4 | 57k / 191k | 27 of 37, not the code behind it | 589 | 0 | 27M |
| plan: one walk, depth 6 | 129k / 465k | 30 of 37 | 599 | 125 | 64M |
| split at endpoints, depth 6 / 4, 10+ callers are leaves | 51k / 254k | 28 of 37 (all 364 endpoints have pages) | 718 | 2 | 16M |

Asked the owner; **decided 2026-10-20: split at endpoints (D63)**. The code supports both through `spec` settings in `stangent.yaml`.
Bundles show types, properties and fields inside the code that uses them rather than as rows of their own: one
`/series/:titleSlug` bundle went from 982k to 330k characters at the same depth.

### Step 2 · fresh review (D61) · 2026-10-20
A separate agent reviewed the code from a fresh context against the plan and decisions (report in the session notes).
Ten findings; each was fixed or rejected with a reason:

| # | Finding | Outcome |
|---|---|---|
| 1 | `fsd check` crashed on a missing output and burnt the retry | fixed: a missing output gets its retry; test |
| 2 | any 4-character fragment passed as a quote | fixed: prose quotes are ≥ 4 words or a whole cell/item/statement; code evidence ≥ 12 characters and two tokens (D53, D62); tests |
| 3 | the bot's spec PR had no checks; regenerated pages left decisions on stale quotes; subagents could write anywhere | fixed: `trace check` runs on the bot PR, the merge job runs `trace check --fix`, subagents may only read and write under `.stangent/` (D60) |
| 4 | a section whose candidate pages failed became a "gap by code" | fixed: reported as failed (D53); test |
| 5 | the verifier had no retry | fixed (D62); test |
| 6 | the answer key's scope named the 3 planted screens | fixed before any judging run: the scope is the areas the FSD's introduction names (D53) |
| 7 | the converter read "2023 Roadmap" as section 2023; images and nested tables in cells were dropped; text boxes read twice; list counters per instance | fixed: a typed number must fit the numbering around it, else a plain heading and a problem; the rest fixed; test |
| 8 | the agree check's missing answers were silent; a dispute without a state became a conflict | fixed: noted on the entry and counted (`unchecked`); a dispute without a state stays "agree", disputed |
| 9 | parallel drivers could read half-written files | fixed: shared files are written whole (temp file, then rename) |
| 10 | the stale comment lacked FSD sections; the depth cut-off wasn't recorded; docs said `spec bundle` prints the previous version; fixed pages aren't re-verified | fixed the first three; **rejected** the last with a reason (D62 refined): the plan doesn't ask for a second pass, and code checks every fix |

### Step 3 · single-turn calls, then the Sonarr proof · 2026-10-20 to 2026-10-24
- **Single-turn calls (D64):** `spec-run.sh` sends each step as one call: the input files in the prompt, `--tools ""`,
  no settings, skills or MCP servers, `--json-schema` from `next.schema` (`stangent spec schema <name>`). On the same
  10 pages: 41k input tokens and $0.18 per page, against 200k and $0.31 with two file-reading sessions
  (`benchmark/results/2026-10-20-phase2a-singleturn-measure.json`). `--agents` keeps the subagent mode.
- **The scratch folder holding the first run was emptied between sessions** (the Sonarr clone, its graph, ~161 pages,
  half a 2b run). The proof was run again from scratch, and its pages and results are now committed after each batch
  (`benchmark/results/2026-10-24-phase2-sonarr/`).
- **The page set** (`benchmark/sonarr-page-set.py` → `benchmark/fsd/sonarr/page-set.json`): every in-scope screen (35),
  every page an FSD section names as a candidate (82, overlapping), and 10 handlers sampled with seed 42: 113 pages.
  The first pass left out the 21 in-scope screens no section names, which hid planted undocumented screens from 2b's
  check (it only sees recorded screens); they were added before the final score, and the first score is kept.

## End-of-phase report

*2026-10-24 · first report.* Built and proven on Sonarr; the owner's 5-page hand-read is **waiting for the owner**.
Build, tests (120) and `phases check` pass locally; the branch isn't pushed yet, so CI hasn't run on it.

### 1. What was built
- **`src/Stangent.Spec/`**: the page list (screens, server-rendered screens, shared components, every endpoint and
  handler, D54 as changed by D63), slices (D63: screens stop at endpoints; depth 6 / backend 4; 10+ callers are
  leaves; cap 260k characters), bundles and their input hash, the page run (write → check → verify → fix → record),
  the code checks, Markdown rendering and staleness.
- **Commands:** `stangent spec pages | bundle | prepare | check | stale | schema`.
- **Plugin:** `stangent-page-writer` and `stangent-page-verifier`, `/stangent:spec-generate`, the batch driver
  `plugin/scripts/spec-run.sh` (single-turn by default, D64), the CI workflow `plugin/ci/spec.yml` (stale list on a PR,
  regeneration and a bot spec PR on merge).
- **Contracts:** `reference-page` v1 (`schemas/reference-page.v1.json`), plus the AI-step contracts
  `page-writer-output.v1.json` and `page-verifier-output.v1.json`.
- **Benchmark:** `--spec-stale N`, `--page-stats`, the Sonarr page set and its runner (`benchmark/sonarr-run-batch.sh`).
- **Tests:** canned subagent outputs, no LLM (`tests/Stangent.Core.Tests/SpecTests.cs`).

### 2. Benchmark results (Sonarr at 0bffefe7, Sonnet 5.5)
Phase 2 is proven on Sonarr only (D56): it is the benchmark repo with an FSD and an answer key. nopCommerce, eShop and
CleanArchitecture have no FSD, and a page run there would show only more of the same (D64).

**Pages** (`2026-10-24-phase2a-sonarr-page-stats.json`, `…-sonarr/specs/reference/`)
| | Result |
|---|---|
| Pages the graph calls for | 718 (37 screens, 117 components, 364 endpoints, 200 handlers); **all 718 under the size cap**, checked by code |
| Generated (D64's set) | 113: 35 screens, 5 components, 62 endpoints, 11 handlers (10 sampled, 1 a section names); **0 failed** |
| Code check | 110 passed first time, 3 after the one retry, 0 failed twice |
| Fresh verifier | 14 findings on 13 pages (3 contradicted, 11 unsupported); 14 fixed, 0 rejected, 0 dropped for lack of evidence |
| Statements | 1,739; median 13 per page, largest 42 |
| Re-run, nothing changed | 113 pages current, **zero AI calls**, `specs/` byte-identical (`2026-10-24-phase2-sonarr-rerun.json`) |

**Cost per page** (writer + verifier, single-turn, list price)
| Kind | Pages | Median slice | Mean cost | Mean input tokens | Mean time |
|---|---|---|---|---|---|
| screen | 35 | 53k chars | $0.38 | 91k | 30 s |
| endpoint | 62 | 19k | $0.13 | 28k | 16 s |
| handler | 11 | 12k | $0.09 | 20k | 12 s |
| component | 5 | 9k | $0.05 | 13k | 8 s |
| **all** | **113** | 22k | **$0.20** ($22.48 in all; writer $13.0, verifier $9.5) | 46k | 20 s |

Writer and verifier apart: medians 14k input tokens and 12 s for the writer per page; the verifier's totals are 2.38M
input tokens, 517 s and $9.50 over 113 calls. At these per-kind means all 718 pages would cost about $85 (37 × $0.38 + 117 × $0.05 + 364 × $0.13 + 200 × $0.09).

**Stale pages per commit** (`2026-10-20-phase2a-sonarr-stale.json`, Sonarr's last 50 main-line commits, no AI): median
1, p90 31, worst 82 of 718.

**Hand-read** (5 pages picked with seed 42, `2026-10-24-phase2a-handread-pick.json`): **waiting for the owner.**

### 3. Decisions made or changed
- [D63](../decisions.md): slices split at the API boundary; every endpoint gets a page (changes D54).
- [D64](../decisions.md): the proof is the pages 2b needs plus 10 handlers; Sonnet writes and verifies; calls are
  single-turn (refined with the measurement).
- D60 refined (the batch driver's permission rules) and D62 refined (evidence length, the verifier's retry, no second
  verifier pass after a fix).

### 4. Known gaps
- **Not every page exists:** 605 of 718 pages (mostly endpoints, half of them v5 copies of v3, and handlers) are left to
  `spec-run.sh --all` (D64).
- **The slice rule may change** when 1d part 2 (context scoping) reports (D45); every page then goes stale.
- **Pages describe only their slice:** a screen page names the endpoints it calls but not their rules (D63's cost).
- **The verifier fixes aren't re-verified** (D62 refined): code checks each fixed statement changed.
- Per-kind costs for components and handlers rest on 5 and 11 pages.
- The ❓ blind spots of the slice are listed on each page by code, not resolved.

### 5. Impact on other phases
- Provides `reference-page` v1, consumed by 2b (current). No contract of an earlier phase changed in 2a.

### 6. Follow-ups
- Generate the remaining 605 Sonarr pages when a full reference is wanted (about $85 at these rates).
- Re-check the slice rule against 1d part 2's verdict (D45).
- The writer model (Sonnet or Opus) once the owner's hand-read is in (D64).
