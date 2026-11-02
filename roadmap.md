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
| **2a. Reference from code** ([plan](phases/2a-reference.md)) | reference pages generated from the graph per screen, shared component and backend entry point; citations checked, a fresh verifier, kept current per PR; proven on Sonarr (D50–D63, D66, D67) | 0, 1a, 1b, 1c | `reference-page` | done ([report](phases/2a-reference.md#end-of-phase-report)) |
| **2b. FSD and discrepancies** ([plan](phases/2b-fsd.md)) | FSD (`.docx`) import, FSD ↔ page links, discrepancy decisions; proven on Sonarr with a planted FSD (D53, D56–D58) | 0, 1a, 2a | `trace-links`, `source-document` (extends `impact-report` → v5) | done, with its noise bar failed: findings off by default (D68) ([report](phases/2b-fsd.md#end-of-phase-report)); a real-FSD dry run (D56) owed. Extended by 2c (D99) |
| **3. Tickets** ([plan](phases/3-tickets.md)) | Jira ticket (through the Atlassian MCP) → context bundle for the developer's Claude Code: starting points, reference pages, FSD sections, impact, attachments; bugs reproduced by a failing unit or Playwright test before a fix; comments drafted by AI, posted by a person; proven on 50 Sonarr issues copied into Jira (D69–D81) | 0, 1a, 2 | `ticket-bundle` (v2), `ticket-update`, `bug-proof` | done, with two bars failed (D109) ([report](phases/3-tickets.md#end-of-phase-report)): right place Sonarr 90%, bitwarden held-out 88% ✅, beats keywords ✅, grounded ✅, repeatable ✅, Jira ✅, proof finds bugs 73% ✅; proof real 77% ❌, wrong reports 7/10 ❌, so the proof ships as advice only. Proof numbers predate the finder, D95, D104; re-run owed. On branch `claude/phase3-build-f26b00`, not yet merged into `main`, so CI hasn't run it |
| **2c. Structured spec** (D99, D109) | the FSD becomes checked requirements with stable ids: features, requirements, acceptance criteria, journeys, scenarios, data; every item quotes the FSD word for word; requirement ↔ page statement ↔ code links; the BA approves each in a spec-only PR; `fsd reconcile` across documents; Jira change requests and as-built drafts as sources; verdicts "bug, per R-0042" and "looks intended"; scored on the planted FSD's 30-item key (27/30 or better, false alarms ≤ 30%) | 0, 1a, 2a, 2b, 3 | a new contract beside `source-document` (name not decided) | not started; decided and designed, **comes before Phase 4** (owner); first: a checker for matching a ticket to its requirement. Phase 3 goes `needs review` when it lands |
| **4. Agents** | ticket → task breakdown, bounded parallel subagents, D18 checks on every output; graph work from D65, D88 and D109 | 0, 1a, 2, 2c, 3 | `task-plan`, `agent-run` | not started; to be grilled first (Where we are) |
| **5. Testing** | unit, integration, Playwright tests chosen from the graph, tagged with requirement and scenario IDs (D10, D99) | 0, 1a, 2, 2c, 4 | `test-result` | not started |
| **6. PR** | review package for seniors, senior comment layer | 0, 1a, 2, 4, 5 | `pr-review` | not started |
| **7. CI/CD** | deployment on GitHub Actions: simple, monitorable, retryable | 0, 5, 6 | `deployment` | not started |
| **8. Dashboard** | BA/PM spec view (D12), then the links between everything | all | — | not started |
| **Demo** (any time) | quick interactive prototypes | 0, 2 | `prototype` | not started |

## Where we are (2026-11-02)

- **Built and measured:** the foundation (0), the code graph (1a–1c), reference pages (2a), FSD import and links (2b,
  findings off), and Phase 3: pull, the file finder with code checks, the bundle, the bug proof (advice only), drafts.
- **Not passing:** proof is real, catches wrong reports (needs 2c and a project whose FSD covers those tickets); 1d part 2 (deferred).
- **Out of date:** the saved proof numbers predate the finder, D95, D104, D106 and D107 (D109).
- **Next, in order (D109):**
  1. Merge the branch, so CI runs (owner's OK).
  2. **Phase 2c:** grill it (D22), first the ticket → requirement checker and the spec recheck triggers, then build it.
  3. The proof re-run on Sonarr, with drafts and replay after it to re-confirm grounded and repeatable (at most about $45, owner's OK).
  4. **Phase 4:** grill it, first "does the bundle help a fix?" (the fix agent with and without it on the same bugs), then build it.
- No private-repo check and no proof run on bitwarden (owner): Phase 3's report says so.

## Decided, not built

So nothing settled lives only in `decisions.md`. Each is built in the phase named.

| What | Decisions | Phase |
|---|---|---|
| Features, requirements, acceptance criteria, journeys, scenarios and data, with stable ids; `stangent spec req <id>` | D99, D10, D11 | 2c |
| Requirement ↔ page statement ↔ code links, scored on the planted FSD's key | D99, D10 | 2c |
| The BA approves each requirement by tick boxes in a spec-only PR; `decided_by` from git | D99, D58 | 2c |
| Spec change states (proposed → verified → implemented, or provisional); developers' changes found later become proposed | D11 | 2c |
| Several documents with effective dates; `fsd reconcile`, run by CI when an import lands | D99, D15 | 2c |
| Jira change requests as sources; as-built drafts when there's no FSD (context only) | D99 | 2c |
| "bug, per R-0042" and "looks intended (R-0042)"; a developer flags a spec gap when confirming a bug | D96, D99 | 2c |
| The graph's blind spots, a smarter split rule, `impact-report` v6 | D65, D88, D109 | 4 |
| Parallel work is merged and built together before it counts | D49 | 4 |
| An agent waits for a person's go on a ticket with no proof | D75, D106 | 4 |
| Code PRs reference the ticket and the requirement ids they implement | D11 | 4 |
| Tests carry requirement and scenario ids; coverage per requirement | D10, D99 | 5 |
| Integration tests with databases or containers | D76 | 5 |
| The review package shows impact, with its 📜 history section | D27, D40 | 6 |
| Revert per ticket: a ticket's spec PR and code PRs together, with their links | D11 | not placed; 6 or 7 |
| The CI workflow that builds and shares the graph (full history) | D42 | 7 |
| Telemetry gathered into a SQLite index | D21 | 8 |
| The BA/PM view, editing the same files through the website | D12, D58 | 8 |
| Draft a Jira ticket from a "code-wrong" decision | D80 | later, not placed |
| Installable for others | D1 | later, not placed |

**In the owner's brief, with no decision yet:** security review (Open questions). D14's full audit on adoption predates D51–D52 and needs
re-deciding with 2c.

**Checks owed:** the proof re-run (D109); a real-FSD dry run (D56); 1d part 2, context scoping (D45); 2b's noise re-measure (D68); hand
checks on 1b, 1c (D28), 2a, 2b (D67); a third repo of another shape (D94); a sign-in approved with only D82's scopes.

Status values: `not started` · `planned` · `in progress` · `done` · `needs review` (an upstream contract changed after this phase was done).

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
2. **Benchmark results.** On nopCommerce, eShop and CleanArchitecture (D6), compared with the last report.
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
