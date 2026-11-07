# Roadmap

The build order (decisions.md D19) and how phases stay in step with each other (D20).

**Read this first.** The file an item sits in is its status (D134): decided and unbuilt here, not decided in [`improvements.md`](improvements.md), built or rejected in [`improvements-done.md`](improvements-done.md). Open questions: [`questions.md`](questions.md). Every decision: [`decisions.md`](decisions.md). How a phase is recorded and changed: [`docs/phases.md`](docs/phases.md).

## Where we are (2026-11-05)

- **Built and measured:** the foundation (0), the code graph (1a–1c), reference pages (2a), FSD import and links (2b,
  findings off; PDF import added by D111), and Phase 3: pull, the file finder with code checks, the bundle, the bug proof (advice only), drafts.
- **Not passing:** proof is real, catches wrong reports (needs 2c and a project whose FSD covers those tickets); 2c's links, both bars
  failed (24/30, 44% false alarms); 1d part 2 (deferred).
- **Out of date:** the saved proof numbers predate the finder, D95, D104, D106 and D107 (D109).
- **Overhaul (2026-11-04), local on `claude/phase2c-spec`, not pushed:** slices 0.1 to 2.6 landed, then the whole-flow test and §8's four batches (D120): switches, seams, import report, Excel, attachments, and table comparison, finer verdicts, grouping and ask, built off.
- **Disprove re-score (finished and hand-checked 2026-11-07, $8.59 in all):** false alarms 44% to **23%**, noise bar met; found 24 to 19 of 30, found bar not met. The 7 real findings it took out were all filed `page-gap` with reasons that say `cant-tell`. Login with `claim-links`: rulings move by ids, but the judge raised no gap or conflict (`phases/2c-spec.md`).
- **§9's batch 1 (2026-11-05), local, not pushed:** T.2, T.4, T.5, T.6, T.8, I.5, I.7, I.8 (off), I.9, I.10, I.11 (D126). Every paid step now needs a cap or `none`, so a script or CI needs `STANGENT_MAX_COST`.
- **§9's batch 2 (2026-11-05), local, not pushed:** D130 and J.4 (`schema-once`, off), `shared-candidates` removed, a warning on a hand-edited `document.md`, T.1, T.3, T.7, I.1, I.2, I.3, I.4, I.6 (D131). Fakes only. Off until a test: `schema-once`, `page-corrections`, `page-exclusions`, `follow-moves`, `statement-update`, `disprove-small`.
- **Unchecked:** §8's and §9's batches had no fresh check (D120), and none has met a real FSD or real AI. Next is "What's next" below.
- No private-repo check and no proof run on bitwarden (owner): Phase 3's report says so.
- **The overhaul's order of work is finished** (slices 0.1 to 2.6, D112). It stopped before Phase 4, which needs the owner: the order after 2.x, and the questions in `questions.md`.

## What's next (D125, with the order proposed 2026-11-04)

**Proposed, not decided.** The assistant proposed this and the owner said "ok what we gonna do next"; see `questions.md`. Until the owner confirms it, D120 and D125 stand as written.

1. **Python support, then the links on the owner's own project (D127):** a Python extractor and Flask detector (built 2026-11-05, `phases/1b-frontend-api.md`; `code-graph` v4 grew in place, so nothing needs review), then `cyberbullying-detection-system` a chapter at a time from Chapter 7's use-case sections. The owner no longer works on that project, so its rulings count toward no bar (D137). Each AI step has its cost stated and the owner's OK. Counts only in the repo.
2. **New Word files as proposals a BA or PM checks (D128, owner).** *Section level built by D150, off (`section-approval`): approve or reject per changed section, promote applies only what was approved, `--partial`, a stale candidate names its sections, readable section files, a review to paste into a PR. Left: a real document's two versions, a real BA's review, requirement level, the dashboard (Phase 8).* Was: the parsed spec is the source of truth; change-by-change approval, comparing against a spec that has moved on, and files holding only changes. After the links have run on the owner's project; also a cleaner diff for BAs (generated files collapsed in `.gitattributes`, the spec stays in the app's repo for now). Details still being settled, roughly 150–250K tokens.
3. **The rest of I and T (owner, 2026-11-05):** batches 1 and 2 are built (D126, D131), all with fakes. Six switches are off until a test says otherwise, listed under "Where we are". Held until a real repo or document needs them: T.9, T.10, I.12.
4. **Then the pilots on public C# projects, where Stangent hasn't been tuned** (RealWorld's C# app chosen first by the owner, whose written spec and tests let us test links; then umbraco/Umbraco-CMS, with no FSD): pages, impact, one real ticket. Everything free first; the cost is stated and the owner says yes before any AI step. Results go in a phase file.
5. Make the test material.
6. Start the test phase: the re-score after Nov 7 (the owner chooses how), then on/off runs against bars frozen first.
7. **"Does the bundle help a fix?", moved earlier:** run in the test phase, not left to Phase 4's grill, because Phase 4 is built on the answer. The fix agent on the same bugs three ways (`questions.md`). Paid, on the owner's OK.
8. Grill Phase 4.

**Cut rule (D138):** a component that fails a bar gets one fix attempt. If it fails again, it's removed; one the pipeline can't run without stays, with its output unconfirmed until a person reads it. The disprove re-score is the links' one attempt. No cap on new items (D139).

## Phases

| Phase | Delivers | Consumes contracts from | Provides contracts | Status |
|---|---|---|---|---|
| **0. Foundation** | repo setup, store interface, artifact schema conventions, CLI skeleton, Claude Code plugin skeleton, telemetry event format, benchmark harness | — | `store`, `artifact-base`, `telemetry-event`, `cli-conventions`, `benchmark` | done ([report](phases/0-foundation.md#end-of-phase-report)) |
| **1a. C# map** | Roslyn extractor at method level, C# link detectors, blind-spot markers, impact answers, scoring | 0 | `code-graph`, `impact-report` | done ([report](phases/1a-csharp-map.md#end-of-phase-report)) |
| **1b. Frontend + API shapes** | TypeScript extractor, frontend → backend HTTP links, API request/response fields; proven on CleanArchitecture, Kavita, Sonarr | 0, 1a | — (extends `code-graph` → v2, `impact-report` → v3) | done ([report](phases/1b-frontend-api.md#end-of-phase-report)) |
| **1c. Git history** | a hardened Test 2 first, then 📜 "usually changes together" evidence with noise filters, kept only if it helps; tuned on nopCommerce and Sonarr, judged on bitwarden/server | 0, 1a | — (extends `code-graph` → v3, `impact-report` → v4) | done ([report](phases/1c-git-history.md#end-of-phase-report)) |
| **1d. Agent-fitness pilot** ([plan](phases/1d-agent-fitness.md)) | does the graph do its Phase 4 jobs (D45)? Splitting first (no agents), then context scoping (agents with vs without a graph slice); pre-agreed bars | 0, 1a, 1b, 1c | — | in progress: part 1 reported, safe fails ([report](phases/1d-agent-fitness.md#end-of-phase-report)); part 2 to be grilled or dropped |
| **2a. Reference from code** ([plan](phases/2a-reference.md)) | reference pages generated from the graph per screen, shared component and backend entry point; citations checked, a fresh verifier, kept current per PR; proven on Sonarr (D50–D63, D66, D67) | 0, 1a, 1b, 1c | `reference-page`, `reference-lines`, `page-corrections`, `page-exclusions`, `page-shape` | done ([report](phases/2a-reference.md#end-of-phase-report)) |
| **2b. FSD and discrepancies** ([plan](phases/2b-fsd.md)) | FSD (`.docx`) import, FSD ↔ page links, discrepancy decisions; proven on Sonarr with a planted FSD (D53, D56–D58) | 0, 1a, 2a | `trace-links`, `source-document`, `table-map` (extends `impact-report` → v5) | done, with its noise bar failed: findings off by default (D68) ([report](phases/2b-fsd.md#end-of-phase-report)); a real-FSD dry run (D56) owed. Extended by 2c (D99) |
| **3. Tickets** ([plan](phases/3-tickets.md)) | Jira ticket (through the Atlassian MCP) → context bundle for the developer's Claude Code: starting points, reference pages, FSD sections, impact, attachments; bugs reproduced by a failing unit or Playwright test before a fix; comments drafted by AI, posted by a person; proven on 50 Sonarr issues copied into Jira (D69–D81) | 0, 1a, 2 | `ticket-bundle` (v2), `ticket-update`, `bug-proof` | done, with two bars failed (D109) ([report](phases/3-tickets.md#end-of-phase-report)): the proof ships as advice only, and its numbers predate the finder, D95 and D104, so a re-run is owed. Merged as [#2](https://github.com/hohkokyoung/stangent/pull/2), CI green |
| **2c. Structured spec** ([plan](phases/2c-spec.md), D99, D109) | the FSD becomes checked requirements with stable ids: features, requirements, acceptance criteria, journeys, scenarios, data; every item quotes the FSD word for word; requirement ↔ page statement ↔ code links; the BA approves each in a spec-only PR; `fsd reconcile` across documents; Jira change requests and as-built drafts as sources; verdicts "bug, per R-0042" and "looks intended"; scored on the planted FSD's 30-item key (27/30 or better, false alarms ≤ 30%) | 0, 1a, 2a, 2b, 3 | a new contract beside `source-document` (name not decided) | in progress, paused for an overhaul the owner wants (2026-11-03): key frozen; extraction run on the planted FSD; links run, **both bars failed** (24/30, false alarms 44%); `ticket match` built, not scored; **comes before Phase 4** (owner). Phase 3 goes `needs review` when it lands |
| **4. Agents** | ticket → task breakdown, bounded parallel subagents, D18 checks on every output; graph work from D65, D88 and D109 | 0, 1a, 2, 2c, 3 | `task-plan`, `agent-run` | not started; to be grilled first (Where we are) |
| **5. Testing** | unit, integration, Playwright tests chosen from the graph, tagged with requirement and scenario IDs (D10, D99) | 0, 1a, 2, 2c, 4 | `test-result` | not started |
| **6. PR** | review package for seniors, senior comment layer | 0, 1a, 2, 2c, 4, 5 | `pr-review` | not started |
| **7. CI/CD** | deployment on GitHub Actions: simple, monitorable, retryable | 0, 5, 6 | `deployment` | not started |
| **8. Dashboard** | BA/PM spec view (D12), then the links between everything | all | — | not started |
| **Demo** (any time) | quick interactive prototypes | 0, 2 | `prototype` | not started |

## Decided, not built

So nothing settled lives only in `decisions.md`. Each is built in the phase named.

| What | Decisions | Phase |
|---|---|---|
| A code-reading check answers "is it true?" before a finding reaches a PM, with a way of catching real findings it throws away; false alarms counted from it, not from PM rulings. What does the check isn't settled | D137, D119 | 2c |
| Requirements citing claims, once `claim-links` has been tried with a real judge | D145, D143, D99 | 2c |
| Table facts from constants and SQL seed data, beside API fields and enums; an AI proposing `table-map.yaml` for a person to approve. Both after the owner answers D68's question | D124, D68 | 2b |
| Journeys across sections: `stangent req` builds them within one section, since it sees one section at a time | D99, D129 | 2c |
| Requirement ↔ page statement ↔ code links reaching their bar. Built (`requirements-links`, on) and scored on the planted FSD's key: both bars failed, 24/30 and 44% false alarms against 27/30 and 30%. What's left is X.9's test | D99, D10, D129 | 2c |
| The BA approves each requirement by tick boxes in a spec-only PR; `decided_by` from git | D99, D58 | 2c |
| Spec change states (proposed → verified → implemented, or provisional); developers' changes found later become proposed | D11 | 2c |
| `/stangent:adopt` sets a project up in one run (replaces D14's audit); a stale page's links are judged again | D99 | 2c |
| Several documents with effective dates; `fsd reconcile`, run by CI when an import lands; items carried forward by code; clashes between sections become BA questions | D99, D15 | 2c |
| Jira change requests as sources; as-built drafts when there's no FSD (context only) | D99 | 2c |
| Links and requirements cite a section's permanent id, not its number, once a paid re-run is due anyway | D117 | not placed |
| A renumbered section's finder hash carried too, with the page set loaded at promotion | D117 | not placed |
| A promotion that can't half-finish | D117 | not placed |
| "bug, per R-0042" and "looks intended (R-0042)"; a developer flags a spec gap when confirming a bug | D96, D99 | 2c |
| Test material, bigger spec (owner, 2026-11-05; recommended, not confirmed): `umbraco/UmbracoDocs` beside Umbraco, a chapter at a time, with Markdown import and an answer key from docs-fix PRs | `questions.md` | before the on/off runs |
| Test material: extend the planted FSD (tables, a workbook, dead rules, a comment, a second version); a second planted FSD on Kavita; Sonarr's public wiki as a messy document, licence to check | D125, D44, D56 | before the on/off runs |
| The graph's blind spots, a smarter split rule, `impact-report` v6 | D65, D88, D109 | 4 |
| Parallel work is merged and built together before it counts | D49 | 4 |
| An agent waits for a person's go on a ticket with no proof | D75, D106 | 4 |
| An agent stops and asks when a fix goes against an approved requirement; a diff touching linked code is flagged | D99 | 4 |
| Code PRs reference the ticket and the requirement ids they implement | D11 | 4 |
| Tests carry requirement and scenario ids; coverage per requirement | D10, D99 | 5 |
| A code PR lists the requirements it reaches with no test | D99 | 6 |
| Integration tests with databases or containers | D76 | 5 |
| The review package shows impact, with its 📜 history section | D27, D40 | 6 |
| Security review as its own checked step in the review package, apart from the FSD comparison | D132 | 6 |
| Revert per ticket: a ticket's spec PR and code PRs together, with their links | D11 | not placed; 6 or 7 |
| The CI workflow that builds and shares the graph (full history) | D42 | 7 |
| Telemetry gathered into a SQLite index | D21 | 8 |
| The BA/PM view, editing the same files through the website | D12, D58 | 8 |
| Draft a Jira ticket from a "code-wrong" decision | D80 | later, not placed |
| Installable for others | D1 | later, not placed |

### Held until a repo or document needs it (D125, D120, D4)

- I.12 · A second reader: pandoc reads the same Word file and code compares the words. *In checks only, so saved work holds. Needs pandoc installed; it isn't on this machine.*
- T.9 · Playwright adapter by settings: specs in another repo, the project's own helpers, its URL shape. *No AI. A link records both repos' commits; this is the reason D4 asked for. Generated scripts only if settings can't express it.*
- T.10 · EMF, draw.io, Word cross-references, PowerPoint. *No AI. When a real document needs them.*
  - PDFs and PowerPoint slides converted to text *PDF is built, read directly (D111). PowerPoint isn't decided.*
  - EMF pictures converted to PNG *Saved as `.bin` today. Recommend: do if a real document needs it.*
  - draw.io diagram sources pulled out of images *Nothing reads diagrams. Recommend: do if a real document needs it.*
  - Word cross-references turned into working links *Plain text today. Recommend: later.*

### Off until its test says so (D120, D125)

**The list.** Some are built and off, some not built yet.

- X.1 · Disprove step. *Re-scored and hand-checked 2026-11-07: 23% false alarms, 19 of 30 found.*
- D151 · A page gap only when the code settles it, waiting for a person to confirm. *Built, off: `page-gap-strict`. No AI has read it. Test: the same 62 Sonarr findings asked again (about $7), scored with the merged hand check, counting planted items back and false alarms.*
- X.2 · Page verifier: keep or drop. *Test: Sonarr key, on and off.*
- X.3 · Each rule tested for whether it can trigger. *Test: Sonarr plus planted dead rules.*
- X.4 · A short guide per app. *Test: Sonarr key; a small effect may not show.*
- X.5 · Findings about the code itself. *Test: Sonarr's real bug history; needs new scoring.*
- X.6 · Haiku on small pages. *Test: Sonarr key and a hand read.*
- X.7 · Table comparison by code. *Test: Extended planted FSD, then a real one.*
- X.8 · Finer verdicts, grouping, ask. *Test: Sonarr key, on and off.*
- X.9 · Requirements layer. *Test: Sonarr key, on and off; can't show catching wrong reports. So far: requirement level 24/30, 44% false alarms; section level 27/30, 31.3%; separate runs (D129).*
- X.10 · Git history. *Test: Existing benchmark; about 2.5 points so far.*
- X.11 · Playwright as evidence. *Test: the pilot's project (the owner has none of their own): unresolved rate and a hand check.*
- X.12 · Structured specs before pages. *Test: One feature on Sonarr, about 3M tokens.*
- X.13 · UI check per screen. *Test: Same run.*
- X.14 · Reviewer per journey. *Test: Same run.*
- X.15 · Passes over finished pages. *Test: Same run.*
- X.16 · Gap finders. *Test: Sonarr key; the only idea that can lift found above 24 of 30.*
- X.17 · Pictures read by the agent. *Test: A real document; a person confirms each claim.*
- X.18 · Overview pages and a glossary. *Test: Sonarr key; may help names that differ.*
- X.19 · Generated per-app adapter scripts. *Test: Only where settings can't express a pattern.*

**Also built and off.**

- A.7 · Embedded files converted, linked from their section, and their text checked with it. A locked or scanned one is kept and listed. *Built (D123). The text stays out of `document.md`, so saved hashes hold. The check seeing them is `fsd.attachmentsInChecks`, off by default; turning it on re-runs sections that gain text, which is paid, and hasn't been run on a real AI.*
- C.2 · Compare table facts by code: fee tables, field lists, status codes against facts from the code. *Built, off, not shown to help (`fsd.compareTables`, D124). `fsd tables`, `table-map.yaml`, `fsd compare-tables`, with API fields and enums from C# and TypeScript, by code. D68's question is still the owner's. Hand-built documents only: there's no real FSD to pilot it on. Constants and seed data aren't done.*
- C.4 · The link judge can say `partial` and `unclear`. *Built, off, not shown to help (`fsd.judgeFinerVerdicts`, D124). New instruction text and schemas; every reader and the scorer handle both. Whether it cuts false alarms needs the paid re-score.*
- C.6 · Group small neighbouring sections into one unit when checking. *Built, off, not shown to help (`fsd.groupSections`, D124). A run under one parent, up to `fsd.groupMaxChars` (4000, stated, with no measurement behind it). Each link cites its own section, by code. It has its own hash: on re-runs, off reuses.*
- E.2 · Ask "how does the app do X today?", answered from pages with citations. *Built, off, not shown to help (`spec.ask`, D124). `stangent spec ask` and `/stangent:ask`: code picks pages by words, one turn, no tools; every id checked by code, lines from `reference-lines`, and an answer with no citations is refused.*
- I.2 · Update only the statements a change touches, not the whole page. *Built, off: `statement-update`. The writer still reads the whole bundle; no real run yet (D131).*
- I.3 · Smaller disprove bundles, watching `cant-tell`. *Built, off: `disprove-small`; `fsd disprove estimate` shows the size and the `cant-tell` count. Whether `cant-tell` goes up isn't known until a paid run (D131). 44 of 62 hit the 60K cap.*
- I.4 · Citations follow code that only moved. *Built, off: `follow-moves`. A renamed TypeScript file changes its ids, so it still runs (D131).*
- I.8 · Files attached to a PDF get a home. *Built, off: `attachment-home`. Off because such a document gains a section (D126).*
- D140 · A section's candidates reach shared components through a screen's unshared ones. *Built, off, not shown to help: `shared-through`. Fakes and counts only. Test: the link judge on Login and other use cases, on and off, with the checker on; paid, on the owner's OK. It's this component's one fix attempt (D138).*
- D145 · Claims get permanent `C-` ids, a judge names ids and copies no words, a link's id comes from ids, and old rulings carry. *Built, off, not shown to help: `claim-links`, needs `fsd-claims`. No judge has read it. Test: a real judge on a section with rulings, judged twice; the first run re-judges what it reaches.*
- D144 · One readable file per flow under `specs/workflows/`, with a count per use case. *Built, off, not shown to help: `workflow-files`, `stangent workflows`, needs `claim-links` (D147). Written by code, read by nothing. On the test app all 87 flows are `not judged`, since no real judge has used `claim-links`. Test: a person reads them after a real judge has run.*
- D146 · A disprove bundle holds an endpoint's frontend callers, and says what it can't show is `cant-tell`. *Built, off, not shown to help: `disprove-callers`. No AI has read it. Test: a real disprove run on findings about endpoints, comparing `stands` and `cant-tell` on and off. Its grouping and joining changes sit under `claim-links` and `fsd-claims`.*
- J.1 · What each Playwright test walks through, in order, under `specs/workflows/tests/`. *Built, off, not shown to help: `test-journeys`, `stangent workflows tests`, no AI (D129, D149). It compares nothing. Test: a person reads eShop's, and a use-case run says whether they help. On Actual 67% of steps are ❓, and no goto has reached a real reference page yet.*
- D.1 · Section-level approval of a new FSD version (D128): approve or reject per changed section, `--partial`, readable section files under `specs/fsd/<doc>/sections/` (D142). *Built, off: `section-approval`, no AI (D150). Test: two real versions of one FSD reviewed by a real BA or PM, with the section files read in a PR.*
- D141 · An AI flag on findings that touch security, sent to a developer and open until a person answers. *Built, off, not shown to help: `security-flag`, needs `disprove`, `trace security` (D148). Not a security review: it only looks at findings. No AI has read it, and turning it on re-asks the disprover, which is paid. Test: a real disprove run on findings a person has read, counting false flags and flags missed, with `stands` always suggesting a severity.*
- D143 · A section's text cut into claims by code. *Built, off, not shown to help: `fsd-claims`, `fsd claims`. Prints only; `claim-links` (D145) builds on the same cut. Counts only, no AI. Test: a person reads the output.*
- T.1 · A person's correction to a page is recorded, approved and survives regeneration. *Built, off: `spec correct`, `page-corrections`. Changes or removes a statement; adding one is still `page-wrong` (D131).*
- T.3 · Every screen and entry point is covered, or excluded with a reason. *Built: `spec coverage`; `spec exclude`, off: `page-exclusions` (D131).*
- J.4 · Batch runs send the output schema only once (D130). *Built, off: `schema-once`. The hash stays on the full text, so nothing regenerates; off until a test, because what the AI reads changes (D131).*

## Checks owed

**Checks owed:** every build under D120 (no fresh check while building); the hard stop, `stream-json` on one-turn calls and the read-only hook against real Claude Code, on the first paid run (D126); the proof re-run (D109); a real-FSD dry run (D56); 1d part 2, context scoping (D45); 2b's noise re-measure (D68); hand
checks on 1b, 1c (D28), 2a, 2b (D67); a third repo of another shape (D94); a sign-in approved with only D82's scopes.
