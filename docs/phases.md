# How phases work

How a phase is recorded, reported and changed (D20). Moved from `roadmap.md` on 2026-11-05.

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
