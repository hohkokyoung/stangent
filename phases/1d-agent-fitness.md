---
phase: 1d
name: agent-fitness
status: planned
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3 }
provides: {}
---

# Phase 1d · Agent-fitness pilot

**Approved 2026-10-19.**

Builds on D45–D49. Every benchmark so far scores the graph's predictions; this pilot checks whether the graph
can do its jobs in Phase 4 (D45). **Part 1, this plan, is the split check (job 1):** can the graph cut a real
change into groups that hold up on their own? It runs no agents and no AI (D18): only git, the graph, compilers
and unit tests.

**Part 2, context scoping (job 2),** is grilled and planned only after part 1 reports, because its result
decides what Phase 4 looks like (D49).

The pilot provides no contract and changes none: it reads `code-graph` v3 and lives in `benchmark/`.

## What gets built

### 1. Cases (`benchmark/`, D47)
- **Repos:** bitwarden/server and Sonarr, at the commits pinned in `benchmark/repos.tsv`.
- **Walk:** main-line commits (`--first-parent`, merges diffed against their first parent), newest first.
- **Size filter:** ≥ 3 changed members across ≥ 2 files, ≤ 15 files. Changed members come from the diff's
  line ranges (`ChangedMembers`, extended to TypeScript through the extractor's points).
- **Stop:** 30 split cases, or 200 cases examined, whichever comes first. The examined count, the split count
  and every skip reason are recorded.

### 2. Health check per case (D46, D47)
- The parent and the change must both **build** (`dotnet build`; `tsc --noEmit` for Sonarr's frontend).
- **Unit tests:** the test projects that reference a changed project, excluding names containing
  `Integration` (and the repo's own integration markers, listed per repo in `benchmark/configs/`).
- A test that fails on the parent or on the full change is **screened out** for that case. A case where the
  parent or change doesn't build is skipped as unhealthy, not counted as examined-and-unsplit.

### 3. Splitting (D48)
- **Graph:** built at the change commit; consecutive cases rebuild incrementally.
- **Rule judged on:** changed members joined by ✅ or 🟡 lines within 2 steps, in either direction, through
  unchanged points too, form one group.
- **Reported beside it:** direct only (1 step); within 2 steps + 📜.
- **Deleted members** aren't in the after graph: they join the groups of the members that referenced them in the
  parent's graph.
- **Edits outside members** go with every group that has a changed member in the same file.
- **Files with no changed members** (`.csproj`, config, resources) form the shared group, applied with every group.
- **Hunks spanning two groups** are split at member boundaries. A hunk that can't be split joins its groups,
  counted as "joined by a shared hunk" so it's visible.
- **Baseline:** split by folder (each changed file's directory is a group), with the same shared group.

### 4. Applying a group alone (D46)
- Start from the parent; apply the group's hunks and the shared group's hunks; nothing else.
- **Step A:** build. A group that fails is an unsafe split, recorded with the compiler errors' first lines.
- **Step B, groups that pass A:** run the case's unit tests. A test that passes on the parent and on the full
  change but fails with the group alone is an unsafe split.
- The working tree is restored after every group (as Test 4 does after an interrupted run).

### 5. Results and the verdict (D49)
- `benchmark/results/<date>-phase1d-<repo>-split.json`: per case, the groups for each rule, the outcome of each
  group (compiles, tests, failures), and the totals.
- **Safe:** share of split cases with every group holding up, plain percentage plus a 95% range (1,000
  resamples, fixed seed). Below 30 split cases, "below the floor".
- **Useful:** share of examined cases split into 2+ groups.
- **Better than naive:** the graph's safe share vs split-by-folder's, with both split rates.
- Run with `dotnet run --project benchmark/Stangent.Benchmark -- <clone> --split --out <file>`.

### 6. Tests
- A fixture repo built by the test (like 1c's): a change with two independent parts, one with a hidden link
  (a caller two steps away), a `.csproj` edit, a deleted method, and a hunk spanning two members.
- Checks: grouping per rule, the shared group, hunk splitting, applying a group alone and restoring the tree,
  and determinism (two runs give identical results).

## Done when
- [ ] Both repos run to 30 split cases or 200 examined; results committed.
- [ ] **Safe:** ≥ 90% of split cases hold up on **each** repo (A, then B).
- [ ] **Useful:** ≥ 25% of examined cases split into 2+ groups.
- [ ] **Better than naive:** safer than split-by-folder, whose split rate is no more than 5 points above the graph's.
- [ ] End-of-phase report written, including what the verdict means for Phase 4 (D49), and part 2 grilled
  or dropped accordingly.
- [ ] Tests cover the fixture repo; CI green.

## Not in this phase
- Part 2, context scoping with agent runs (D45): grilled after part 1 reports.
- Integration tests (D46).
- End-to-end test links (Playwright `goto` → pages): waits for the pilot (D45).
- Splitting before code exists (from a ticket alone): that's Phase 4's planner; this pilot gives it the best case.

## Progress
