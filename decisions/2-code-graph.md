Short entries for what's still in force. Full text, and everything archived: [archive/2-code-graph.md](archive/2-code-graph.md).

# Decisions D25–D49: the code graph and the agent-fitness pilot

## D25 · The code graph is rebuilt from code, never committed — 2026-10-19
The graph lives in `.stangent/graph.sqlite` (git-ignored), tagged with its commit. CI builds it once per merge to `main` as a build output; people and agents download it and re-index only local changes. Same commit, same graph. Git holds only what people decide; the committed link file (D10) is checked in CI against a fresh graph.
**Why:** a committed graph means churn and merge conflicts on every PR, and CI must rebuild it to check it anyway. **Cost:** a fresh checkout has no graph until it downloads or indexes; old build outputs expire.

## D26 · What the code graph holds — 2026-10-19
Points at method level, plus entry points (endpoints, pages, events, tables) and tests. Also API data shapes and config keys, so breaks that aren't calls show up. Every line has a label: ✅ certain (compiler), 🟡 guessed (detector), 📜 usually changes together (git history, a hint only, file to file, D34–D36), ❓ unknown. No numeric confidence scores.
**Why:** labels a person can check, not made-up precision. **Cost:** a bigger graph and full clones for history; a label that mostly adds false alarms is switched off or tuned.

## D27 · Impact answers: summary first, details on request — 2026-10-19
An impact answer stops at entry points people know (endpoints, pages, event handlers, requirements, tests), grouped by kind and label with counts, nearest first, split direct / near / far; `--max-depth` cuts the rest. `--expand <group>` shows details. ❓ items are always listed one by one. Built by code, no AI; JSON for agents, Markdown for people. Past an event handler, only further events are followed.
**Why:** an agent reads about 15 lines, not hundreds. **Cost:** group counts can hide something important.

## D28 · How impact analysis is scored — 2026-10-19
All by code from git history, no AI or hand labels. Test 1: given one file of a past PR, find its other files. Test 2 (headline): does impact on a change include what a later fix touched. Test 3: builds are byte-identical, and timed. Test 4: rename a method, recompile, score against what fails to compile. Every test reports per label and against free baselines (same folder, co-change). Needs full history.
**Why:** a ground truth no one has to label. **Cost:** today's graph is used for past commits; a sample check measures that error.

## D30 · The TypeScript extractor runs in Node, inside the stangent tool — 2026-10-19
`stangent graph build` runs a bundled Node extractor with the real TypeScript compiler for each `tsconfig.json`, writing the same graph format; linking to backend endpoints happens in C#. It runs `npm ci` (or yarn/pnpm) when packages are missing; `--no-restore` skips it, and links then fall back to 🟡 name matching. Code it can't parse is ❓, never skipped silently.
**Why:** real types, one install. **Cost:** Node is required for frontend analysis; a first `npm ci` takes minutes.

## D31 · What the frontend map holds — 2026-10-19
From the TypeScript compiler (✅): files, classes, functions, members, calls, uses, React JSX. Angular templates through `@angular/compiler`: child components by selector (✅ if one match, 🟡 if several) and template bindings. Router routes are pages. Frontend → backend calls: hand-written HTTP calls 🟡, in-house wrappers named in `detectorSettings`, generated clients (NSwag first). Vue and Svelte wait until a repo needs them.
**Why:** cross-stack links are what breaks unseen. **Cost:** a larger extractor bundle.

## D32 · How frontend links are proven — 2026-10-19
TypeScript links: Test 4 for the frontend (rename, type-check). Frontend → backend calls and API field names: checked against a committed `openapi.json` (Kavita). Sonarr gets the history tests; CleanArchitecture a small hand-checked list. Whole-stack breaks have no compiler answer; they're estimated from these two checks.
**Why:** an OpenAPI file is a free, exact answer key for the 🟡 links no compiler checks. **Cost:** only works where a project publishes one.

## D33 · Test 2 is hardened before 📜 is judged — 2026-10-19
Test 2 counts each fix once, however many changes it pairs with; reads full history; and reports bundle PRs (mixed squash merges) as their own slice.
**Why:** a pass or fail on 10–24 independent cases is noise. **Cost:** earlier history results had to be re-measured. Part archived: the order of work (stop 1c if co-change adds nothing) is done (D44).

## D34 · 📜 links files, not members (members maybe later) — 2026-10-19
A 📜 line joins two file points already in the graph (C#, TypeScript, templates); files off the map are ignored. Only `git log --name-only` is read. Hub files are handled by thresholds and size filters. Member-level pairs only if file-level proves too noisy.
**Why:** member pairs need every old commit parsed, give sparse evidence and can't reach templates. **Cost:** coarse: it doesn't say which part of the file.

## D35 · 📜 evidence is a count over each file's recent commits — 2026-10-19
For file A, take its last `window` commits that pass the filters; B is 📜 for A when it changed in at least `minShared` and at least `minShare` of them. Each direction is judged on its own. Evidence reads like "changed together in 7 of the last 12". Tuned defaults (D43): window 10, minShared 3, minShare 0.2; per repo in `stangent.yaml`.
**Why:** older history weighs less without a decimal score. **Cost:** older evidence counts zero; one burst can fill a file's window.

## D36 · 📜 counts changes on main's own line — 2026-10-19
History is read with `git log --first-parent`: a merged PR counts once, squashed PRs and direct commits once each. Rebase-and-merge commits count one by one; git keeps no reliable trace of which formed a PR. Squash merges are advised, not required. Grouping by PR through the code host only if rebased history measurably hurts scores.
**Why:** one change should count once. **Cost:** rebase-merged history is counted per commit.

## D37 · 📜 noise filters — 2026-10-19
Four filters, each reported as a count: huge commits (more than 50 map files), bots (`[bot]` authors plus a per-repo list), formatting sweeps listed in `.git-blame-ignore-revs`, and reverts with the commit they revert. Migrations and model snapshots are kept on purpose.
**Why:** each filter is checkable by code. **Cost:** an unlisted, small formatting sweep or an unlisted bot gets through.

## D38 · 📜 follows renames and moves — 2026-10-19
git's rename detection (`-M`) joins a moved file's old history to its path, reading only the map's file types (fast on a full clone). A blobless clone would fetch over the network on every build, so there renames aren't followed and the build warns. Clones with `--filter=blob:limit=200k` follow them.
**Why:** nopCommerce has 1,838 renames on its main line. **Cost:** renames git doesn't recognise start fresh.

## D39 · 📜 lines live in the code graph — 2026-10-19
A `changes-with` line between file points, labelled 📜, with its evidence count, in `.stangent/graph.sqlite`. CI builds with full history, so shallow clones still get 📜 by download. The graph records the history it used (`meta.history`) and warns when there's none. No separate history file.
**Why:** one file to share and join; D26 already makes 📜 a line label. **Cost:** none left. Part archived: the `code-graph` v2 → v3 bump and 1b's review are done.

## D40 · 📜 in impact answers: its own group, last, capped, never walked — 2026-10-19
📜 is looked up one step from the changed files only; the walk never follows it. It's its own group after every ✅/🟡 group, top 10 by count with evidence; `--expand history` shows the rest. A file already found gets an "also 📜" tag; re-ranking by it waits for proof. Never blocking.
**Why:** chains of "usually" are how precision fell to 0.5%. **Cost:** a capped list can hide the 11th file that mattered.

## D41 · 📜 is recomputed in full, not incrementally — 2026-10-19
📜 is recomputed whenever the graph's commit or a filter input (`stangent.yaml`, `.git-blame-ignore-revs`) changes, and skipped otherwise. Incremental history only if nopCommerce's full recompute passes about 10 s. Test 3 covers it.
**Why:** incremental history invites bugs (force-pushes, branch switches, the sliding window) to save seconds. **Cost:** every new commit pays the full recompute.

## D42 · 📜 uses whatever history the clone has; CI's graph carries it — 2026-10-19
`graph build` never fails on a shallow clone; it records the history used and warns when there's none. CI builds the shared graph from a full-history clone with `fetch-depth: 0` and `filter: blob:limit=200k`. No cut-off date. A local rebuild on a shallow clone keeps the downloaded 📜 lines, marked with their commit.
**Why:** failing would annoy every developer; dropping 📜 would lose it silently. **Cost:** 📜 can be a few commits older than the code.

## D43 · 📜 is tuned on some repos and judged on others — 2026-10-19
Defaults are chosen on the tuning repos (nopCommerce, Sonarr), then frozen before the judging repos are scored (bitwarden/server; Kavita and eShop reported only). A repo with fewer than 30 independent Test 2 fixes doesn't count toward pass or fail. The floor and caps are never moved after seeing a number.
**Why:** tuning on the scored repos measures the tuning, not 📜. **Cost:** half the history goes to tuning; a thin judging side.

## D44 · 📜 passed its gate and ships on by default — 2026-10-19
📜 passed Gate 2 on bitwarden/server: it adds recall with a 95% range above zero, beats the plain baseline's precision, grows answers by ≤ 10 files, costs ≤ 10 s, and builds stay identical. If a retune fails "adds something", 📜 ships off (`history: off`). Any retune uses all of Gate 2's checks as eligibility, not precision alone (Sonarr misses the recall check today).
**Why:** a bar agreed first can't drift. **Cost:** a small real gain may not clear it. Part archived: Gate 1 and the run order are done.

## D45 · The graph's Phase 4 jobs are checked before Phase 4 is built — 2026-10-19
In Phase 4 the graph has three jobs: split a ticket into independent tasks, scope each subagent's context, check what a change may have broken. Pilot 1d checks them with bars agreed before any run. Splitting is reported (fails safe, D64). Context scoping (a subagent with and without its graph slice) is still owed; Phase 4 waits for the pilot.
**Why:** the graph's accuracy doesn't show it makes agents safer or cheaper. **Cost:** context scoping needs paid agent runs that vary. Part archived: "before Phase 2" was dropped on 2026-10-20; Playwright links went to D115.

## D46 · A split group "holds up" if it compiles alone, then passes the unit tests — 2026-10-19
Each group the graph calls independent is applied alone onto the change's parent. It must compile (`dotnet build`, `tsc --noEmit`), then pass the repo's unit tests where the parent and the full change pass. Integration tests are left out.
**Why:** compiling is exact and repeatable; unit tests catch behaviour breaks without infrastructure. **Cost:** breaks only integration tests would catch go unseen; flaky tests must be screened on the parent.

## D47 · Split cases: recent main-line PRs from bitwarden/server and Sonarr — 2026-10-19
A case has ≥ 3 changed methods across ≥ 2 files and ≤ 15 files; the parent and the change both build and pass their tests. Examine until 30 are split, at most 200. Phase 1d part 1 used it; any later split-rule test (D65) uses the same rules with the 1d harness. **Why:** real changes big enough to split, small enough to check. **Cost:** two repos only.

## D48 · Splits come from the after-change graph, joined within 2 steps (✅ + 🟡) — 2026-10-19
The split harness builds the graph at the change itself (the best case: a pass doesn't prove Phase 4, a fail is decisive). Changed methods joined by ✅ or 🟡 lines within 2 steps, either way, form one group. Edits outside methods go with their file's group. Files with no changed methods form a shared group applied with every group. The cutter's mechanics are in the archive and `phases/1d-agent-fitness.md`.
**Why:** Test 2 showed the graph's recall lives within 2 steps. **Cost:** fewer, larger groups.

## D49 · The split check's done-when, and what its fail means — 2026-10-19
Bars, agreed first: safe (≥ 90% of split cases hold up, on each repo), useful (≥ 25% of cases split), better than split-by-folder. Safe failed (D64), so Phase 4 doesn't trust the graph's split: parallel runs are merged and built together before they count, or tasks run one after another. Useful is unmeasured.
**Why:** a 1-in-5 miss rate wastes more than parallel runs save. **Cost:** a pre-set bar can fail a split that was good enough in practice.
