# Code graph (contract `code-graph` v1)

The map of the code that impact analysis walks (decisions D25–D28). It's built by code from the
code; it's never committed and has no AI in it.

## Where it lives

- `.stangent/graph.sqlite`, git-ignored and tagged with the commit it was built from (D25). Its
  file layout has its own version (`meta.schemaVersion`, now 2). An older layout is rebuilt rather
  than migrated.
- `stangent graph export <file>` / `import <file>` write and read one gzip'd, canonical JSON-lines
  file. Two exports of the same graph are byte-identical; this is the unit CI will share.

## Points

Every point has an id, a kind, a display name, its project, and a file and line span when it has one.

| Kind | Id | From |
|---|---|---|
| `project` | `project:<path/to/X.csproj>` | the compiler |
| `file` | `file:<path>` | the compiler |
| `type`, `method`, `property`, `field` | `<assembly>\|<documentation-comment id>`, e.g. `Nop.Services\|M:Nop.Services.Orders.OrderProcessingService.PlaceOrderAsync(…)` | the compiler |
| `endpoint` | `endpoint:<assembly>\|<VERB> <route>` | `aspnetcore-routes` |
| `table`, `column` | `table:<assembly>\|<name>`, `column:<assembly>\|<table>.<column>` | `entity-tables` |
| `config` | `config:<assembly>\|<Key:Path>` | `config-keys` |

Test methods (xUnit, NUnit and MSTest attributes) are marked `isTest`. Ids are the same ones the
link file uses (D10), so requirements can point at them.

## Lines

Lines point **from the dependent to what it depends on**. "What does a change to X affect?" means
everything that can reach X by following lines backwards.

| Kind | Meaning | Followed by impact |
|---|---|---|
| `calls`, `uses` | code → code it calls or uses | yes |
| `dispatches-to` | interface or virtual member → each implementation | yes |
| `inherits`, `implements`, `overrides` | derived → base | only from the changed points themselves |
| `wired-to` | DI service type → its implementation | yes |
| `routes-to` | endpoint → action or handler | yes |
| `triggered-by` | event handler → each method that creates the event | yes; past a handler, only further events (event chains) |
| `maps` | table → entity type; column → property | yes |
| `reads-config`, `binds` | code or settings property → config key | yes |
| `calls-http` | calling method → endpoint in another service | yes |
| `contains` | project → file → type → member, table → column | no (structure) |

### Labels

- ✅ `certain`: the compiler proved it.
- 🟡 `guessed`: a detector inferred it from a pattern.
- 📜 `usually changes together`: from git history (Phase 1c).
- ❓ **blind spots** are stored separately: a place, a reason and the detector that recorded it.
  Examples: reflection, `dynamic`, URLs and config keys built at runtime, unloaded projects,
  services registered with several implementations.

## Detectors (D17)

Detectors are switched on per repo in `stangent.yaml`. `detectors: []` (the default) runs all of
them; list names to run only those; `[none]` runs none. Per-detector settings go in `detectorSettings`:

| Detector | Settings |
|---|---|
| `aspnetcore-routes` | — |
| `microsoft-di` | — |
| `publish-handle` | `handlerInterfaces: [Ns.IHandler`1]`, added to the built-ins (nopCommerce, eShop, MediatR, MassTransit) |
| `entity-tables` | `entityBaseTypes: [Ns.BaseEntity]`, for ORMs mapped by convention |
| `config-keys` | — |
| `http-services` | `services: ["stock-api=Shop.Stock"]`, for service names without an Aspire AppHost |

Each detector extracts facts per project (with Roslyn), then links once across the whole graph
(without Roslyn). That split is what lets incremental builds re-extract only changed projects.

## Building

`stangent graph build [--no-restore] [--full]`

1. Find every `.csproj` once and fingerprint it: its own files, the shared build files above it, and
   the fingerprints of the projects it references.
2. Check that the SDK the repo's `global.json` asks for is installed (`sdk_missing` otherwise).
3. Restore (unless `--no-restore`), then load only projects whose fingerprint changed.
4. Extract, link, save.

A project that can't be loaded is listed in `loadErrors` and its code is ❓. MSBuild warnings
(NuGet audit, for example) are reported as `loadWarnings` and don't stop the build.

## Impact answers (contract `impact-report` v2, D27)

`stangent graph impact --symbol <id or Type.Member> | --file <path> | --diff <base..head or ref> [--expand <group>] [--max-depth N] [--format md]`

- **Groups, in order:** `endpoints`, `eventHandlers`, `tables`, `tests`, `projects`, `code`. Each
  has a total, a ✅/🟡 split, a distance split and its first five items.
- **Nearest first.** Every item has a `depth`: how many lines it is from the change (1 = a direct
  dependent). Items are sorted by depth, then label. Groups count them as **direct** (1), **near** (2–3)
  and **far** (4+). The D28 scores showed why: direct dependents are far likelier to be hit, while
  following every caller of a caller reaches most of a layered app.
- **`--max-depth N`** leaves out everything beyond N lines.
- **Every ❓** in the affected code is listed, never counted.
- Every answer is validated against `schemas/impact-report.v2.json` before it's printed (v1 is kept for history).

`stangent graph show <id or Type.Member>` lists a point's lines in and out, and its blind spots.
