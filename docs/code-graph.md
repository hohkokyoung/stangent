# Code graph (contract `code-graph` v3)

The map of the code that impact analysis walks (decisions D25–D28, frontends D30–D32, history D34–D42).
It's built by code from the code and its git history; it's never committed and has no AI in it. v2
(Phase 1b) added frontends: components, templates, pages, and the `renders` and `maps-field` lines.
v3 (Phase 1c) adds 📜 `changes-with` lines from git history and `meta.history`.

## Where it lives

- `.stangent/graph.sqlite`, git-ignored and tagged with the commit it was built from (D25). Its
  file layout has its own version (`meta.schemaVersion`, now 3). An older layout is rebuilt rather
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
| `project` (frontend) | `project:<path/to/tsconfig.json>` | the TypeScript extractor |
| `type`, `method`, `property`, `field` (TypeScript) | `ts\|<file>#<Name>`, e.g. `ts\|UI/Web/src/app/_services/series.service.ts#SeriesService.getSeries` | the TypeScript extractor |
| `component` | an Angular `@Component` class or a React function component (capitalised, returns JSX) | the TypeScript extractor |
| `template` | `ts\|<template file>#<Component>.template`: an Angular component's template, spanning its lines | the TypeScript extractor |
| `page` | `page:<tsconfig path>\|<route>`: an Angular Router or React Router route | the TypeScript extractor |

Test methods (xUnit, NUnit and MSTest attributes; `it`/`test` cases inside `describe` blocks, named
`Suite › case`) are marked `isTest`. Ids are the same ones the
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
| `calls-http` | calling method → endpoint in another service, or frontend code → backend endpoint | yes |
| `renders` | component → its template; template or JSX → a child component | yes |
| `maps-field` | frontend property → the backend property with the same JSON name | yes |
| `contains` | project → file → type → member, table → column | no (structure) |

### Labels

- ✅ `certain`: the compiler proved it.
- 🟡 `guessed`: a detector inferred it from a pattern.
- 📜 `usually changes together`: from git history (below). Never walked.
- ❓ **blind spots** are stored separately: a place, a reason and the detector that recorded it.
  Examples: reflection, `dynamic`, URLs and config keys built at runtime, unloaded projects,
  services registered with several implementations.

## History: 📜 `changes-with` (D34–D42)

A `changes-with` line from B to A says: B changed in `shared` of the last `of` commits that touched A
("changed together in 7 of the last 12"). Both are files on the map (files of `file` and `template`
points). Lines are stored apart from the others (`history_lines`; `"t":"history"` in exports), so the
impact walk never follows them.

- **Which commits:** main's own line, `git log --first-parent` from the graph's commit. A merged PR counts
  once, diffed against main before it; rebased PRs count per commit (D36). Moves and renames are followed,
  so a file keeps its history under today's path; a deleted path's history doesn't pass to a later file of
  the same name (D38). Only paths with the map's file types are read.
- **Noise filters (D37),** in this order, each counted in `meta.history.dropped`: `reverts` (a commit saying
  `This reverts commit <hash>`, and that commit), `formatting` (listed in `.git-blame-ignore-revs`), `bots`
  (author ending in `[bot]`, or listed in `history.botAuthors`), `huge` (more than `history.maxFiles` files on the map).
- **Counting (D35):** each file's last `window` surviving commits; B is 📜 for A when it changed in at least
  `minShared` of them and at least `minShare` of them. Each direction is judged separately.
- **Settings** (`stangent.yaml`, defaults shown): `history: { enabled: true, window: 20, minShared: 3, minShare: 0.3, maxFiles: 30, botAuthors: [], followRenames: true }`.
- **Recomputed in full** when the commit, the settings, `.git-blame-ignore-revs` or the files on the map
  change; otherwise reused (D41). About 1 s on nopCommerce's 16k commits.
- **`meta.history`:** `status` (`full`, `shallow`, `none` or `off`), the `commit` the lines were computed at,
  the `oldest` commit read, `commitsRead` (touching the map), `commitsUsed`, `dropped`, `renamesFollowed`, `fingerprint`, `note`.
- **Clones (D42):**
  - A shallow clone that already has 📜 lines (e.g. downloaded from CI, D25) keeps them, marked with the
    commit they were computed at. One without them uses what history it has. Either way it warns (`history_shallow`).
  - Following renames needs old file contents. Clone with `--filter=blob:limit=200k`
    (`actions/checkout`: `fetch-depth: 0`, `filter: blob:limit=200k`): small files are downloaded, large ones
    aren't. A blobless clone (`blob:none`) would fetch over the network on every build (34 s on nopCommerce),
    so there renames aren't followed and the build warns (`history_renames_skipped`).

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
| `frontend-http` | `wrappers: ["useApiQuery:path", "useApiMutation:path:method"]`: in-house call helpers as `name:urlProperty[:verbProperty]` (a plain `name` takes the URL as its first argument) |

`aspnetcore-routes` also takes `groupConventions: ["Ns.IEndpointGroup=/api/{type}"]` for route groups
mapped by convention rather than in code (CleanArchitecture registers each `IEndpointGroup` at
`/api/<ClassName>` by reflection). Values containing `{` must be quoted in YAML.

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

### Frontends (D30)

- **Found by:** a `tsconfig.app.json` or `tsconfig.json` beside a `package.json`, outside
  `node_modules`. A config that only `references` others uses the app one.
- **Restore:** packages are installed when `node_modules` is missing, using the lock file's tool.
  - Install scripts are never run (`--ignore-scripts`).
  - yarn and pnpm run through `npx` at the version `packageManager` pins.
  - An out-of-step `package-lock.json` falls back to an install that doesn't touch the lock file.
  - Without packages, calls are matched by name (🟡) and `loadWarnings` says so.
- **Extractor:** the bundled extractor (`ts-extractor/` beside the stangent assemblies, TypeScript
  6.0.3 and `@angular/compiler`) prints the frontend's fragment.
  - Node missing → `node_missing` in `loadErrors`; the C# graph still builds.
  - An extractor that prints nothing is an error, never an empty frontend.
- **Linking (`frontend-http`):**
  - **calls:** verb and URL from Angular `HttpClient`, `fetch`, axios and configured wrappers. A leading
    base URL (`environment.apiUrl`, `this.baseUrl`) is matched against the end of endpoint paths.
  - **generated clients** whose file isn't in the repo use NSwag's naming
    (`TodoListsClient.getTodoLists()` → the `GetTodoLists` action in `TodoLists`).
  - **fields:** typed results and bodies give `maps-field` lines, matched by JSON name (C# side:
    `[JsonPropertyName]`, `[JsonProperty]`, `[DataMember]`, else camelCase).

## Impact answers (contract `impact-report` v4, D27)

`stangent graph impact --symbol <id or Type.Member> | --file <path> | --diff <base..head or ref> [--expand <group>] [--max-depth N] [--format md]`

- **Groups, in order:** `endpoints`, `pages`, `eventHandlers`, `tables`, `tests`, `projects`, `code`. Each
  has a total, a ✅/🟡 split, a distance split and its first five items.
- **Nearest first.** Every item has a `depth`: how many lines it is from the change (1 = a direct
  dependent). Items are sorted by depth, then label. Groups count them as **direct** (1), **near** (2–3)
  and **far** (4+). The D28 scores showed why: direct dependents are far likelier to be hit, while
  following every caller of a caller reaches most of a layered app.
- **`--max-depth N`** leaves out everything beyond N lines.
- **Every ❓** in the affected code is listed, never counted.
- **`pages`** (v3) are frontend routes; components and templates are in `code`.
- **`history`** (v4, D40) comes after every group: the files that usually change together with the changed
  files (📜), one step out from them and never walked. It lists the top 10 by share ("7 of the last 12"),
  each with the changed file it goes with and whether the graph found it too (`found`: ✅, 🟡 or none);
  `--expand history` lists the rest. `--max-depth` doesn't cut it. Its `status` says when there's no history
  (`none`, `off`) or only a shallow clone's. Graph items whose file is one of them carry an "also 📜" `history`
  tag and keep their place. The changed files are those of the start points, the `--file` itself, or
  every file in the `--diff`.
- Every answer is validated against `schemas/impact-report.v4.json` before it's printed (v1–v3 are kept for history).

`stangent graph show <id or Type.Member>` lists a point's lines in and out, and its blind spots.
