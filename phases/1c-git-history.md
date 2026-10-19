---
phase: 1c
name: git-history
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 2 }
provides: {}
---

# Phase 1c · Git history

**Approved 2026-10-19. Started 2026-10-19.**

Builds on D26–D29 and D33–D44. The goal: 📜 "usually changes together" evidence from git history,
shown as a capped hint after the ✅ and 🟡 answers, and kept only if it measurably helps. No AI anywhere (D18).

The phase has a gate in the middle. **Step 1 hardens the measuring stick before any 📜 code** (D33). If
history adds nothing over the graph on solid numbers, 1c stops after step 1 and records why (D44, Gate 1).

Like 1b, this phase **extends 1a's contracts** rather than providing new ones. When 📜 lands, it
reopens `phases/1a-csharp-map.md` and bumps `code-graph` to v3 and `impact-report` to v4 (D20).

## What gets built

### 1. A trustworthy Test 2, then Gate 1 (`benchmark/`, D33, D43, D44)
- **Full-history clones** of nopCommerce, Sonarr, Kavita and eShop, recreated as `benchmark/README.md` describes.
- **Main line only** (D36): the commit list comes from `git log --first-parent`, with merges diffed
  against their first parent, instead of `--no-merges`. The co-change baseline and Test 1 use the same list.
- **Each fix counts once.** Test 2 has one case per fix:
  - its seeds are the changed members of every commit that introduced the lines it fixed;
  - its targets are the files it touched outside those commits.
  - A fix that blames 9 change commits is still one case.
- **Full history:** no 36-month window, and more fix commits sampled (all of them, capped for time). The cap is recorded in the results.
- **Bundle slice:** fixes whose subject reads as a bundle ("fixes", "polish", "misc", "bunch",
  "collection", "and more") are scored as their own slice as well as in the total. The pattern lives in the benchmark.
- **Independent fixes reported per repo.** A repo under 30 doesn't count toward pass or fail (D43).
- **95% ranges by resampling fixes** (1,000 resamples, fixed seed, so a re-run prints the same numbers)
  for every recall and precision, and for differences between predictors.
- **Gate 1 predictor:** "graph within 2 lines + the plain co-change baseline's top 10" (ranked by count), next to "graph within 2 lines" alone.
- Results: `benchmark/results/<date>-phase1c-<repo>-gate1.json`.
- **Gate 1:** if adding history raises recall (95% range above zero) on no repo with ≥ 30 fixes, 1c
  stops here. The report says so, and the rest of this plan is not built.

### 2. The history reader (`src/Stangent.Graph/History/`, C#)
- **Reads the main line:** `git log --first-parent --diff-merges=first-parent -M --name-status` from the graph's commit (D36, D38).
  - Renames and moves are followed newest to oldest, so old paths map to today's paths.
  - The cost of following renames is measured on nopCommerce. If the first build gets more than ~30 s
    slower, it falls back to not following renames, and the gap is recorded (D38).
- **Only files on the map count** (D34): files with a point in the graph, including Angular templates. Everything else is ignored.
- **Noise filters** (D37), applied in this order, each with a count of what it dropped:
  1. **reverts:** a commit whose message says `This reverts commit <hash>`, and that commit;
  2. **formatting sweeps:** commits listed in `.git-blame-ignore-revs`;
  3. **bots:** an author ending in `[bot]`, or listed in `history.botAuthors`;
  4. **huge commits:** more than `history.maxFiles` (30) files on the map.
- **Counting** (D35): for each file A, take its last `history.window` (20) surviving commits. B is 📜 for A
  when it changed in ≥ `history.minShared` (3) of them and ≥ `history.minShare` (30%). Each pair is judged in both directions separately.
- **Lines:** `changes-with` from B to A, labelled 📜 (a new `Label.History`), carrying `shared` and `of`
  ("7 of the last 12"). This follows the graph's rule: lines point from the dependent to what it depends on.
- **Settings in `stangent.yaml`:**
  ```yaml
  history: { enabled: true, window: 20, minShared: 3, minShare: 0.3, maxFiles: 30, botAuthors: [Weblate], followRenames: true }
  ```
  The defaults are frozen after tuning (step 5).
- **Recomputed in full** (D41) when the graph's commit or a filter input (the `history` settings,
  `.git-blame-ignore-revs`) changes; skipped otherwise. Incremental history is built only if nopCommerce's full recompute takes more than ~10 s.
- **Shallow or no history** (D42):
  - `graph build` never fails over a shallow clone;
  - `meta.history` records the commit range used, the commits read, and the drop counts per filter, or `none`;
  - with no history, the 📜 lines already in the stored graph (e.g. CI's downloaded copy, D25) are kept,
    marked with the commit they were computed at; otherwise there's no 📜 and `loadWarnings` says `history_unavailable`.
- Runs after the link step; no Roslyn and no Node.

### 3. Impact answers (`impact-report` v4, D40)
- **Never walked:** the walk never follows `changes-with`. 📜 is looked up one step out from the changed
  files only (`--diff` and `--file` seeds; for `--symbol`, the symbol's file).
- **A `history` group, after every other group:**
  - top 10 files, by share, then count, then path; the changed files themselves are excluded;
  - each item has its evidence (`shared`, `of`);
  - `--expand history` lists the rest.
- **"also 📜" tag:** an item already found by ✅/🟡 keeps its place and carries the same evidence. Nothing is re-ranked.
- **Markdown:** "📜 usually changes together (3 of 9 shown)", each line "7 of the last 12".
- `schemas/impact-report.v4.json`; v1–v3 are kept for history.

### 4. Contracts (D39, D20)
- **`code-graph` v3:**
  - the `changes-with` line kind and the 📜 label;
  - `meta.history`;
  - the `history` settings;
  - file layout version 3 (an older file is rebuilt, not migrated);
  - documented in `docs/code-graph.md`.
- **`impact-report` v4:** the `history` group and the tag.
- **Reopen 1a:** its `provides` becomes `code-graph: 3, impact-report: 4`, with a "Reopened by Phase 1c" note.
- **1b consumes `code-graph` v2,** so it's marked `needs review` in `roadmap.md` with a follow-up: confirm
  nothing in 1b breaks, then move it to v3. 1c moves its own `consumes` to v3.
- `phases check` passes at every commit.

### 5. Tuning, then Gate 2 (`benchmark/`, D43, D44)
- **New predictors** in the history tests:
  - "📜 (top 10)";
  - "graph within 2 lines + 📜 (top 10)", which is what a person sees first;
  - next to the plain co-change baseline and "graph within 2 lines". Test 1 also reports 📜 per label (D28).
- **Tuning on nopCommerce and Sonarr only.** A small grid: window {10, 20, 40}, minShared {2, 3},
  minShare {0.2, 0.3, 0.5}, maxFiles {20, 30, 50}.
  - The rule: the highest graph + 📜 recall whose 📜 precision is at least the plain baseline's.
  - The chosen values become the defaults and are **frozen** (committed) before the judging repos are scored.
- **Judging on bitwarden/server** with the frozen defaults (Kavita and eShop reported too; both fell under the 30-fix floor at Gate 1, D43): Gate 2's checks (see Done when).
- **Timing:** 📜's share of a nopCommerce build, and the extra first-build time from following renames.
- Results: `benchmark/results/<date>-phase1c-<repo>-history.json`, plus `-tuning.json` for the grid.

### 6. Tests and CI
- **A fixture repo built by the test**, a temporary git repo with scripted commits. It covers:
  - a merged branch (counts once);
  - a squash commit;
  - a bot commit;
  - a revert pair;
  - a commit listed in `.git-blame-ignore-revs`;
  - a huge commit;
  - a rename with an edited namespace line;
  - a file whose last 20 commits push out an old partner;
  - a shallow clone of it (keeps stored 📜, warns).
- **Impact tests:** the `history` group, the cap and `--expand`, the "also 📜" tag, `changes-with` never walked, and the v4 schema.
- **Determinism:** two builds byte-identical with 📜; incremental = full.
- **Docs:** `docs/code-graph.md` and `benchmark/README.md`. The CI note for users' repos: the graph build needs `fetch-depth: 0` and
  `filter: blob:none` on `actions/checkout`. Writing the workflow itself is Phase 7.
- stangent's own CI needs no history: the fixture repo is created by the test.

## Done when
- [x] **Gate 1** run on all four repos (and bitwarden/server), with each fix counted once, full history, 95% ranges and the bundle slice; results committed. *If it fails, 1c stops here: the report says why, and the boxes below don't apply.*
- [ ] `stangent graph build` adds 📜 lines with all four noise filters, renames followed (or the fallback recorded), and `meta.history`; a shallow clone warns and keeps stored 📜.
- [ ] `graph impact` shows the `history` group last, capped at 10 with evidence, `--expand history`, the "also 📜" tag, and never walks 📜 (`impact-report` v4).
- [ ] Contracts bumped per D20: `code-graph` v3, `impact-report` v4, 1a reopened, 1b marked `needs review` with its follow-up; `phases check` passes.
- [ ] Defaults tuned on nopCommerce and Sonarr, then frozen and committed before judging.
- [ ] **Gate 2** on the judging repo, bitwarden/server (Kavita and eShop reported, under the floor):
  - [ ] **📜 adds something:** graph + top-10 📜 beats graph alone on recall, with the 95% range of the gain above zero;
  - [ ] **filters help, not hurt:** 📜's precision ≥ the plain baseline's, and its recall ≥ 90% of the baseline's;
  - [ ] **answers stay short:** the median answer grows by ≤ 10 files;
  - [ ] **cheap:** ≤ 10 s added to a nopCommerce build; ≤ 30 s added to the first build by rename following;
  - [ ] **stable:** Test 3 holds (byte-identical builds, incremental = full).
  - *If "📜 adds something" fails:* 📜 ships **off by default** (`history.enabled: false`) and the report says why.
- [ ] Tests cover the fixture repo, the filters, renames, shallow clones, impact output and determinism.
- [ ] CI green; end-of-phase report written.

## Not in this phase
- Member-level 📜 (D34): only if file-level 📜 proves too noisy.
- Grouping rebased commits by PR through the code-host adapter (D36): only if rebased history measurably hurts.
- Incremental history (D41): only if nopCommerce's full recompute exceeds ~10 s.
- Files that aren't on the map (`.resx`, `.sql`, `.csproj`, JSON config) (D34).
- Re-ranking ✅/🟡 items by their "also 📜" tag (D40): only once the benchmark shows it's clearly more precise.
- The CI workflow that builds and shares the graph (Phase 7); 1c only documents the clone it needs.

## Progress

### Gate 1 · 2026-10-19 · passed (`benchmark/results/2026-10-19-phase1c-*-gate1.json`)
Test 2 hardened as planned:
- the main line only (`--first-parent`, blame too);
- one case per fix;
- full history;
- up to 400 fix commits sampled per repo;
- 95% ranges from 1,000 resamples of fixes (fixed seed).

| Repo (role) | Fixes | Graph ≤ 2: recall | + co-change top 10 | Gain (95% range) | Median answer | Verdict |
|---|---|---|---|---|---|---|
| Sonarr (tune) | 87 | 0.46 | 0.52 | **+0.06 (0.03 to 0.10)** | 48 → 56 files | history helps |
| nopCommerce (tune) | 44 | 0.45 | 0.46 | +0.01 (0 to 0.04) | 109 → 118 | no clear gain |
| bitwarden/server (judge) | 91 | 0.44 | 0.48 | **+0.04 (0.02 to 0.07)** | 119 → 123 | history helps |
| Kavita (reported) | 23 | 0.03 | 0.12 | +0.09 (0.02 to 0.20) | 36 → 46 | under the 30-fix floor |
| eShop (reported) | 1 | — | — | — | — | under the floor |

- **Gate 1 passes:** history adds recall on Sonarr and bitwarden/server, with ranges above zero, so 1c continues.
- **nopCommerce gains almost nothing:** the graph already finds what its fixes touch.
- **Kavita and eShop are too thin to count** (D43). Many of Kavita's fixes touch more than 15 files, Test 2's
  cap from 1a; 18 of its 23 are bundles. Its direction matches 1b (history finds what the graph barely
  reaches), but on 23 fixes. bitwarden/server was added as the judging repo instead (D43 refined).
- **Bundle fixes:** 18 of 23 on Kavita, 17 of 44 on nopCommerce, 13 of 91 on bitwarden/server, 1 of 87 on Sonarr.
  On Sonarr and bitwarden/server the gain comes from single-topic fixes (+0.06 and +0.05), not bundles.
- The plain baseline's top 10 alone finds 0.06–0.13 of what fixes touch. Its value is in what it adds to the graph, not as a replacement.

## End-of-phase report

*Written when the phase ends, in the format in `roadmap.md`:*

1. What was built
2. Benchmark results (Gate 1 on all four repos; tuning; Gate 2 on the judging repos; compared with 1b's history results)
3. Decisions made or changed
4. Known gaps
5. Impact on other phases
6. Follow-ups
