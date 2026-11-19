# Decisions D25–D49: the code graph and the agent-fitness pilot

Full entries, word for word. One line each in [`decisions.md`](../decisions.md).

## D25 · The code graph is rebuilt from code, never committed — 2026-10-19
- The graph lives in `.stangent/graph.sqlite` (git-ignored), tagged with the commit it was built from.
- CI builds it once per merge to `main` and saves it as a build output; developers and agents
  download that copy instead of re-indexing, then re-index only what changed locally.
- Same commit → same graph; tested by building twice and comparing.
- The rule (refines D7): git holds what people decide (specs, trace links, verifications,
  discrepancy decisions); anything code can regenerate stays out. The committed link file
  (D10) is checked in CI against a freshly built graph.
**Why:** committing it would add graph churn to every PR, cause unresolvable merge conflicts,
and risk a stale graph — and CI would have to rebuild it to verify it anyway.
**Cost:** a fresh checkout has no graph until it downloads one or indexes (minutes at 500k
lines); CI build outputs expire, so old commits eventually need re-indexing.

## D26 · What the code graph holds — 2026-10-19
- **Points, at method level:** project, file, class/interface, method, property; entry points
  (HTTP endpoint, frontend page/component, event/message, database table); test methods (marked as tests).
- **Also, so non-call breakages show up:**
  - **API data shapes:** each endpoint's request/response fields, plus entity/DTO properties
    (respecting each project's JSON naming settings). Frontend fields are linked only when the
    frontend type is clearly tied to the endpoint (typed interface, generated client); otherwise ❓.
  - **Config keys,** linked to the code that reads them (including bound settings classes).
    Environment variables and external secrets are ❓.
- **Lines:** calls, uses, implements/overrides, inherits, wired to (DI), routes to, publishes/handles,
  reads/writes table, HTTP call, reads config, tests.
- **Every line is labelled:** ✅ certain (compiler), 🟡 guessed (detector pattern),
  📜 usually changes together (from `git log`, filtered: no merges, no huge commits, older history
  weighs less; a hint only, never blocking), ❓ unknown (blind spot).
- Later: test coverage data (Phase 5); real traffic between services from OpenTelemetry traces, if the company has them.
- *Refined 2026-10-19 (Phase 1c):* 📜 is file to file (D34), counted over each file's recent commits
  (D35), and "no merges" became "main line only" (D36).
- Rejected: numeric confidence scores (made-up precision).
**Cost:** Phase 1 takes longer (three extra detectors); the graph is roughly 1.5–2× bigger (estimate, to
measure); history needs full clones, not shallow ones. Reports list ✅ first, then 🟡, then 📜 and ❓,
grouped and capped, to avoid alert fatigue. The benchmark scores each label separately; a label that
mostly adds false alarms is switched off or tuned.

## D27 · Impact answers: summary first, details on request — 2026-10-19
- An impact answer stops at entry points people recognise: endpoints, pages, event handlers,
  requirements (via D10 links), and the tests that cover the change. Grouped by kind and by
  label (✅ 🟡 📜 ❓), each group with its count.
- Details come on request (`--expand <group>`); the methods in between stay available, not shown.
- ❓ unknowns are always listed one by one, never folded into a count.
- One result, two formats: compact JSON for agents, Markdown for PRs and people (reused in Phase 6).
- Built by code (graph walk + grouping), no AI: the same change always gives the same report (D18).
- *Refined 2026-10-19 (Phase 1a):* event handlers are end points of the walk. Past one, only further
  events are followed, never the dispatcher behind it (otherwise one change reached 2,534 nopCommerce endpoints).
- *Refined 2026-10-19 (D28 scores):* items are listed nearest first, with each item's distance and each
  group split into direct (1 line away), near (2–3) and far (4+). `--max-depth` cuts off the rest
  (`impact-report` v2). Following every caller of a caller gave 0.5% precision on nopCommerce;
  within 2 lines it was 7%.
**Why:** an agent reads ~15 lines instead of hundreds; a BA/PM can follow it through requirement IDs.
**Cost:** group counts can hide something important; mitigated by counts on every group and listing every unknown.

## D28 · How impact analysis is scored — 2026-10-19
Three tests, all run by code from git history (no AI, no manual labelling):
1. **Finds the rest of the change:** given one file of a past PR, how many of the PR's other files
   does impact analysis find, and how many wrong ones does it add.
2. **Predicts what broke (headline):** pairs of a change and a later fix ("fix", "regression", a
   referenced issue); does impact on the first commit include what the fix touched.
3. **Stable and fast:** two builds of the same commit are byte-identical; full and incremental build times.
- Tests 1–2 report found/wrong **per label** (✅ 🟡 📜 ❓, D26) in every end-of-phase report.
- A sample of ~20 Test 2 pairs is hand-checked once to say how reliable the pair-finding is.
- No targets yet: measure first, set targets from the first results.
- The benchmark needs full git history for these repos, not shallow clones.
- *Refined 2026-10-19:* scoring starts from changed lines, finding changed methods by name in today's
  graph. Every test also scores free baselines (same project, same folder, co-change) on the same cases.
  An exact-graph check on a sample of past commits measures the error left by using today's graph
  (median agreement 1.0).
- *Refined 2026-10-19:* **Test 4, compile-break ground truth.** Rename sampled methods in memory,
  recompile the projects that depend on them, and score against the files that fail to compile. It
  checks the ✅ graph directly. Direct ✅ links scored recall 1.00 on nopCommerce and 0.97 on eShop.

## D29 · Phase 1 is split into 1a, 1b, 1c — 2026-10-19
| Sub-phase | Delivers | Proven on |
|---|---|---|
| 1a. C# map | Roslyn extractor (method level); detectors: routes, DI, events, EF Core / entity tables, config keys, HTTP between services; impact answers (D27); scoring tests (D28) | nopCommerce, eShop |
| 1b. Frontend + API shapes | TypeScript extractor; Angular/React → backend HTTP links; API request/response fields | CleanArchitecture |
| 1c. Git history | 📜 "usually changes together", with noise filters | nopCommerce, eShop |
Each ends with its own report (D20). 1b depends on 1a; 1c may wait if 1a's scores are good enough.
- *Refined 2026-10-19 (Phase 1c):* 1c is tuned on nopCommerce and Sonarr and judged on bitwarden/server (D43; Kavita and eShop have too few fixes).
**Why:** a working, scored C# impact analysis much sooner, and a chance to change course before the frontend half.

## D30 · The TypeScript extractor runs in Node, inside the stangent tool — 2026-10-19
- `stangent graph build` runs a bundled Node extractor for each frontend project (each `tsconfig.json`),
  using the real TypeScript compiler. It writes its fragment in the same format as the C# side
  (`code-graph`); linking frontend calls to backend endpoints happens in C#, in the same link step.
- One install (the dotnet tool carries the bundled extractor); Node must be on the machine (D9).
- Types need the frontend's packages: `graph build` runs `npm ci` (or yarn/pnpm, whichever the project
  uses) when they aren't installed, just as it runs `dotnet restore`; `--no-restore` skips both.
  Without packages, frontend links fall back to 🟡 name matching and stangent says so.
- One bundled TypeScript version; code it can't parse is recorded as ❓, never skipped silently.
Rejected: parsing TypeScript from C# (no types); a separate npm tool (two installs to keep in step).
**Cost:** first-time `npm ci` per frontend takes minutes; Node is a hard requirement for frontend analysis.

## D31 · What the frontend map holds — 2026-10-19
- **From the TypeScript compiler (✅):** files, classes, functions, methods, properties; calls and uses;
  React components through JSX (`<SeriesCard />` is a resolved reference).
- **Angular templates:** parsed with Angular's own template parser (`@angular/compiler`, bundled).
  Child components link by selector (✅ when exactly one component declares it, 🟡 when several do);
  template bindings link to the component members they call or read. Both NgModule and standalone
  apps are supported (the owner's apps weren't specified; CleanArchitecture uses NgModules).
- **Pages:** Angular Router and React Router routes are frontend entry points, a new `pages` group in
  impact answers (`impact-report` v3, recorded per D20 when built).
- **Frontend → backend calls:** one frontend-HTTP detector, matched against backend endpoints like
  `http-services`:
  - hand-written calls (Angular `HttpClient`, `fetch`, axios): URL and verb from the code, 🟡;
  - in-house wrappers (e.g. Sonarr's `useApiQuery`): named in `detectorSettings`, like `handlerInterfaces`;
  - generated clients: read the generated file when it exists (exact URLs and types); otherwise the
    generator's naming rules (NSwag: `TodoListsClient.getTodoLists()` → `GetTodoLists` in `TodoLists`), 🟡.
    NSwag first; other generators each get their own small rule set when needed.
- Out of scope until a repo needs them: Vue and Svelte templates.
**Cost:** a larger extractor bundle (`@angular/compiler`); templates using new control flow (`@if`, `@for`) need a recent parser.

## D32 · How Phase 1b's frontend links are proven — 2026-10-19
| Link | Ground truth | Test |
|---|---|---|
| TypeScript code links (calls, uses, JSX, Angular templates) | the TypeScript / Angular type-checker | Test 4 for the frontend: rename a sampled method, property or component input; files that fail the type-check are what broke |
| Frontend call → backend endpoint | Kavita's committed `openapi.json` | every endpoint we extract should be in it; every linked frontend call should land on one that is |
| API data fields (C# `SeriesDto.Name` ↔ TS `Series.name`) | the same `openapi.json` | JSON field names per endpoint vs our naming-policy mapping and TS interface matches |
- Sonarr gets the history tests (1–2, 12k commits); CleanArchitecture a small hand-checked list of its endpoint links.
- Whole-stack breakage ("rename a backend field, which frontend code breaks") has no compiler answer;
  it's estimated from the two checks it's made of.
**Why:** the 🟡 cross-stack links are the ones no compiler checks; an OpenAPI file is a free, exact answer key.
**Cost:** the OpenAPI check only works where a project publishes its OpenAPI file.

## D33 · Phase 1c starts by hardening Test 2; 📜 is built only if history still helps — 2026-10-19
1b's "co-change beats the graph on Kavita" rests on too little to build on:
- 24 usable pairs on Kavita and 17 on Sonarr. On Sonarr, 9 of the 19 sampled pairs share one fix, so
  it's about 10 independent fixes.
- Kavita squash-merges mixed PRs ("A collection of bug fixes", "Polish 6"). Its pairs mostly link one
  bundle of edits to a later bundle, which co-change history naturally predicts.

So 1c's first step is the measuring stick, before any 📜 code:
- each fix counts once, however many change commits it pairs with;
- full history, not a 36-month window;
- nopCommerce and eShop pairs join Kavita and Sonarr;
- bundle PRs (Kavita's style) are reported as their own slice.

Then graph vs co-change is re-run. **If co-change adds nothing over "graph within 2 lines" on those
numbers, 1c stops there** and records why (the exact bar is part of 1c's done-when).
**Why:** a pass or fail on ~10–24 independent cases is noise; a day of benchmark work decides whether
the rest of 1c is worth building.
**Cost:** 📜 arrives later; the 1b history results are re-measured, not reused.

## D34 · 📜 links files, not members (members maybe later) — 2026-10-19
- A 📜 line joins two **file points already in the graph**: C#, TypeScript, Angular templates. Files
  that aren't on the map (`.resx`, `.sql`, `.csproj`, JSON config) are ignored for now.
- Only `git log --name-only` is needed: seconds, even on 12k commits, with dense evidence across the stack.
- Hub files (`Startup.cs`, a big service) are handled by thresholds and commit-size filters, not by going finer.
- **Follow-up, not in 1c:** member-level pairs, tried only if file-level 📜 proves too noisy on the hardened Test 2 (D33).
**Why:** member pairs need every past commit diffed and parsed (Roslyn and the Node extractor on old
code; minutes per build), give sparse evidence (two methods rarely change together twice), and can't reach templates.
**Cost:** coarse: "this file usually changes with that one" doesn't say which part of it.

## D35 · 📜 evidence is a count over each file's recent commits — 2026-10-19
- For a file A, look at **its last 20 commits** that survive the noise filters. A busy file is judged on
  recent months, a quiet one reaches further back; anything older counts zero. This is how "older
  history weighs less" (D26) is met without a decimal score.
- B is 📜 for A when B changed in **at least 3** of those commits **and at least 30%** of them.
- Evidence is shown as a count anyone can check with `git log`: "changed together in 7 of the last 12".
- Directional: A → B and B → A are judged separately. A hub as the starting point yields almost
  nothing (no partner reaches 30% of its many commits); a small file that nearly always drags `Startup.cs` along shows it.
- 20, 3 and 30% are starting defaults, tuned once on the hardened Test 2 (D33), then fixed in `stangent.yaml` (per repo, overridable).
- *Tuned 2026-10-19 (D43):* the rule picked **window 10, minShared 3** (minShare then never binds: 3 of ≤ 10 is ≥ 30%;
  its default is 0.2) and D37's size cap at **50** files. Pooled over nopCommerce and Sonarr, graph + 📜 recall 0.500 vs
  0.457 for the graph alone, and 📜's precision beat the plain baseline's on both (0.015 vs 0.012, 0.028 vs 0.026).
  The starting values (20/3/30%/30) missed by 0.001 on nopCommerce. The margins are small; the rule was fixed beforehand.
**Why:** D26 rejected made-up numeric confidence; a half-life decay produces exactly that ("3.41").
**Cost:** a hard cut at 20 commits ignores older evidence entirely; a file that changes in bursts can
have its whole window filled by one burst.

## D36 · 📜 counts changes on main's own line — 2026-10-19
- History is read with `git log --first-parent` on the graph's commit. A PR merged with a merge commit
  counts **once** (the merge, diffed against main before it); its branch commits aren't counted again.
  Squashed PRs and direct commits count once each. Closed or unmerged PRs aren't on main, so they're ignored.
- Refines D26's "no merges": with `--no-merges`, a PR of 8 commits that each touch the same two files
  counts as 8 pieces of evidence for one change.
- **Rebase-and-merge PRs** land as separate commits and count separately. Git keeps no reliable trace of
  which commits formed one PR: on Sonarr, six unrelated commits share one timestamp because a
  maintainer rebased them together, so grouping by timestamp would invent PRs. Rebasing a branch
  *locally* before a squash or merge changes nothing.
- **Advised, not required:** squash merges for our own repos (one PR = one commit; revert per ticket, D11,
  becomes one commit). Merge commits work as well; rebase-and-merge with messy commits ("wip", "fix typo") inflates 📜.
- Sonarr (rebased, tidy commits) is in the benchmark, so the effect is measured, not assumed.
- **Follow-up, not in 1c:** group commits by PR through the code-host adapter (D24; GitHub's commit → PR
  lookup), built only if a team's rebased history measurably hurts the scores.
**Why:** one logical change should count once, the same reason D33 counts each fix once.
**Cost:** rebase-merged history is counted per commit; the fix needs network and a token at build time.

## D37 · 📜 noise filters — 2026-10-19
Four filters, each checkable by code and reported as a count ("312 commits dropped: 40 huge, 251 bots, …"):
| Filter | Example | Rule |
|---|---|---|
| Huge commits | "Upgrade to .NET 10", Kavita's "Polish 6" | more than 50 files on the map (tuned with D35's; started at 30) |
| Bots | `dependabot[bot]`, Sonarr's "Automated API Docs update", Weblate translations | author ends in `[bot]`, plus a per-repo author list in `stangent.yaml` |
| Formatting sweeps | "Run dotnet format" | listed in the repo's `.git-blame-ignore-revs` (the file teams already keep for `git blame`); most are also huge |
| Reverts | "Revert 'Add series filter'" | a commit whose message says `This reverts commit <hash>`, **and** that commit: the change didn't stick, and keeping both counts it twice |
- Not filtered: lock files, `.csproj` and generated files (off the map, D34, so already invisible);
  EF Core migrations and model snapshots (true and useful: "changing `Series.cs` usually needs a migration").
- Rejected: diffing every commit to find whitespace-only changes (minutes per build instead of seconds).
**Cost:** a formatting sweep that isn't listed and isn't huge gets through; a bot not in the list
counts as a person until someone adds it.

## D38 · 📜 follows renames and moves, if it's cheap enough — 2026-10-19
- git's normal rename detection (`-M`) joins a moved file's old history to its current path, so a
  folder reorganisation doesn't wipe its 📜 evidence. Moving a C# file usually changes its `namespace`
  line, so exact-only matching would miss most moves.
- It needs the contents of commits that add and delete files; a blobless clone downloads them on demand.
- **Measured in 1c on nopCommerce.** If the first build gets more than ~30 s slower, 1c falls back to
  not following renames and records the gap.
- *Measured 2026-10-19:* nopCommerce has 1,838 renames of map files on its main line, so following them matters.
  Reading only the map's file types (`git log -- "*.cs" "*.ts" …`) keeps every one and cuts `git log -M` from
  7.1 s to 0.75 s on a full clone. A **blobless** clone is the exception: it fetches old contents over the network
  on *every* build (159 fetches, 34 s), so there renames aren't followed and the build warns. A clone with
  `--filter=blob:limit=200k` (315 MB vs 1.6 GB full) has them: 4.9 s on its first build, 0.8 s after.
**Cost:** a one-off download in blobless clones; renames git doesn't recognise (heavily edited in the same commit) still start fresh.

## D39 · 📜 lines live in the code graph (`code-graph` v3) — 2026-10-19
- A new line kind, `changes-with`, between file points in `.stangent/graph.sqlite`, labelled 📜, carrying its evidence count.
- Shared with the graph (D25): CI builds with full history, so a developer with a shallow clone still
  downloads 📜. "Same commit → same graph" holds: history up to a commit and the filter inputs
  (`stangent.yaml`, `.git-blame-ignore-revs`) are fixed by the commit.
- A shallow clone would build a different graph for the same commit, so the graph records it
  (`meta.history`: full or none) and `loadWarnings` says so.
- **D20:** 1c reopens 1a and bumps `code-graph` v2 → v3. 1b consumes v2, so it's marked `needs review`
  with a follow-up: confirm nothing in 1b breaks, then move it to v3.
- Rejected: a separate `history.sqlite` (two files for CI to share and impact to join, and D26 already defines 📜 as a graph line label).
**Cost:** a contract bump and a `needs review` on 1b for what is, for 1b, an additive change.

## D40 · 📜 in impact answers: its own group, last, capped, never walked — 2026-10-19
- **Never walked.** 📜 is looked up one step out from the changed files only; the walk never follows a 📜
  line or continues from a file found through one (chains of "usually" are how D27's 0.5% precision happened).
- **Its own group, after every ✅/🟡 group:** "usually changes together", top 10 by count, each with its
  evidence ("7 of the last 12"); `--expand history` shows the rest. A hint only, never blocking (D26).
- A file the graph already found keeps its ✅/🟡 place and gets an "also 📜" tag. Re-ranking by that
  tag waits until the benchmark shows "found by both" is clearly more precise.
- **Contract:** `impact-report` v3 → v4 (the group and the tag). Only 1a provides it and no phase consumes it yet.
- Rejected: mixing 📜 into the existing groups (hints among certain answers); corroboration only (drops
  the files only history finds, which is what co-change won with on Kavita).
**Cost:** one more group to read; a capped list can hide the 11th file that mattered.

## D41 · 📜 is recomputed in full, not incrementally — 2026-10-19
- 📜 is recomputed in full whenever the graph's commit or a filter input (`stangent.yaml`,
  `.git-blame-ignore-revs`) changes, and skipped when neither did.
- Reading the main line and counting pairs should take seconds (≤30 files per commit, so ≤~900 pairs each).
- Incremental history is built **only if nopCommerce's full recompute takes more than ~10 s**.
- Test 3 ("incremental = full", two builds byte-identical) covers 📜 with no extra work.
**Why:** incremental history has to handle force-pushes, branch switches, filter changes and D35's
sliding 20-commit window, each a correctness bug in waiting, to save a few seconds.
**Cost:** every new commit pays the full recompute.

## D42 · 📜 uses whatever history the clone has; CI's graph carries it — 2026-10-19
- `stangent graph build` never fails over a shallow clone. It records the history it used (`meta.history`:
  the commit range, or none) and warns when there's none.
- **CI builds the shared graph (D25) from a full-history clone without large files** (`fetch-depth: 0`,
  `filter: blob:limit=200k`), set in the thin workflow file (D24). `actions/checkout`'s default (depth 1) would
  ship graphs with no 📜. *Refined 2026-10-19:* `blob:none` was the first choice, but it can't follow renames
  without fetching on every build (D38, measured).
- No cut-off date: D35 reaches back 20 commits per file, and a quiet file needs years of history to fill that.
- **A local rebuild on a shallow clone keeps the downloaded 📜 lines**, marked with the commit they were
  computed at, rather than dropping them. A few commits stale is fine for a hint.
- Rejected: failing without full history (annoying for every developer); dropping 📜 on shallow builds
  (most local rebuilds would lose it silently).
**Cost:** the graph can carry 📜 a few commits older than its code; `meta.history` says by how much.

## D43 · 📜 is tuned on two repos and judged on two others — 2026-10-19
| Role | Repo | History | How changes land |
|---|---|---|---|
| Tune | nopCommerce | 16,295 commits since 2010 | direct + merges |
| Tune | Sonarr | 12,099 since 2010 | direct / rebased |
| Judge | Kavita | 3,452 since 2020 | squash bundles + direct |
| Judge | eShop | 349 since 2023 | squash |
- D35's and D37's defaults are chosen on the tuning repos only, then **frozen** before the judging repos are scored.
- That also tests whether defaults tuned on direct-commit repos survive squash-merge repos, which is
  what the company's repos (style unknown) will get.
- **Floor:** a repo with fewer than 30 independent Test 2 fixes is reported but doesn't count toward pass
  or fail. eShop will probably fall below it, so the verdict may rest on Kavita alone (bundles and all).
  Accepted; the report says so.
- CleanArchitecture stays out (a template with little history).
- *Refined 2026-10-19 (Gate 1 run):* Kavita has 23 usable fixes and eShop 1, so neither reaches the floor and
  Gate 2 had no judging repo. **bitwarden/server** joins the judging side: 7,963 commits, every one a squashed
  PR, ~15% of them fixes, a different team and style from both tuning repos. Kavita and eShop are still
  reported. Rejected: lowering the floor or raising Test 2's 15-file cap after seeing Kavita's number (moving
  the goalposts); splitting Sonarr by time (judging on the tuning repo's own team and style).
**Why:** tuning on the repos that are then scored measures the tuning, not 📜.
**Cost:** half the history is spent on tuning; a thin judging side.

## D44 · 1c's done-when: two gates, judged by code — 2026-10-19
Both gates use the hardened Test 2 (D33), each fix counted once.
- **Gate 1, before any 📜 code (all four repos):** "graph within 2 lines" vs "graph within 2 lines + the plain
  co-change baseline's top 10". If adding history raises recall on no repo with ≥ 30 fixes, 1c stops (D33).
- **Gate 2, the done-when (judging repos, D43, frozen defaults):**
  | Check | Bar |
  |---|---|
  | 📜 adds something | recall of graph + top-10 📜 beats graph alone, and the 95% range of the gain (resampling fixes, fixed seed) stays above zero |
  | Filters help, not hurt | 📜's precision ≥ the plain baseline's, and its recall ≥ 90% of the baseline's |
  | Answers stay short | the median answer grows by ≤ 10 files |
  | Cheap | ≤ 10 s added to a nopCommerce build (D41); ≤ 30 s added to the first build by rename following (D38) |
  | Stable | Test 3 holds: byte-identical builds, incremental = full |
- **If "adds something" fails,** 📜 ships **off by default** (`history: off` in `stangent.yaml`) and the
  report says why: D26's "a label that mostly adds false alarms is switched off", applied literally.
- Rejected: report-and-decide with no pre-agreed bar; also requiring graph + 📜 to beat plain co-change alone.
- *Result 2026-10-19:* Gate 1 passed (Sonarr, bitwarden/server). Gate 2 passed on bitwarden/server with every
  check (recall +0.025, range 0.009–0.047; 📜 precision 0.031 vs 0.022; +4 files; 0.9 s; stable), so 📜 ships on
  by default. Gap found: the tuning rule (D43) checked precision only, and with the frozen settings Sonarr misses
  Gate 2's recall check. Next tuning uses all of Gate 2's checks as eligibility.
**Why:** a pre-agreed bar can't drift toward whatever the numbers turn out to be.
**Cost:** with 30–60 fixes, a real but small gain may not clear "above zero"; strict on purpose.

## D45 · Before Phase 2, check the graph can do its Phase 4 jobs; splitting first — 2026-10-19
Every benchmark so far scores the graph's predictions; none shows it helps the agents of Phase 4. There it
has three jobs: **(1) split** a ticket into tasks that really are independent, **(2) scope** each subagent's
context to a slice, **(3) check** what a change may have broken (roughly Test 2).
- A pilot (phase 1d, D19 amended) runs before Phase 2. **Job 1 first:** take past multi-part changes, let the
  graph split them into "independent" groups, apply each group alone, and check it builds and passes tests.
  No agent runs: cheap, deterministic, fits the benchmark harness.
- **Job 2 next:** the same task given to a subagent with and without its graph slice; tokens, build/tests, misses.
- The bar is agreed before any run (as D44). A failure reshapes Phase 4 before Phases 2–3 are built on it.
- End-to-end test links (Playwright `goto` → pages) wait until the pilot reports.
  *Changed 2026-11-04 (D115):* built in 1.2, as separate lines nothing walks, so they can't affect what the pilot measures.
**Why:** the graph's accuracy doesn't show it makes agents safer or cheaper; 1c's gains were already small
(+2 to +6 points), and a bad split makes parallel subagents break each other.
- *Changed 2026-10-20:* Phase 2 may be **built** before the split check reports. The verdict decides Phase 4's
  shape (parallel or mostly sequential), not Phase 2's reference pages or FSD links. Phase 4 still waits for it,
  and part 2 (context scoping) may add to Phase 2 later. Phase 2 is built in its own worktree, so the running
  benchmark's binaries aren't rebuilt under it.
**Cost:** Phase 2 starts later; job 2 needs agent runs, which vary between tries and cost tokens.

## D46 · A split group "holds up" if it compiles alone, then passes the unit tests — 2026-10-19
Each group the graph calls independent is applied alone onto the change's parent commit.
- **Step A, every case:** it compiles (`dotnet build`; `tsc --noEmit` for frontends). A group that doesn't is an
  unsafe split: the graph missed a link.
- **Step B, groups that pass A:** the repo's unit-test projects run. A test that passes on the parent and on the
  full change but fails with the group alone is also an unsafe split.
- Integration tests (databases, services) are left out of the pilot.
Rejected: compile only (misses behaviour breaks); every test including integration (a test environment per repo
for little extra signal in a pilot).
**Why:** compiling is exact, cheap and repeatable (D18), and Test 4 already runs it; unit tests catch behaviour
breaks without infrastructure.
**Cost:** breaks only integration tests would catch go unseen; flaky unit tests must be screened out on the parent first.

## D47 · Split cases: recent main-line PRs from bitwarden/server and Sonarr — 2026-10-19
- **Source:** main-line commits (`--first-parent`, 1c's reader); in a squash-merge repo each is one merged PR,
  about a ticket's size.
- **Size:** ≥ 3 changed methods across ≥ 2 files, ≤ 15 files (Test 2's cap). Smaller has nothing to split; bigger
  is a refactor or a bundle.
- **Health:** the parent and the change both build and pass their unit tests; otherwise a failure says nothing
  about the split.
- **Repos:** **bitwarden/server** (squash-only, judged 1c) and **Sonarr** (adds `tsc` on a React frontend).
  nopCommerce is left out: slow builds, little extra.
- **Count:** newest first, examine cases until **30 are split** into 2+ groups, at most **200 examined** per repo.
  The split rate counts everything examined. "Safe" is judged on the plain percentage, with its 95% range
  reported; under 30 split cases, "safe" is below the floor for that repo (as D43).
- *Refined 2026-10-19:* first set at ~40 cases per repo, which gives only 10–16 split cases: too few to judge 90%.
**Why:** real changes of ticket size on repos whose history the benchmark already reads.
**Cost:** two repos, ~80 cases: a pilot's evidence, not a verdict for every codebase; recent cases lean towards
how those teams work today.

## D48 · Splits come from the after-change graph, joined within 2 steps (✅ + 🟡) — 2026-10-19
- **Graph:** built at the change itself, so added methods exist. This is the best case: more than a Phase 4
  planner knows before code is written. A pass doesn't prove Phase 4; a fail is decisive.
- **Groups:** changed methods joined by ✅ or 🟡 lines within 2 steps (through unchanged points too), in
  either direction, are one group. **Judged on this rule;** direct-only (1 step) and "+📜" are reported beside it.
- **Edits outside methods** (usings, fields, attributes) go with the group of the changed methods in their
  file; a new type is a point, so its users link to it.
- **Files with no changed methods** (`.csproj`, config, resources) form a shared group, applied together with
  every group, as Phase 4 would treat shared setup.
- *Refined 2026-10-19, while building:* these are mechanics, not a change to the rule.
  - **Owners.** A changed line belongs to the innermost member around it: method, property, field, or a TypeScript
    component. A line inside a type but outside its members makes the type a changed member only when it carries
    meaning (a header, base list or attribute; not a blank line, brace or comment).
  - **A changed type keeps its changed members.** Its text holds theirs, so a new class is never applied without
    its methods.
  - **Hunks** are cut into one piece per owner. Old and new runs of the same owner are matched; the rest become
    separate insertions and deletions, so any subset still applies as text. Only a line owned by two members at
    once (`int a, b;`) can't be cut, and it is counted as "joined by a shared hunk".
  - **Deleted members** join through the parent's graph with the same rule (1 or 2 steps).
  - *Fixed after bitwarden's first run:* 8 of its 14 failed splits were the tool's own cuts, sorted by a fresh agent
    (D61). The fixes:
    - A member whose signature changed is one member, matched by name without its parameter list.
    - A type or member that moved to another namespace or file is one member, when its name matches exactly one
      deleted and one added.
    - A type that exists on one side only is a changed member, so its members go with it.
    - A positional record's header line belongs to the type, not its properties.

    That run is still the one judged on, as bounds (D64).
  - **📜** comes from commits older than each change, as in 1c.
**Why:** Test 2 showed the graph's recall lives within 2 steps; direct-only misses too much.
**Cost:** within 2 steps joins more, so fewer groups; a file whose changed methods fall into two groups has its
other edits counted in both.

## D49 · The split check's done-when: safe, useful, better than split-by-folder — 2026-10-19
Agreed before any run (as D44), on the D48 rule:
| Check | Bar |
|---|---|
| Safe | of the cases split into 2+ groups, ≥ 90% have every group hold up alone (D46), **on each repo** |
| Useful | ≥ 25% of all cases are split into 2+ groups |
| Better than naive | safer than split-by-folder, whose split rate is no more than 5 points above the graph's |
- **Both pass:** Phase 4 plans parallel subagents from the graph; job 2 (context scoping) is next.
- **Safe fails:** Phase 4 doesn't trust the split: every parallel run is merged and built together before it
  counts, or tasks run one after another.
- **Useful fails:** parallel subagents seldom pay off; Phase 4 is mostly sequential.
**Why:** Phase 4 checks the merged result anyway (D18), so rare misses are caught; a 1-in-5 miss rate would
waste more than parallel runs save, and below 25% parallelism isn't worth building.
**Cost:** a pre-set bar can fail a split that would have been good enough in practice.

