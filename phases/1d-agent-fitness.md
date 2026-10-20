---
phase: 1d
name: agent-fitness
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3 }
provides: {}
---

# Phase 1d · Agent-fitness pilot

**Approved 2026-10-19. Part 1 reported 2026-10-20: safe fails (D64); part 2 open.**

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
- [ ] Both repos run to 30 split cases or 200 examined; results committed. **Waived by D64, not met.**
  - bitwarden/server reached 30 split cases (116 examined).
  - Sonarr crashed at 32 examined.
  - Both runs used a hunk cutter with bugs, now fixed. They aren't rerun, because no rerun is likely to turn "safe"
    into a pass. The results are committed.
- [ ] **Safe:** ≥ 90% of split cases hold up on **each** repo (A, then B). **Fails** on bitwarden/server: its 6 real
  failures already exceed the 3 that 30 split cases allow.
- [ ] **Useful:** ≥ 25% of examined cases split into 2+ groups. **Not measured.** The first run's 26% on bitwarden is
  inflated by the cutter bug, which made some unsplit cases look split; with the fix it is likely 19–21%. The
  re-grouping that would have measured it was stopped (the owner's choice: Phase 4 redesigns the rule, D65).
- [ ] **Better than naive:** safer than split-by-folder, whose split rate is no more than 5 points above the
  graph's. **Not met as specified.** The graph is safer (0.53 vs 0.32; the gain's 95% range is 0.02 to 0.40). But
  split-by-folder splits 98% of cases against the graph's 26%, so the bar's condition for a fair comparison doesn't
  hold.
- [x] End-of-phase report written, including what the verdict means for Phase 4 (D49).
- [ ] Part 2 grilled or dropped accordingly: *the owner's call, still open.*
- [x] Tests cover the fixture repo; CI green ([run](https://github.com/hohkokyoung/stangent/actions/runs/39430285763)).

## Not in this phase
- Part 2, context scoping with agent runs (D45): grilled after part 1 reports.
- Integration tests (D46).
- End-to-end test links (Playwright `goto` → pages): waits for the pilot (D45).
- Splitting before code exists (from a ticket alone): that's Phase 4's planner; this pilot gives it the best case.

## Progress

### Part 1 · the split check · 2026-10-19 to 2026-10-20
- `--split` in `benchmark/Stangent.Benchmark` (`Split/`).
  - **Diff and cut:** `SplitDiff` reads each change as bytes. `Splitter` cuts hunks at member boundaries and groups
    them per rule.
  - **Graph:** `GraphView` gives which point owns a line, a point's containing type, and reach within N steps.
  - **Apply and restore:** `GroupApplier` writes a group onto the parent and puts the tree back. A state file
    recovers a tree left half-applied by an interrupted run.
  - **Build and test:** `DotnetChecker` runs `dotnet build` or a solution filter, then `tsc`, then `dotnet test` with
    TRX results.
  - **Score:** `SplitScore` holds D49's bars, with 1,000 resamples and a fixed seed.
  - **Resume:** each walked commit's result goes to `.stangent/split-progress.jsonl`, so a crashed run resumes.
  - **Re-grouping:** `--groups-only <results.json>` re-groups the cases a run examined without applying anything.
- Per-repo settings in `benchmark/configs/<name>.split.json`:
  - Sonarr: its CI's unit-test categories, and `tcm` + `tsc` for the frontend.
  - bitwarden: `IntegrationTest` projects left out.
- **Rust:** bitwarden's `util/RustSdk` runs `cargo`, and its seeder and integration-test projects depend on it.
  Rust 1.94.1 was installed with Homebrew's `rustup` (the owner agreed), so no case is skipped for it.
- **Tests:** 17 in `tests/Stangent.Benchmark.Tests`.
  - A fixture repo built by the test: two independent parts, a caller two steps away, a `.csproj` edit, a deleted
    method, and a hunk spanning two members. It checks grouping per rule, the shared group, hunk cutting, applying
    one group and restoring the tree, a group missing a link failing a real `dotnet build`, TRX reading,
    determinism, and resuming.
  - Hand-made graphs for the cutter bugs bitwarden showed: a changed signature, a new record, adjacent overloads, a
    positional record's header, and a type moved to another namespace.

### Runs · 2026-10-19 to 2026-10-20
- **bitwarden/server:** 267 commits walked, 116 examined, 30 split (judged rule). About 14 min per examined case,
  sharing the machine with Sonarr.
- **Sonarr:** crashed at 1:50 am, after 32 examined. It was a segfault inside the .NET 10.0.10 runtime's GC, not an
  exception in our code; another dotnet process crashed the same way at 3:53 am. That's when resume was added. A
  rerun with the fixed cutter was started and then stopped (D64).
- **The cutter's bugs,** found by sorting bitwarden's failures with a fresh agent (D61):
  - A changed signature (a different id on each side) was cut into a deletion and an insertion in different groups.
  - A type moved to another namespace had new ids.
  - A new type's members could scatter.
  - A positional record's header line was owned by a property.

  All four were fixed and tested (d770578, b7e90e9). They added failures, and also false splits: a leftover half
  of a changed signature or a moved type became its own group.

## End-of-phase report

*2026-10-20 · part 1 (the split check).* The verdict on D49's bars:
- **Safe fails.**
- **Useful isn't measured** (likely below the bar).
- **Better than naive isn't met as specified.**

All judged numbers come from bitwarden's first run, whose cutter had bugs (D64). They are bounds, not measurements.
Part 2 is the owner's call (see Follow-ups).

### 1. What was built
- `benchmark/Stangent.Benchmark --split`, described in Progress above:
  ```bash
  bench <clone> --split --split-config benchmark/configs/<name>.split.json [--max-split 30] [--max-examined 200] --out <file>
  bench <clone> --split --split-config ... --groups-only <earlier results.json> --out <file>   # re-group only
  ```
- `benchmark/configs/{sonarr,bitwarden}.split.json`, and `tests/Stangent.Benchmark.Tests` (17 tests).
- No contract provided or changed. The pilot reads `code-graph` v3 and lives in `benchmark/`.

### 2. Benchmark results (`benchmark/results/2026-10-20-phase1d-*.json`)
**bitwarden/server (judged, D64; first run, buggy cutter):** 116 examined, 30 split by the judged rule.
| Rule | Split cases | Split rate (95% range) | Held up | Safe (95% range) |
|---|---|---|---|---|
| **within 2 steps, ✅+🟡 (judged)** | 30 | 0.26 (0.18–0.34), *inflated* | 16 | 0.53 (0.37–0.70) |
| direct only (1 step) | 90 | 0.78 | 38 | 0.42 |
| within 2 steps + 📜 | 27 | 0.23 | 13 | 0.48 |
| split by folder (baseline) | 114 | 0.98 | 36 | 0.32 |

The 14 failed splits, sorted by a fresh agent (`-bitwarden-failures.json`):
- **8 were the tool's own cuts,** now fixed: changed signatures, moved types, a new record, and record headers. At
  least 6 of those 8 cases split only because of the bug, so with the fix they likely don't split at all.
- **6 are real:**
  - **Files the graph can't see:** a Razor view, a package upgrade in the shared `.csproj`, and a build-warning
    setting in `Directory.Build.props`.
  - **A meaning change:** a using-only edit that changes a parameter's type.
  - **A code link the graph had but didn't join:** a deleted interface still used as a parameter type in another
    group. The cause isn't traced.
  - **Behaviour links between code and tests:** test data that must match new validation, and a test counting routes.

**Why "safe" fails anyway.** A fixed rerun walks the same commits, newest first. It would find these 6 real failures
among its first 30 split cases, and 90% of 30 allows at most 3. So safe is at most (30 − 6) / 30 = 80%. That's very
likely, not proven: a fixed cutter could in principle join one of those 6 cases into a single group.

**Sonarr (partial, not judged; buggy cutter, rebuilt from its log):** 32 examined before the crash.
| Rule | Split cases | Split rate | Held up | Safe |
|---|---|---|---|---|
| within 2 steps, ✅+🟡 | 10 | 0.31 | 7 | 0.70 |
| direct only | 17 | 0.53 | 12 | 0.71 |
| within 2 steps + 📜 | 9 | 0.28 | 6 | 0.67 |
| split by folder | 23 | 0.72 | 9 | 0.39 |

Most of Sonarr's commits were skipped:
- 205 had fewer than 2 source files: Sonarr rebases, so many commits are small.
- 52 had more than 15 files.
- 99 had a parent that didn't build. These are mostly this machine, not the code: NuGet audit turns advisories
  published after the commit into errors, and `node_modules` come from today's commit.

How to read it:
- **When the graph splits, its groups hold up about 1.7–1.8× as often as split-by-folder's:** 0.53 vs 0.32 on
  bitwarden, and 0.70 vs 0.39 on Sonarr. The graph knows real links that folders don't.
- **Under the old cut, 6 of bitwarden's 30 splits broke for real reasons.** With the false splits gone, that's
  likely about one real failure in four splits. Most misses are files or meanings outside a C# call graph.
- **Within 2 steps vs direct links:**
  - On bitwarden, within 2 steps was safer (0.53 vs 0.42) and split a third as often (30 vs 90).
  - On Sonarr's 32 cases, it was as safe (0.70 vs 0.71) and split 0.6× as often (10 vs 17).
- **📜 didn't help either:** it joins more and isn't safer.

### 3. Decisions made or changed
- D45–D49, grilled before the phase.
- D48 refined twice: how lines are owned and hunks cut, then the four fixes after bitwarden's first run.
- D61: every verification goes to a separate agent with a fresh context. It was used here twice: to sort the failures
  and to check this report.
- D45 changed: Phase 2 may be built before this verdict.
- D64: judged on bitwarden's first run, no rerun.
- D65: the graph's blind spots and a smarter split rule are Phase 4 work.

### 4. Known gaps
- The judged numbers come from a run whose cutter had bugs, so they are bounds:
  - safe is ≥ 0.53, and very likely ≤ 0.80;
  - useful is ≤ 0.26 and unmeasured; the fixed cutter likely gives 0.19–0.21.
- Sonarr has no judged result: it crashed, and it was skewed to recent commits by environment skips. A rerun needs
  `-p:NuGetAudit=false` and a frontend install per commit.
- Tests added by a change are screened out by design (they don't exist on the parent), so a group missing a new
  test row isn't caught.
- Blind spots behind the real misses (Phase 4 work, D65):
  - Razor views;
  - `.csproj`, `Directory.Build.props` and package versions;
  - using-only edits that change what a name means;
  - tests tied to behaviour by data or counts.

  One code link (a deleted interface still used as a parameter type) wasn't joined, and its cause isn't traced.
- Hub types joining unrelated changes within 2 steps is inferred, not measured. It's suggested by direct-only
  splitting 3× as often on bitwarden.
- The .NET runtime crashed twice under hours of load. Runs now resume, but the cause isn't known.

### 5. Impact on other phases
- **Phase 4 (D49, "safe fails"):** it doesn't trust the graph's split on its own. Parallel work is merged and built
  together before it counts, or tasks run one after another. In practice that's the hybrid recorded in the open
  question on contract-first subagents:
  - parallel readers (exploring, finding tests, reviewing);
  - one writer by default;
  - parallel writers only behind a contract the orchestrator fixes first, then a merged build and test.

  If "useful" also fails, which is likely, D49 says parallel writers seldom pay off, so the default is one writer
  working through tasks in order.
- **Phase 4 also takes on (D65):** the graph's blind spots and a smarter split rule, proven with this harness first.
- **Phases 5 and 6:** the same blind spots limit "what may this change break" until Phase 4 fixes them.
- No contract changed; no phase needs review.

### 6. Follow-ups
- **Part 2 (context scoping, D45):** grill or drop. It's the owner's call. Even with one writer, giving that writer
  its graph slice could still save tokens.
- **Phase 4:** when it designs its splitter, prove it with this harness first. Run with `-p:NuGetAudit=false`, and
  use `--groups-only` on these runs' cases for a fair baseline.
- **Trace the missed link** (a deleted interface still used as a parameter type, commit c40a93e144) when Phase 4
  works on the graph.
