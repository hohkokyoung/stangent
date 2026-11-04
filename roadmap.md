# Roadmap

The build order (decisions.md D19) and how phases stay in step with each other (D20).

## Phases

| Phase | Delivers | Consumes contracts from | Provides contracts | Status |
|---|---|---|---|---|
| **0. Foundation** | repo setup, store interface, artifact schema conventions, CLI skeleton, Claude Code plugin skeleton, telemetry event format, benchmark harness | — | `store`, `artifact-base`, `telemetry-event`, `cli-conventions`, `benchmark` | done ([report](phases/0-foundation.md#end-of-phase-report)) |
| **1a. C# map** | Roslyn extractor at method level, C# link detectors, blind-spot markers, impact answers, scoring | 0 | `code-graph`, `impact-report` | done ([report](phases/1a-csharp-map.md#end-of-phase-report)) |
| **1b. Frontend + API shapes** | TypeScript extractor, frontend → backend HTTP links, API request/response fields; proven on CleanArchitecture, Kavita, Sonarr | 0, 1a | — (extends `code-graph` → v2, `impact-report` → v3) | done ([report](phases/1b-frontend-api.md#end-of-phase-report)) |
| **1c. Git history** | a hardened Test 2 first, then 📜 "usually changes together" evidence with noise filters, kept only if it helps; tuned on nopCommerce and Sonarr, judged on bitwarden/server | 0, 1a | — (extends `code-graph` → v3, `impact-report` → v4) | done ([report](phases/1c-git-history.md#end-of-phase-report)) |
| **1d. Agent-fitness pilot** ([plan](phases/1d-agent-fitness.md)) | does the graph do its Phase 4 jobs (D45)? Splitting first (no agents), then context scoping (agents with vs without a graph slice); pre-agreed bars | 0, 1a, 1b, 1c | — | in progress: part 1 reported, safe fails ([report](phases/1d-agent-fitness.md#end-of-phase-report)); part 2 to be grilled or dropped |
| **2a. Reference from code** ([plan](phases/2a-reference.md)) | reference pages generated from the graph per screen, shared component and backend entry point; citations checked, a fresh verifier, kept current per PR; proven on Sonarr (D50–D63, D66, D67) | 0, 1a, 1b, 1c | `reference-page`, `reference-lines` | done ([report](phases/2a-reference.md#end-of-phase-report)) |
| **2b. FSD and discrepancies** ([plan](phases/2b-fsd.md)) | FSD (`.docx`) import, FSD ↔ page links, discrepancy decisions; proven on Sonarr with a planted FSD (D53, D56–D58) | 0, 1a, 2a | `trace-links`, `source-document` (extends `impact-report` → v5) | done, with its noise bar failed: findings off by default (D68) ([report](phases/2b-fsd.md#end-of-phase-report)); a real-FSD dry run (D56) owed. Extended by 2c (D99) |
| **3. Tickets** ([plan](phases/3-tickets.md)) | Jira ticket (through the Atlassian MCP) → context bundle for the developer's Claude Code: starting points, reference pages, FSD sections, impact, attachments; bugs reproduced by a failing unit or Playwright test before a fix; comments drafted by AI, posted by a person; proven on 50 Sonarr issues copied into Jira (D69–D81) | 0, 1a, 2 | `ticket-bundle` (v2), `ticket-update`, `bug-proof` | done, with two bars failed (D109) ([report](phases/3-tickets.md#end-of-phase-report)): right place Sonarr 90%, bitwarden held-out 88% ✅, beats keywords ✅, grounded ✅, repeatable ✅, Jira ✅, proof finds bugs 73% ✅; proof real 77% ❌, wrong reports 7/10 ❌, so the proof ships as advice only. Proof numbers predate the finder, D95, D104; re-run owed. Merged as [#2](https://github.com/hohkokyoung/stangent/pull/2), CI green |
| **2c. Structured spec** ([plan](phases/2c-spec.md), D99, D109) | the FSD becomes checked requirements with stable ids: features, requirements, acceptance criteria, journeys, scenarios, data; every item quotes the FSD word for word; requirement ↔ page statement ↔ code links; the BA approves each in a spec-only PR; `fsd reconcile` across documents; Jira change requests and as-built drafts as sources; verdicts "bug, per R-0042" and "looks intended"; scored on the planted FSD's 30-item key (27/30 or better, false alarms ≤ 30%) | 0, 1a, 2a, 2b, 3 | a new contract beside `source-document` (name not decided) | in progress, paused for an overhaul the owner wants (2026-11-03): key frozen; extraction run on the planted FSD; links run, **both bars failed** (24/30, false alarms 44%); `ticket match` built, not scored; **comes before Phase 4** (owner). Phase 3 goes `needs review` when it lands |
| **4. Agents** | ticket → task breakdown, bounded parallel subagents, D18 checks on every output; graph work from D65, D88 and D109 | 0, 1a, 2, 2c, 3 | `task-plan`, `agent-run` | not started; to be grilled first (Where we are) |
| **5. Testing** | unit, integration, Playwright tests chosen from the graph, tagged with requirement and scenario IDs (D10, D99) | 0, 1a, 2, 2c, 4 | `test-result` | not started |
| **6. PR** | review package for seniors, senior comment layer | 0, 1a, 2, 2c, 4, 5 | `pr-review` | not started |
| **7. CI/CD** | deployment on GitHub Actions: simple, monitorable, retryable | 0, 5, 6 | `deployment` | not started |
| **8. Dashboard** | BA/PM spec view (D12), then the links between everything | all | — | not started |
| **Demo** (any time) | quick interactive prototypes | 0, 2 | `prototype` | not started |

## Where we are (2026-11-04)

- **Built and measured:** the foundation (0), the code graph (1a–1c), reference pages (2a), FSD import and links (2b,
  findings off; PDF import added by D111), and Phase 3: pull, the file finder with code checks, the bundle, the bug proof (advice only), drafts.
- **Not passing:** proof is real, catches wrong reports (needs 2c and a project whose FSD covers those tickets); 2c's links, both bars
  failed (24/30, 44% false alarms); 1d part 2 (deferred).
- **Out of date:** the saved proof numbers predate the finder, D95, D104, D106 and D107 (D109).
- **Overhaul:** slices 0.1 to 2.6 landed locally on `claude/phase2c-spec` (2026-11-04), not pushed. The disprove step isn't shown to help until the re-score runs (about $4–6, the owner's OK), and it can't raise links found above 24 of 30, so 2c's 27 of 30 bar needs something else as well. Its order is below and replaces D109's "next" list until the owner says otherwise.
- No private-repo check and no proof run on bitwarden (owner): Phase 3's report says so.

## The overhaul: order of work

The one place for what comes next (D112). From the foundation up, applying `improvements.md`. Stops before Phase 4. ✅ when landed.

| # | Slice | What | Decisions | Needs the owner? |
|---|---|---|---|---|
| 0.1 ✅ | Evidence shape | One `evidence` contract, a Core type and validator: permanent id, citation, commit, strength, who or what checked it. Consumers move in 2.x | D18, D53, D62 | no |
| 0.2 ✅ | State changes | One Core module: a table of legal moves per kind, rejects the rest, keeps a history. Real lifecycles move onto it; result fields don't | D18, D58, D60 | no |
| 0.3 ✅ | Client-data check | `stangent data check`: terms kept outside the repo, tracked documents, run in CI | D56, D16 | no |
| 0.4 ✅ | Coverage measured | A script and a recorded number, no floor (D28) | D28 | a floor, later |
| 0.5 ✅ | Driver tests | The bash drivers tested from xUnit with fake `claude` and `stangent` on the path | D110 | no |
| 1.1 ✅ | Line citations | The graph answers symbol → `path:lines` at a commit, for 2.3 | D17, D25 | no |
| 1.2 ✅ | Playwright as evidence | Specs that open a screen become labelled links in the graph; `code-graph` bump if the shape changes | D45, D76, D86 | no |
| 2.1 ✅ | Word fails loudly | Tracked changes, moves, hidden text, comments, footnotes, embedded files, nested tables and vertical merges each stop the import or are listed | D52, D111 | no |
| 2.2 ✅ | Stable section ids | Ids that survive edits and moves; a new version comes in as a candidate with a section diff; doubtful matches go to a person; removed ids kept | D15, D99 | no |
| 2.3 ✅ | Stable statement ids | Ids never renumbered or reused; `path:lines` citations; commit stamped | D62, D54 | no |
| 2.4 ✅ | Findings carry evidence | Findings use 0.1 and 0.2; severity from one scale | D53, D68 | no |
| 2.5 ✅ | Disprove step | A second agent tries to refute every finding, reversing D62's "rejected"; page gaps go to developers; a "page is wrong" decision; an AI suggestion for a finding's severity (D118) | D62, D99, D58 | built and off by default; **the re-score is paid: prepared, not run** (estimate and command in `phases/2c-spec.md`) |
| 2.6 ✅ | 2c's leftovers | Only if they still apply after 2.5: `LinkRun.cs` review items, "no code found" on 56 of 109 | D99 | no paid run needed: three defects fixed; "no code found" is the failed links, not a bug |

Stop there. Phase 4 needs the owner: the order after 2.x, and the questions under "Open questions" in `decisions.md`.

**Follow-ups from 1.1 and 1.2 (D20, D115).** Done 2026-11-04: `code-graph` went to v4, and 1b, 1c, 1d, 2a, 2b, 2c and 3 were each checked by grep, tests re-run, and moved to `code-graph: 4`. Their code wasn't touched.

## Decided, not built

So nothing settled lives only in `decisions.md`. Each is built in the phase named.

| What | Decisions | Phase |
|---|---|---|
| Features, requirements, acceptance criteria, journeys, scenarios and data, with stable ids; `stangent spec req <id>` | D99, D10, D11 | 2c |
| Requirement ↔ page statement ↔ code links, scored on the planted FSD's key | D99, D10 | 2c |
| The BA approves each requirement by tick boxes in a spec-only PR; `decided_by` from git | D99, D58 | 2c |
| Spec change states (proposed → verified → implemented, or provisional); developers' changes found later become proposed | D11 | 2c |
| `/stangent:adopt` sets a project up in one run (replaces D14's audit); a stale page's links are judged again | D99 | 2c |
| Several documents with effective dates; `fsd reconcile`, run by CI when an import lands; items carried forward by code; clashes between sections become BA questions | D99, D15 | 2c |
| Jira change requests as sources; as-built drafts when there's no FSD (context only) | D99 | 2c |
| Links and requirements cite a section's permanent id, not its number, once a paid re-run is due anyway | D117 | not placed |
| A renumbered section's finder hash carried too, with the page set loaded at promotion | D117 | not placed |
| A promotion that can't half-finish | D117 | not placed |
| Excel import, by code, listing what can't be brought in exactly (PDF is built) | D111, D15, D72 | not placed; after 2.1, once its questions are grilled |
| "bug, per R-0042" and "looks intended (R-0042)"; a developer flags a spec gap when confirming a bug | D96, D99 | 2c |
| The graph's blind spots, a smarter split rule, `impact-report` v6 | D65, D88, D109 | 4 |
| Parallel work is merged and built together before it counts | D49 | 4 |
| An agent waits for a person's go on a ticket with no proof | D75, D106 | 4 |
| An agent stops and asks when a fix goes against an approved requirement; a diff touching linked code is flagged | D99 | 4 |
| Code PRs reference the ticket and the requirement ids they implement | D11 | 4 |
| Tests carry requirement and scenario ids; coverage per requirement | D10, D99 | 5 |
| A code PR lists the requirements it reaches with no test | D99 | 6 |
| Integration tests with databases or containers | D76 | 5 |
| The review package shows impact, with its 📜 history section | D27, D40 | 6 |
| Revert per ticket: a ticket's spec PR and code PRs together, with their links | D11 | not placed; 6 or 7 |
| The CI workflow that builds and shares the graph (full history) | D42 | 7 |
| Telemetry gathered into a SQLite index | D21 | 8 |
| The BA/PM view, editing the same files through the website | D12, D58 | 8 |
| Draft a Jira ticket from a "code-wrong" decision | D80 | later, not placed |
| Installable for others | D1 | later, not placed |

**In the owner's brief, with no decision yet:** security review (Open questions).

**Checks owed:** every build under D120 (no fresh check while building); the proof re-run (D109); a real-FSD dry run (D56); 1d part 2, context scoping (D45); 2b's noise re-measure (D68); hand
checks on 1b, 1c (D28), 2a, 2b (D67); a third repo of another shape (D94); a sign-in approved with only D82's scopes.

Status values: `not started` · `planned` · `in progress` · `done` · `needs review` (an upstream contract changed after this phase was done).

## Proposed, not decided

Every ⬜ and 🟡 item in `improvements.md`, with what it touches. The owner hasn't confirmed the list, so nothing here is a commitment until it moves to the order above or to "Decided, not built". Marks are as they stood before the overhaul; `improvements.md` holds the current ones. "Slice" points at the order of work. "Owner" means it waits on a question only the owner can settle.

| § | Item | Mark at start | Touches | Slice |
|---|---|---|---|---|
| 1 | Permanent id and `path:lines` per statement | ✅ | 2a, D62, D118 | done (2.3) |
| 1 | Ids never renumbered or reused | ✅ | 2a, D62, D118 | done (2.3) |
| 1 | Pages follow journeys | ⬜ | 2a, D54, D63 | Owner |
| 1 | Shared behaviour written once | 🟡 | 2a, D54 | Owner, with page shape |
| 1 | Today, required and findings kept apart, with ids | ✅ | 2a, 2b, D52, D118 | done (2.4); "a new app" isn't Stangent's job |
| 1 | Severity from one scale | ✅ | 2b, D68, D118 | done (2.4), set by a person; the AI's suggestion built (2.5), shown only |
| 1 | Second agent disproves findings; gap finders | 🟡 | 2b, D62, D119 | disprove built, off, not shown to help until the paid re-score (2.5); gap finders still to do |
| 1 | Call graph: every entry point on real C# | 🟡 | 1a, 4, D109 | Phase 4 (decided) |
| 1 | Adapters: Playwright, Razor, Svelte, SQL seed | 🟡 | 1b, D31, D65, D86 | 1.2 for Playwright; rest when a repo needs them |
| 1 | Generated navigation and numbering | 🟡 | 2a | after 2.3 |
| 1 | Script checks, including client data | 🟡 | 0, D18 | 0.3 |
| 1 | Client code read-only by hook | 🟡 | 3, D60, D75, D86 | not placed; analysis steps only |
| 1 | Runs reproducible, measured by a fresh session | ⬜ | 2a, D57, D79 | after 2.3; paid |
| 2 | Updates by overlapping lines | 🟡 | 2a, D55 | after 2.3 |
| 2 | Changed code no page covers is listed | ⬜ | 2a, D55 | after 2.3 |
| 2 | Stamp per repo for split apps | 🟡 | D4 | only if a real target is split |
| 3 | Fails loudly | ✅ | 2b, D111, D116 | done (2.1) |
| 3 | Messy Word files (parent item) | 🟡 | 2b, D111, D116 | 2.1 done; embedded Excel, EMF, draw.io, cross-references remain |
| 3 | Tracked changes including moves | ✅ | 2b, D116 | done (2.1) |
| 3 | Vertical merges expanded | ✅ | 2b, D116 | done (2.1); merges across columns stay blank |
| 3 | Embedded Excel per sheet | ⬜ | 2b, D111 | with Excel; listed since 2.1 |
| 3 | PDF and PowerPoint | 🟡 | D111 | PowerPoint not decided |
| 3 | EMF to PNG | ⬜ | 2b | if a real document needs it |
| 3 | draw.io sources | ⬜ | 2b | if a real document needs it |
| 3 | Word cross-references as links | ⬜ | 2b | later |
| 3 | Lists everything not brought in exactly | ✅ | 2b, D111, D116 | done (2.1); not yet measured on a file saved by Word |
| 3 | Word comments become BA questions | 🟡 | 2b, D58, D116 | listed since 2.1; becoming questions still to do |
| 4 | Section ids survive edits | ✅ | 2b, 2c, D15, D99, D117 | done (2.2); a split or merge is a person's call |
| 4 | Candidate versions with a section diff | ✅ | 2b, D15, D117 | done (2.2) |
| 4 | Proven on a real version change | ⬜ | D56 | needs a document with two versions |
| 4 | First-run errors pinned as tests | 🟡 | 2b, D117 | a reviewed pair is pinned (2.2); no real first run yet |
| 5 | BAs change the FSD through Claude | ⬜ | D52, D12 | Owner |
| 5 | BAs never touch git | ⬜ | D99, D12 | Owner |
| 5 | A branch per BA, every edit a commit | ⬜ | D12 | Owner |
| 5 | Done is one step | ⬜ | D12 | Owner |
| 5 | Lead BA approves or sends back | 🟡 | D99 | Owner |
| 5 | Approval safety rules (parent item) | 🟡 | D99, D58 | Owner |
| 5 | Can't approve your own change | ⬜ | D99 | Owner |
| 5 | Approval never overwrites a later change | 🟡 | D58 | Owner |
| 5 | Approving asks for confirmation | ⬜ | D99 | Owner |
| 5 | Audit trail: who asked, who approved | 🟡 | D58 | Owner |
| 5 | Clashes between BAs | ⬜ | D12 | Owner |
| 5 | One command for what's next | ⬜ | plugin | Owner |
| 5 | One-line installer, pinned version | ⬜ | D1 | when BAs become users |
| 6 | Check against code or against the reference | 🟡 | 2b, D99, D119 | built, off (2.5): code first, unscored |
| 6 | Finder never verifies; fresh re-check | 🟡 | D62, D119 | built, off (2.5), unscored |
| 6 | Stamped with commit and evidence strength | ✅ | D53, 0, D118 | done (2.4) |
| 6 | Unverified until code confirms | 🟡 | D53, D68, D119 | built, off (2.5), unscored |
| 6 | "The reference is wrong" decision | ✅ | D58, D119 | done (2.5) |
| 6 | Only what changed is re-checked | 🟡 | D18, D58, D99 | 2.6 stopped one re-paid judge; a stale page's links still aren't judged again (2c step 6) |
| 6 | Reference gaps go to a developer | 🟡 | D99, D119 | built, off (2.5): sorted by the unscored step |
| 6 | Per-chapter report | ⬜ | 2b, D118 | still to do: 2.4 shows ids and severity in `trace check` only |
| 6 | Alias map for names | ⬜ | 2b | if real documents need it |
| 6 | Trial result (12 of 12) | ⬜ | D99 | a result, not a feature; the owner's denominator is open |
| 7 | All state changes through one module | ⬜ | 0, 2, 3, D18 | 0.2 |
| 7 | Every requirement traced to a test | ⬜ | 5, D10 | Phase 5 |
| 7 | Coverage floor, real tools | 🟡 | 0, D28 | 0.4 for measuring |
| 7 | No client data in the tool's repo | ⬜ | 0 | 0.3 |
| 7 | Hooks never block, tested | ⬜ | plugin | only if hooks are added |
| 8 | A test of the whole flow with fake AI answers | ✅ | 0, 2 | S.0; `WholeFlowTests.cs` |
| 8 | Every component has a switch, and a run says which were on | ✅ | all, D120, D121 | F.1 |
| 8 | Links store a section's permanent id as well as its number | ✅ | 2b, D117 | S.1 |
| 8 | Import problems become structured | ✅ | 2b, D111, D116 | S.2 |
| 8 | One set of import steps every format fills in, and ticket attachments use the same path | ✅ | 2b, 3, D72, D111 | S.3 |
| 8 | Connect or plainly label what Phase 1 and 0 built and nothing uses | ⬜ | 0, 1, 3, D115 | S.4 |
| 8 | Re-read the same file with a newer build, through the candidate flow | ⬜ | 2b, D117 | A.1 |
| 8 | One readable import report with a verdict | ⬜ | 2b, D111 | A.2 |
| 8 | Sensitivity labels stop the import, and a locked Word file gets its own stop with plain in | ⬜ | 2b, D111 | A.3 |
| 8 | When a Word file has no headings, list the bold lines that look like headings | ⬜ | 2b, D111 | A.4 |
| 8 | Excel follows the same steps | ⬜ | D111 | A.5 |
| 8 | Ids for tables, pictures and attachments, kept in `document.json` | ⬜ | 2b, D113 | A.6 |
| 8 | Embedded files converted, linked from their section, and their text checked with it. A loc | ⬜ | 2b, 2c, D53, D111 | A.7 |
| 8 | A section's fingerprint includes its attachments | ⬜ | 2b, D117 | A.8 |
| 8 | A person approves the first import, in a spec-only PR | ⬜ | 2b, D99 | A.9 |
| 8 | `fsd outline` and `fsd show <section>` | ⬜ | 2b | A.10 |
| 8 | `fsd export` | ⬜ | 2b | A.11 |
| 8 | Every section is linked or marked "not in code" by a person, with its attachments listed | ⬜ | 2b, 2c, D53 | C.1 |
| 8 | Compare table facts by code | ⬜ | 1, 2b, D68, D56 | C.2 |
| 8 | A decided finding becomes `recheck` or `gone` | ⬜ | 2b, D58, D118 | C.3 |
| 8 | The link judge can say `partial` and `unclear` | ⬜ | 2b, 2c, D53 | C.4 |
| 8 | BA decisions `accepted` and `not a difference` | ⬜ | 2b, D58 | C.5 |
| 8 | Group small neighbouring sections into one unit when checking | ⬜ | 2b, 2c | C.6 |
| 8 | A status for a document | ⬜ | plugin, 2b | E.1 |
| 8 | Ask "how does the app do X today?", answered from pages with citations | ⬜ | plugin, 2a | E.2 |
| 8 | Run one chapter and see its cost before the whole document | ⬜ | plugin, 2b | E.3 |

**Order for §8 (D120, build first, test later):** F.1 and S.1 to S.4; then A.1 to A.4, A.6, A.9 to A.11, C.1, C.3, C.5, E.1, E.3; then A.5, A.7, A.8; then C.2, C.4, C.6, E.2, built switched off. **Earlier suggestion:** S.0, then S.1 and S.2, then S.3, then Excel and attachments (A.5 to A.8), then C.1, then the C.2 pilot once its decision is made. The re-score comes before C.4 and C.6. Other A and E items fit in where they're small.

## Phase files

Each phase gets `phases/<n>-<name>.md` when it starts. Front matter declares the contract
versions it consumes and provides — this is what the dependency check reads:

```yaml
---
phase: 2
name: spec
status: in progress
consumes: { store: 1, artifact-base: 1, code-graph: 1 }
provides: { spec: 1, trace-links: 1, source-document: 1, discrepancy: 1 }
---
```

The body holds the phase's plan and, once it ends, its **end-of-phase report**.

## End-of-phase report

Written when a phase ends, and again whenever the phase is reopened and changed:

1. **What was built.** Commands, files, contracts (with versions).
2. **Benchmark results.** On the repos the phase is measured on, from D6's six (nopCommerce, eShop, CleanArchitecture, Kavita, bitwarden/server, Sonarr), compared with the last report.
3. **Decisions made or changed.** With links to `decisions.md` entries.
4. **Known gaps.** What doesn't work yet, and the blind spots.
5. **Impact on other phases.** Contracts that changed, and which phases consume them.
6. **Follow-ups.** Work handed to later phases.

## When a finished phase changes

1. Reopen its phase file, make the change, and bump the version of any contract whose shape changed.
2. A check (built in Phase 0) compares every phase's `consumes` against the current
   `provides` versions and lists each phase still on an old version.
3. Those phases are marked `needs review` here, each gets a follow-up task, and the change's
   report lists them. Downstream code is never changed automatically: each consumer is updated
   and re-tested by its own follow-up.
4. A phase leaves `needs review` only when its `consumes` versions are current and its tests pass.
