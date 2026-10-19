---
phase: 1c
name: git-history
status: done
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, code-graph: 3 }
provides: {}
---

# Phase 1c · Git history

**Approved 2026-10-19. Done 2026-10-19.**

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
  `filter: blob:limit=200k` on `actions/checkout` (D42 refined). Writing the workflow itself is Phase 7.
- stangent's own CI needs no history: the fixture repo is created by the test.

## Done when
- [x] **Gate 1** run on all four repos (and bitwarden/server), with each fix counted once, full history, 95% ranges and the bundle slice; results committed. *If it fails, 1c stops here: the report says why, and the boxes below don't apply.*
- [x] `stangent graph build` adds 📜 lines with all four noise filters, renames followed (or the fallback recorded), and `meta.history`; a shallow clone warns and keeps stored 📜.
- [x] `graph impact` shows the `history` group last, capped at 10 with evidence, `--expand history`, the "also 📜" tag, and never walks 📜 (`impact-report` v4).
- [x] Contracts bumped per D20: `code-graph` v3, `impact-report` v4, 1a reopened, 1b marked `needs review` with its follow-up; `phases check` passes.
- [x] Defaults tuned on nopCommerce and Sonarr, then frozen and committed before judging.
- [x] **Gate 2** on the judging repo, bitwarden/server (Kavita and eShop reported, under the floor): **passed**
  - [x] **📜 adds something:** graph + top-10 📜 beats graph alone on recall, with the 95% range of the gain above zero;
  - [x] **filters help, not hurt:** 📜's precision ≥ the plain baseline's, and its recall ≥ 90% of the baseline's;
  - [x] **answers stay short:** the median answer grows by ≤ 10 files;
  - [x] **cheap:** ≤ 10 s added to a nopCommerce build; ≤ 30 s added to the first build by rename following;
  - [x] **stable:** Test 3 holds (byte-identical builds, incremental = full).
  - *If "📜 adds something" fails:* 📜 ships **off by default** (`history.enabled: false`) and the report says why.
- [x] Tests cover the fixture repo, the filters, renames, shallow clones, impact output and determinism.
- [x] CI green ([run](https://github.com/hohkokyoung/stangent/actions/runs/39246814190)); end-of-phase report written.

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

### Step 2 · history reader · 2026-10-19
- `src/Stangent.Graph/History/HistoryReader.cs`: the log parser (renames, deletes), the counter (filters,
  windows, thresholds) and the build step (reuse, shallow clones, blobless clones). 📜 lines live in their own
  table, outside the walked lines, so "never walked" holds by construction.
- **Cost on the benchmark repos** (full clones, history step only): nopCommerce 1.0 s (16k commits),
  Sonarr 0.9 s, bitwarden/server 0.75 s, Kavita 0.4 s. Well under D41's 10 s, so no incremental history.
- **Renames (D38):** the first version took 7.1 s on nopCommerce and 34 s on a blobless clone. Reading only the
  map's file types fixed the first; the second is a property of blobless clones, so they skip renames with a
  warning, and CI's clone is `blob:limit=200k` instead (D42 refined).
- **`code-graph` v3** (file layout 3): 1a reopened, 1b marked `needs review` (D20).
- 18 tests: parsing, each filter, windows, thresholds, direction, a merged branch counting once, a move with an
  edited namespace line, reuse vs recompute, a shallow clone keeping stored lines, a blobless clone, storage round-trips.

### Step 3 · impact answers · 2026-10-19
- `impact-report` v4: the `history` section after every group (top 10 by share, evidence, the changed file it
  goes with, whether the graph found it too, `--expand history`), the "also 📜" tag on graph items, and the
  Markdown block. 1a reopened again; the `/stangent:impact` command explains 📜.
- On Kavita, `--file Kavita.Services/SeriesService.cs --max-depth 2` lists `SeriesRepository.cs` and the Angular
  `series-detail.component.ts` (7 of the last 20 commits each), which the graph doesn't reach within 2 lines.
- 5 more tests: cap and order, never walked, the tag, depth limits and Markdown, no history.

### Step 5a · tuning · 2026-10-19 (`benchmark/results/2026-10-19-phase1c-*tuning.json`)
- **No peeking ahead:** the counter became a `HistoryIndex` that answers "📜 for these files from commits older than
  this one". The benchmark scores each change with only the history before it, using the product's own code; the
  index at today's commit reproduced the stored lines exactly on both repos (`historySelfCheck`).
- **Grid:** window {10, 20, 40} × minShared {2, 3} × minShare {0.2, 0.3, 0.5} × maxFiles {20, 30, 50}, on Test 2's
  131 fixes (nopCommerce 44, Sonarr 87). `--select-tuning` applied the rule; 8 of 54 settings were eligible.
- **Chosen and frozen:** window 10, minShared 3, minShare 0.2, maxFiles 50. Graph + 📜 recall 0.500 vs graph alone 0.457.

## End-of-phase report

*2026-10-19 · first report.* Gate 2 passed; CI green on GitHub ([run](https://github.com/hohkokyoung/stangent/actions/runs/39246814190)); phase **done**.

### 1. What was built
- **The history reader** (`src/Stangent.Graph/History/`):
  - `HistoryLog` reads main's own line (`--first-parent`), follows renames and moves (only the map's file
    types), and stops a deleted path's history passing to a later file of the same name.
  - `HistoryIndex` applies the four noise filters and counts each file's window. It answers "📜 from commits
    older than X", which is what lets the benchmark score without peeking ahead.
  - `HistoryStep` runs it in `graph build`: reuse by fingerprint, shallow clones keep stored lines, blobless
    clones skip renames with a warning.
- **The graph:** 📜 `changes-with` lines in their own table, outside the walked lines; `meta.history`; the
  `history` settings in `stangent.yaml`.
- **Impact answers:** the `history` section (top 10 by share, evidence, `found`, `--expand history`), the
  "also 📜" tag, the Markdown block, and the `/stangent:impact` reading guide.
- **Contracts:** `code-graph` v3 (file layout 3) and `impact-report` v4. 1a reopened; 1b marked `needs review` (D20).
- **Benchmark:**
  - Test 2 hardened (main line, one case per fix, full history, 400 fixes, bundle slice, 95% ranges);
  - Gate 1 and Gate 2;
  - 📜 predictors for Tests 1 and 2 (history older than each change) with a self-check against the stored lines;
  - the tuning grid and `--select-tuning`;
  - bitwarden/server added.
- **Tests:** 24 new, 79 in all.

### 2. Benchmark results (`benchmark/results/2026-10-19-phase1c-*.json`)
Gate 1 and tuning are in Progress above. With the frozen defaults (window 10, minShared 3, maxFiles 50):

**Gate 2: Test 2 (what broke), one case per fix** (recall · precision; 📜 and the baseline are each top 10)
| Repo (role) | Fixes | Graph ≤ 2 | Graph + 📜 | Gain (95% range) | 📜 alone | Plain co-change | Median added | Checks |
|---|---|---|---|---|---|---|---|---|
| **bitwarden/server (judge)** | 91 | 0.444 · 0.004 | **0.470** · 0.004 | **+0.025 (0.009 to 0.047)** | 0.079 · **0.031** | 0.065 · 0.022 | 4 | **all pass** |
| Sonarr (tune) | 87 | 0.462 · 0.007 | 0.517 · 0.007 | +0.056 (0.023 to 0.092) | 0.112 · 0.028 | 0.133 · 0.026 | 4 | recall check fails |
| nopCommerce (tune) | 44 | 0.451 · 0.005 | 0.473 · 0.005 | +0.022 (0 to 0.058) | 0.055 · 0.015 | 0.055 · 0.012 | 4 | no clear gain |
| Kavita (reported) | 23 | 0.030 · 0.002 | 0.090 · 0.004 | +0.06 (0 to 0.112) | 0.060 · 0.018 | 0.090 · 0.027 | 9 | under the floor |
| eShop (reported) | 1 | — | — | — | — | — | — | under the floor |

**Test 1: from one file of a commit, its other files** (recall · precision)
| Predictor | bitwarden/server | Sonarr | nopCommerce | Kavita | eShop |
|---|---|---|---|---|---|
| graph ≤ 2 | 0.20 · 0.03 | 0.17 · 0.08 | 0.21 · 0.04 | 0.11 · 0.12 | 0.08 · 0.19 |
| 📜 (top 10) | 0.14 · **0.27** | 0.12 · **0.27** | 0.12 · 0.19 | 0.05 · 0.12 | 0.02 · 0.37 |
| plain co-change (top 10) | 0.16 · 0.20 | 0.16 · 0.19 | 0.17 · 0.17 | 0.02 · 0.14 | 0.05 · 0.25 |
| **graph ≤ 2 + 📜** | **0.29** · 0.04 | **0.26** · 0.10 | **0.28** · 0.05 | **0.14** · 0.11 | **0.10** · 0.21 |

**Cost and stability**
| | bitwarden/server | nopCommerce | Sonarr | Kavita | eShop |
|---|---|---|---|---|---|
| 📜 step in `graph build` | 0.8–0.9 s | 1.0–1.3 s | 1.0 s | 0.5 s | 0.2 s (blobless: renames skipped) |
| 📜 lines | 10,020 | 9,454 | 7,369 | 4,230 | 51 |
| Test 3: identical builds · incremental = full | ✅ · ✅ | ✅ · ✅ | — | — | — |
| Self-check: index at HEAD = stored lines | ✅ | ✅ | ✅ | ✅ | ✅ |

First build with rename following on a `blob:limit=200k` nopCommerce clone: 4.9 s (D38's limit: 30 s).

How to read it:
- **Gate 2 passes, so 📜 ships on by default.** On a repo it was never tuned on, with a different merge style
  (squash-only), history adds recall the graph misses, at better precision than plain co-change, for +4 files.
- **The gain for "what broke" is modest** (+2 to +6 points where it's clear). The gain for "what else this commit
  touches" is larger (+6 to +9 points), and there 📜's top 10 is right 19–27% of the time, against 3–8% for the graph.
  That's the hint's real use: "you probably need to touch these too".
- **Compared with 1b:** 1b had co-change beating the graph on Kavita's Test 2 (0.32 vs 0.29 recall on 24 pairs).
  Counted per fix on the main line, Kavita has 23 fixes, mostly bundles: too few to say anything, though history
  still finds what the graph within 2 lines doesn't.
- **📜 isn't uniformly better than plain co-change.** Its precision is higher everywhere except Kavita, but its
  recall is lower on Sonarr and Kavita: its window is 10 commits, while plain co-change counts every older commit.

### 3. Decisions made or changed
- D33–D44, grilled before the phase.
- **D43 refined:** bitwarden/server judges, because Kavita and eShop fell under the 30-fix floor.
- **D38 and D42 refined by measurement:** read only the map's file types (7.1 s → 0.75 s). Blobless clones skip
  renames and warn. CI clones with `blob:limit=200k`.
- **D35 and D37 tuned:** window 10, minShared 3, minShare 0.2 (never binding), maxFiles 50.

### 4. Known gaps
- **The tuning rule didn't include Gate 2's recall check.** With the frozen settings, Sonarr fails "recall ≥ 90% of
  the baseline's" (0.112 vs 0.120). The verdict is judged on bitwarden/server, but the next tuning should use
  all of Gate 2's checks as its eligibility rule.
- **Small margins:** the precision differences that decided tuning eligibility are 0.001–0.01.
- **Rebased PRs count per commit** (D36), and blobless clones lose rename history (D38).
- **Thin repos:** Kavita (23 fixes) and eShop (1). Test 2 still skips fixes touching more than 15 files, a cap inherited from 1a.
- **Not built:** member-level 📜 (D34), files off the map, re-ranking by the "also 📜" tag (D40).
- **Requirements and testing:** `--diff-merges` needs git 2.31 or newer. Not tested on Windows.
- **Hand-checking:** the Test 2 pairs from D28 are still not checked by a person.

### 5. Impact on other phases
- `code-graph` v2 → v3: 1b is `needs review` (its follow-up: confirm, then move to v3).
- `impact-report` v3 → v4: no phase consumes it yet. Phase 6 (PR review) will show the `history` section.
- Phase 7's graph-building workflow must clone with `fetch-depth: 0` and `filter: blob:limit=200k` (D42).
- Phase 5 (testing) could use 📜 to suggest tests that usually change with a file; not designed yet.

### 6. Follow-ups
- ~~Push and confirm CI is green, then mark this phase `done` here and in `roadmap.md`.~~ Done.
- 1b's `needs review`: confirm nothing in 1b depends on the v2 layout, then move it to `code-graph: 3`.
- Next time 📜 is tuned, make the eligibility rule all of Gate 2's checks.
- A person checks the Test 2 hand-check sample once (D28).
