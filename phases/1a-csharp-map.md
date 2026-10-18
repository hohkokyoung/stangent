---
phase: 1a
name: csharp-map
status: in progress
consumes: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 1, benchmark: 1 }
provides: { code-graph: 1, impact-report: 1 }
---

# Phase 1a · C# map

**Approved 2026-10-19. In progress.**

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
- [ ] `stangent graph build` succeeds on nopCommerce and eShop; projects that failed to load, if any, are listed.
- [ ] Building the same commit twice gives byte-identical graphs; an incremental build after a one-file change equals a full build and is faster.
- [ ] `graph impact --symbol` on nopCommerce's `OrderProcessingService.PlaceOrderAsync` lists its endpoints and
      the `OrderPlacedEvent` consumers (Avalara, Brevo, Omnisend, Facebook Pixel and others), each with its label.
- [ ] On eShop, impact on an ordering event reaches the handlers in the other services that consume it.
- [ ] Every impact answer validates against `impact-report.v1.json`.
- [ ] Scoring tests 1–3 run from `benchmark/` and their results are in the report.
- [ ] Tests cover extraction, each detector (on small fixture projects), incremental = full, and impact grouping.
- [ ] CI green; end-of-phase report written.

## Not in this phase
TypeScript and frontend links, API request/response fields (1b); 📜 git history (1c); gRPC; publishing
graphs from CI (Phase 7).

When 1b and 1c extend the graph, they reopen this file and bump `code-graph` to v2 and v3 here, which
flags any consumer (D20).

## End-of-phase report
_Not started._
