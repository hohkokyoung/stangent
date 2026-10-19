# Roadmap

The build order (decisions.md D19) and how phases stay in step with each other (D20).

## Phases

| Phase | Delivers | Consumes contracts from | Provides contracts | Status |
|---|---|---|---|---|
| **0. Foundation** | repo setup, store interface, artifact schema conventions, CLI skeleton, Claude Code plugin skeleton, telemetry event format, benchmark harness | — | `store`, `artifact-base`, `telemetry-event`, `cli-conventions`, `benchmark` | done ([report](phases/0-foundation.md#end-of-phase-report)) |
| **1a. C# map** | Roslyn extractor at method level, C# link detectors, blind-spot markers, impact answers, scoring | 0 | `code-graph`, `impact-report` | done ([report](phases/1a-csharp-map.md#end-of-phase-report)) |
| **1b. Frontend + API shapes** | TypeScript extractor, frontend → backend HTTP links, API request/response fields; proven on CleanArchitecture, Kavita, Sonarr | 0, 1a | — (extends `code-graph` → v2, `impact-report` → v3) | done ([report](phases/1b-frontend-api.md#end-of-phase-report)) |
| **1c. Git history** | a hardened Test 2 first, then 📜 "usually changes together" evidence with noise filters, kept only if it helps; tuned on nopCommerce and Sonarr, judged on bitwarden/server | 0, 1a | — (extends `code-graph` → v3, `impact-report` → v4) | done ([report](phases/1c-git-history.md#end-of-phase-report)) |
| **1d. Agent-fitness pilot** ([plan](phases/1d-agent-fitness.md)) | does the graph do its Phase 4 jobs (D45)? Splitting first (no agents), then context scoping (agents with vs without a graph slice); pre-agreed bars | 0, 1a, 1b, 1c | — | planned |
| **2. Spec** ([plan](phases/2-spec.md)) | 2a: reference pages generated from the graph, citations checked, kept current per PR; 2b: FSD (`.docx`) import, FSD ↔ page links, discrepancy decisions; proven on Sonarr with a planted FSD (D50–D60). Built after 1d reports | 0, 1a, 1b, 1c | `reference-page`, `trace-links`, `source-document` (extends `impact-report` → v5) | planned |
| **3. Tickets** | Jira ticket → context bundle (spec, impact, attachments); updates written back | 0, 1a, 2 | `ticket-bundle`, `ticket-update` | not started |
| **4. Agents** | ticket → task breakdown, bounded parallel subagents, D18 checks on every output | 0, 1a, 2, 3 | `task-plan`, `agent-run` | not started |
| **5. Testing** | unit, integration, Playwright tests chosen from the graph, tagged with requirement IDs | 0, 1a, 2, 4 | `test-result` | not started |
| **6. PR** | review package for seniors, senior comment layer | 0, 1a, 2, 4, 5 | `pr-review` | not started |
| **7. CI/CD** | deployment on GitHub Actions: simple, monitorable, retryable | 0, 5, 6 | `deployment` | not started |
| **8. Dashboard** | BA/PM spec view (D12), then the links between everything | all | — | not started |
| **Demo** (any time) | quick interactive prototypes | 0, 2 | `prototype` | not started |

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
