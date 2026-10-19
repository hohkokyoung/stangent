---
phase: 1a
name: csharp-map
status: done
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1 }
provides: { code-graph: 1, impact-report: 2 }
---

# Phase 1a · C# map

**Approved 2026-10-19. Done 2026-10-19.**

Decisions this phase builds on: D4, D17, D25–D29. The goal is a deterministic, scored C# impact
analysis on nopCommerce and eShop. No AI anywhere in this phase (D18).

## What gets built

### 1. `src/Stangent.Graph`: the extractor (C#, uses Stangent.Core in-process, D23)
- **Loading.** Find every `.csproj` once, even when several solutions share it (D4), and load them
  with Roslyn's MSBuild workspace. `stangent graph build` runs `dotnet restore` first unless given
  `--no-restore`. A project that fails to load is reported by name, and its contents become ❓; the
  rest of the build carries on.
- **Points (D26):** project, file, class/interface/record/struct/enum, method/constructor, property/field;
  test methods (xUnit/NUnit/MSTest attributes) are marked as tests. IDs are Roslyn documentation-comment
  IDs, the same ones the link file uses (D10).
- **✅ lines from the compiler:** calls, object creation, member use, inherits, implements, overrides, tests.
- **Determinism:** sorted writes and no timestamps, so the same commit gives a byte-identical graph.

### 2. Link detectors (plugins, switched on in `stangent.yaml`, D17)
| Detector | Finds | Seen in |
|---|---|---|
| `aspnetcore-routes` | attribute routes, minimal APIs (`MapGet`, `MapGroup`) → ✅; conventional routes given as strings (`MapControllerRoute` defaults) → 🟡 | both |
| `microsoft-di` | `AddScoped/AddTransient/AddSingleton<I, T>` and similar → "wired to"; with several implementations, flags which one wins | both |
| `publish-handle` | **one configurable detector** for event systems: a publish method plus a handler interface. Covers nopCommerce (`IEventPublisher.PublishAsync` / `IConsumer<T>`) and eShop (`IEventBus.PublishAsync` / `IIntegrationEventHandler<T>`) with a few lines of config each, and in-house buses the same way | both |
| `entity-tables` | EF Core `DbSet<T>` and entity configuration → table/column; a configurable "entity base class → table" convention for ORMs like nopCommerce's linq2db | both |
| `config-keys` | `appsettings*.json` keys; `IConfiguration[...]`, `GetSection`, `GetValue`, options binding (`Configure<T>(GetSection("X"))` → `X:Property`) | both |
| `http-services` | typed `HttpClient`s and their base addresses → the service they call. Service names (`https+http://catalog-api`) are resolved through the Aspire AppHost when one exists → 🟡 links to that service's endpoints | eShop |

- **❓ blind spots** are recorded wherever a link can't be resolved: reflection, `dynamic`, strings built
  at runtime, unloaded projects, gRPC (its detector comes later).

### 3. Storage (D25)
- `.stangent/graph.sqlite` (git-ignored), tagged with its commit.
- `stangent graph export` / `import` write and read one file, which is how CI will share a graph.
  Publishing that file from CI comes with the CI adapter (Phase 7).
- **Incremental builds:** each project gets a fingerprint (its sources, its `.csproj` and the
  fingerprints of the projects it references). Only changed projects are re-extracted, and only their
  edges are re-linked. A test checks that an incremental build equals a full build.

### 4. Queries and impact answers (D27)
- `stangent graph impact --diff <base>..<head>` (the changed lines are mapped to the methods they're in),
  or `--symbol <id>`, or `--file <path>`.
- It walks back to the entry points (endpoints, event handlers, tables, config keys) and the covering
  tests, grouped by kind and by label, with counts. ❓ items are always listed one by one.
- `--expand <group>`, `--format json|md`.
- Also: `stangent graph show <id>`, `callers <id>`, `tests <id>`.

### 5. Contracts
- `code-graph` v1: the point and line kinds, the labels and the SQLite tables, documented in `docs/code-graph.md`.
- `impact-report` v1: `schemas/impact-report.v1.json`. Every impact answer is validated against it.

### 6. Scoring (D28) — `benchmark/`
- The harness gains a full-history mode for these repos.
- **Test 1, finds the rest of the change:** past PRs, given one file of each.
- **Test 2, predicts what broke:** change → later-fix pairs found from commit messages and issue references.
  Twenty pairs are hand-checked once.
- **Test 3, stable and fast:** two builds compared byte for byte; full and incremental times.
- Results are reported per label in the end-of-phase report. No targets yet (D28).

## Done when
- [x] `stangent graph build` succeeds on nopCommerce and eShop; projects that failed to load, if any, are listed.
      (eShop: its 3 MAUI projects need workloads this machine doesn't have; they're listed and their code is ❓.)
- [x] Building the same commit twice gives byte-identical graphs; an incremental build after a one-file change equals a full build and is faster.
- [x] `graph impact --symbol` on nopCommerce's `OrderProcessingService.PlaceOrderAsync` lists its endpoints and
      the `OrderPlacedEvent` consumers (Avalara, Brevo, Omnisend, Facebook Pixel and others), each with its label.
- [x] On eShop, impact on an ordering event reaches the handlers in the other services that consume it.
- [x] Every impact answer validates against `impact-report.v1.json`.
- [x] Scoring tests 1–3 run from `benchmark/` and their results are in the report.
- [x] Tests cover extraction, each detector (on small fixture projects), incremental = full, and impact grouping.
- [x] CI green ([run 39156170029](https://github.com/hohkokyoung/stangent/actions/runs/39156170029), ubuntu-latest); end-of-phase report written.

## Not in this phase
TypeScript and frontend links, API request/response fields (1b); 📜 git history (1c); gRPC; publishing
graphs from CI (Phase 7). Dropped from the plan: separate `callers` and `tests` commands, since
`graph show` and `graph impact --expand tests` already answer them.

When 1b and 1c extend the graph, they reopen this file and bump `code-graph` to v2 and v3 here, which
flags any consumer (D20).

## End-of-phase report

*2026-10-19 · first report; updated the same day when CI ran green on GitHub. Phase 1a is **done**.*

### 1. What was built
- **`src/Stangent.Graph`:** the project scanner (finds each `.csproj` once and fingerprints it); the
  Roslyn extractor (method-level points, ✅ calls/uses/inheritance/dispatch, ❓ for reflection,
  `dynamic` and unresolved calls); six detectors (`aspnetcore-routes`, `microsoft-di`,
  `publish-handle`, `entity-tables`, `config-keys`, `http-services`); incremental builds; SQLite
  storage and canonical export/import; impact analysis with Markdown output.
- **CLI:** `graph build | impact | show | export | import`.
- **Plugin:** `/stangent:impact`.
- **Contracts:** `code-graph` v1 (`docs/code-graph.md`) and `impact-report` v1
  (`schemas/impact-report.v1.json`). `cli-conventions` went to v2 (see Phase 0's reopened note).
- **Benchmark:** `benchmark/Stangent.Benchmark` (scoring tests 1–3), per-repo configs, full-history clones.
- **Tests:** 45, all passing. They include a 4-project fixture shop (`tests/fixtures/shop`) that
  exercises every detector without NuGet packages.

### 2. Benchmark results (`benchmark/results/2026-10-19-phase1a-*.json`)
Graph size: nopCommerce has 41 projects, ~44k points and ~178k lines; eShop has 28 projects, ~3.9k
points and ~9.9k lines.

| | eShop | nopCommerce |
|---|---|---|
| Full build (packages already restored) | 13 s | 38 s |
| Incremental build, one file changed | 2.3 s | 2.0 s |
| Two full builds byte-identical | ✅ | ✅ |
| Incremental = full | ✅ | ✅ |
| **Test 1** finds the rest of a commit: ✅ recall / precision | 0.09 / 0.16 (332 cases) | 0.28 / 0.005 (925 cases) |
| Test 1, ✅ + 🟡 | 0.09 / 0.13 | 0.28 / 0.005 |
| Test 1, plus the changed code's direct dependencies | 0.14 / 0.11 | 0.37 / 0.006 |
| **Test 2** predicts what broke: ✅ recall | 1 of 11 files (2 pairs) | 7 of 9 files (4 pairs) |
| Test 2, median predicted files | 8 | 1,023 |

How to read it:
- **Precision is low because of how scoring starts.** It starts from *whole files*, so a commit
  touching a core type such as `BaseEntity` marks most of nopCommerce as affected. Real use starts
  from changed *lines* (`--diff`), which is much narrower. Scoring from each commit's changed lines
  is the most important next measurement.
- **Test 1 has a ceiling.** Impact answers "what depends on this". Commits also edit what the
  changed code depends on, plus new tests and unrelated files. Adding direct dependencies raises
  recall from 0.28 to 0.37 on nopCommerce.
- **🟡 adds little to recall.** Guessed links mostly connect endpoints and handlers, not the
  ordinary files commits touch.
- **Test 2 is too small to draw conclusions from.** Of 68 fix commits, only 6 gave non-trivial
  change → fix pairs (24 fixes touched only their own change's files). The agent judged ~3–4 of the 6
  plausibly genuine, from commit subjects only (`handCheckSample` in the results). A person should
  re-check them.

The acceptance checks behave as specified:
- **nopCommerce:** `PlaceOrderAsync` → the checkout confirm endpoints and PayPal's approve endpoint
  (🟡, conventional routes), the 5 `OrderPlacedEvent` consumers named by plugin (Brevo, Omnisend,
  Avalara, Facebook Pixel, Google Analytics), and 8 ✅ callers.
- **eShop:** `Order.SetPaidStatus` → the domain-event handler → the integration-event handlers in
  Catalog.API, WebApp and Webhooks.API.

### 3. Decisions made or changed
- **Events are matched across services by type name (🟡).** eShop declares a copy of each integration
  event in every service, and the bus routes by name (D17 refined).
- **The impact walk treats event handlers as end points.** Past a handler it follows only further
  events (event chains), never the generic dispatcher. Without this, one change marked 2,534
  nopCommerce endpoints as affected (D27 refined).
- **Framework calls that merely fail to bind aren't ❓;** only calls with no candidate at all are.
- **`http-services` accepts `services: ["name=Project"]`** for service names without Aspire (e.g. Kubernetes).
- **The repo's SDK is checked before loading** (`sdk_missing`). The nopCommerce benchmark uses a
  `global.json` override: it pins SDK 10.0.401, which neither this machine nor CI has.
- **Graph rows are keyed by owner as well.** A file compiled into several projects (eShop's
  `src/Shared`) lost rows in incremental builds until this. It's caught by the shared-file fixture
  and by Test 3. The file layout is now version 2.

### 4. Known gaps
- **Precision on hub files** (above). The next step is scoring from changed lines, then deciding
  whether impact reports should cap or rank very large answers.
- **Changes downstream of a publisher.** Changing a method that *calls* a publisher doesn't reach that
  publisher's event handlers, because the walk only follows dependents.
- **Not built yet:** gRPC, `SendAsync` HTTP calls, and URLs or config keys assembled at runtime (all ❓).
- **Conventional routes are approximate.** nopCommerce's generic `{controller}/{action}` route
  creates an endpoint for every action (3,591 endpoints), all 🟡.
- **Scoring uses the pinned commit's graph for all of history.** Older commits are scored against code that has since moved.
- **Not covered by tests:** the EF Core path (`DbSet`, `ToTable`), because the fixture has no packages.
  It works on eShop (9 tables).
- Tested on macOS (local) and Linux (CI); not on Windows.

### 5. Impact on other phases
- `cli-conventions` v1 → v2 (opt-in `--format md`). Its only consumer, 1a, was updated with it.
- No other phase consumes `code-graph` or `impact-report` yet. 1b and 1c will extend `code-graph` (v2, v3).

### 6. Follow-ups
- ~~Push and confirm CI is green~~: done (the graph tests run on Linux too).
- Before 1b, the owner decides: score from changed lines (recommended), and tune precision if that
  shows it's needed.
- 1b (frontend + API shapes) starts with its own grilling (D22).

### Addendum 2026-10-19 · line-level scoring, baselines, exact check
Asked for after the first report. The scoring tool now:
- **Starts from changed lines.** It finds the methods each commit changed by name in the old
  file's syntax, then looks them up in today's graph.
- **Scores free baselines on the same cases:** same project, same folder, co-change.
- **Checks itself against exact graphs.** `--exact-sample` rebuilds the graph at past commits.

**The by-name method is sound.** On commits rebuilt exactly (eShop 15 of 30, the rest needed old
preview SDKs; nopCommerce 20 of 20), the by-name answer matched the exact one with median agreement
1.0 (mean 0.84 eShop, 0.88 nopCommerce), and recall and precision were almost identical. Scoring
old commits against today's graph costs little accuracy. Of 3,907 changed members, 3,362 were
found in the same file, 95 had moved, and 450 were unmatched (renamed or deleted).

**Test 1, ✅ + 🟡, recall / precision:**

| Predictor | nopCommerce (894 cases) | eShop (299 cases) |
|---|---|---|
| graph, unlimited depth | 0.23 / 0.005 | 0.08 / 0.13 |
| graph, depth ≤ 1 | 0.13 / 0.12 | 0.06 / 0.26 |
| graph, depth ≤ 2 | 0.17 / 0.07 | 0.07 / 0.18 |
| graph, plus direct dependencies | 0.33 / 0.007 | 0.14 / 0.12 |
| same folder | 0.16 / 0.04 | **0.17 / 0.34** |
| co-change | 0.09 / 0.11 | 0.05 / 0.23 |
| same project | 0.43 / 0.005 | 0.47 / 0.09 |

**What it means:**
- **Unlimited depth is the wrong default.** Callers of callers of callers reach most of a layered
  app, so precision collapses. Within 1–2 lines of the change, precision is 15–25× higher.
- **On nopCommerce, the graph within depth 2 beats the folder baseline** on both recall and precision.
- **On eShop it doesn't.** eShop is small and organised by feature folders, and its commits rarely leave the folder.
- **Test 1 measures what developers co-edited, which isn't what breaks.** A compatible change
  breaks nothing, so its dependents aren't edited, and folder-local commits favour the folder
  baseline. Test 2 asks the right question but has only 6 pairs. Neither test yet proves or
  disproves the graph's main claim: links across projects, services and events.

**Test 3** still holds after this change: identical full builds; incremental equals full.

### Reopened 2026-10-19 · `impact-report` v1 → v2
- **What changed:** impact answers list the nearest first. Each item has a `depth`, each group has
  direct/near/far counts, and there's an optional `--max-depth`.
- **Why:** the line-level scores above. Answers that follow every caller of a caller have very low
  precision; direct dependents have 15–25× more.
- **Consumers:** none yet (the `/stangent:impact` command shows the Markdown as it is).

### Addendum 2026-10-19 · compile-break ground truth (Test 4) and two extractor bugs
**Test 4.** For 40 sampled methods per repo, the benchmark renames each method in memory, recompiles
every project that depends on it with Roslyn, and records which other files now fail to compile.
That's the compiler's own answer to "what breaks", with no history and no AI.

| Predictor | nopCommerce (17 broken files) | eShop (39 broken files) |
|---|---|---|
| **graph ✅, direct only (depth ≤ 1)** | **recall 1.00 · precision 0.55** | **recall 0.97 · precision 0.76** |
| graph ✅, depth ≤ 2 | 1.00 · 0.11 | 0.97 · 0.48 |
| graph ✅, unlimited | 1.00 · 0.001 | 0.97 · 0.35 |
| same folder | 0.35 · 0.03 | 0.23 · 0.17 |
| same project | 0.77 · 0.002 | 0.85 · 0.008 |

- **The ✅ part of the graph is right.** Every file that failed to compile was predicted, except one
  in eShop's iOS project, which can't load without the MAUI workload.
- **Direct dependents are the right default view.** Further out, precision falls fast. That
  supports the `impact-report` v2 ordering.
- **Why direct precision isn't 1.0:** some direct dependents don't fail to compile on a rename. An
  implementation's interface, for example, or a caller that only passes the method as a delegate
  through an overload that still binds.

**Two extractor bugs Test 4 found, fixed with a failing test first:**
1. **Code in top-level statements (`Program.cs`) had no owner on the map.** The compiler-generated
   entry point has no documentation id, and the declaration scan skipped each file's root. It's now
   `<assembly>|M:Program.<Main>$(System.String[])`. Every minimal-API app starts there, so this
   mattered (eShop recall rose from 0.87 to 0.97).
2. **Incremental builds reused fragments made by an older extractor or with other detector
   settings.** Fingerprints now include the extractor build (module version id), the graph layout
   version and the detector settings.

The line-level table above and the exact check were re-run after these fixes. eShop moved by about
0.01 (top-level statements are now on the map); nopCommerce didn't change. The first report's table
(file-level scoring) is kept as it was measured.

**Conclusion.**
- **As a map of what breaks, the graph is proven** on both repos, and it beats every free baseline by a wide margin.
- **Where it's weak is long chains** (callers of callers). Those are now ranked by distance (`impact-report` v2), not dropped.
- **Co-edit Test 1 still favours "same folder" on eShop.** That says more about what developers edit
  together than about what breaks.
- **Not yet measured:** behavioural breakage (tests that fail), which belongs with Phase 5.

