---
phase: r2
name: graph-for-flows
status: in progress
consumes: { code-graph: 4 }
provides: {}
---

# Rebuild layer 2: is the code graph ready for flows? (D156, D154, D159)

Checked 2026-11-08 on **Sonarr only** (owner's call: Kavita and nopCommerce skipped to save tokens).
No paid AI. Graph built with this branch's code at `be858cd` on Sonarr commit `0bffefe`:
38,898 points, 180,020 lines, 96 blind spots, no load errors, ~50 s.

## Bottom line

The "Add series" path is **mostly there** from screen to database, but three breaks would make a
flow wrong, not just thin: the endpoint can't tell Add from Delete (verbs lost), the call is linked to
two API versions, and the database write stops at a generic `Insert` with no table. Labels, validation
rules and authorisation are **not read at all**.

## 1. Add series: click → database

| Step | Result | Evidence |
|---|---|---|
| Route → screen | works | `page:tsconfig.json\|/add/new` renders down to `AddNewSeriesModalContent` in 4 `renders` steps |
| Button → handler | **missing** | `onPress={handleAddSeriesPress}` (line 367) is a `useCallback` inside the component, not a point; the graph only knows the whole component calls `useAddSeries` |
| Handler → HTTP call | partly | `useAddSeries` (a react-query hook, not Redux: Sonarr moved this screen off Redux) `calls-http` 🟡 → `ANY /api/v5/Series` **and** `ANY /api/v3/Series`. The code says `method: 'POST'`, and `apiRoot = '/api/v5'` |
| HTTP → endpoint | **wrong** | `ANY /api/v5/Series` routes to `AddSeries`, `DeleteSeries`, `UpdateSeries`, `GetResourceByIdWithErrorHandler`. Sonarr's `[RestPostById]` derives from `HttpPostAttribute`, `[RestGetById]` implements `IActionHttpMethodProvider`; the detector doesn't read either, so verb and `{id}` are lost |
| Endpoint → service | works | `SeriesController.AddSeries` → `IAddSeriesService.AddSeries` → `AddSeriesService.AddSeries` → `SeriesService.AddSeries` (depth 1–4, `calls` + `dispatches-to`) |
| Service → database write | partly | reaches `IBasicRepository`1.Insert` → `BasicRepository`1.Insert` (depth 5–6), but as the open generic. **No `table` points exist in Sonarr** (`select count(*) from nodes where kind='table'` = 0): Sonarr maps tables with its own Dapper `TableMapping`, which `entity-tables` doesn't read |
| Published event | works | `triggered-by` gives `SeriesAddedHandler.Handle`, `SceneMappingService.Handle`, `TrackedDownloadService.Handle` from `SeriesService.AddSeries`; the event type is in `facts` (`handle`), not on the line |
| Outside call | **missing, silently** | `AddSkyhookData` → `IProvideSeriesInfo.GetSeriesInfo` → SkyHook over `_requestBuilder.Create()…` + `_httpClient.Get`. No `call` fact, no `calls-http` line, **and no blind spot** for it |

## 2. Facts code can read

| Need | Result | Evidence |
|---|---|---|
| Button's visible label | missing | `translate('AddSeriesWithTitle', { title })` is only a `calls` → `translate`; the key and its `en.json` text are not stored (`en.json` is already in `tickets.textFiles`) |
| Authorisation markers | missing | `[Authorize(Policy="UI")]`, `[AllowAnonymous]` exist in `Sonarr.Http`; no fact kind records attributes. Sonarr's frontend has no route guards to read |
| Validation rules | missing | `SeriesController` builds 15 FluentValidation rules (`PostValidator.RuleFor(s => s.TvdbId).GreaterThan(0)`, `.Title).NotEmpty()`…); none recorded. Fact kinds are only `call, handle, bind, read, endpoint-types, json-shape, shape` |
| Constants as facts | partly | constants are points with `uses` lines (`kinds.PRIMARY`, `inputTypes.TAG`), but their values aren't stored |
| Writes vs reads | missing | no tables, so no write/read split; repository method names (`Insert` vs `Get`) aren't classed |
| Outside calls | partly | `http-services` finds literal URLs (`call` facts: 103, mostly tests); builder-made URLs are missed with no blind spot |
| Published events | works | 1,188 `triggered-by` lines; 313 `handle` facts |

## 3. Blind spots that break a flow's path

Ranked by how badly they break a flow:

1. **Custom verb attributes → `ANY`** (no blind spot recorded): one endpoint stands for four actions.
2. **Base URL from an exported constant** (`apiRoot`): every frontend call matches v3 and v5.
3. **Generic repository with no table**: the write's target is unknown.
4. **Builder-made outside URLs**: missing with no blind spot, so a flow looks complete when it isn't.
5. **Inline handlers**: a screen with several buttons can't say which button does what.
6. Recorded blind spots (96: reflection 38, runtime-built frontend URLs 17, config 2…) don't touch this flow.

## Gaps for the owner (nothing fixed yet, D159)

All fixes are code only (D155), no paid AI. Cost is build effort in this repo.

| # | Gap | Recommended fix | Rough cost |
|---|---|---|---|
| G1 | Verbs lost on custom attributes | read `IActionHttpMethodProvider` / `HttpMethodAttribute` base types and `IRouteTemplateProvider` templates through Roslyn, not attribute names | small |
| G2 | Two API versions matched | the extractor resolves an exported string constant used as base URL; match only that prefix | small |
| G3 | No table, generic `Insert` | read Sonarr-style `Mapper.Entity<T>("Name")` table maps; resolve `BasicRepository<T>` calls to `T`'s table; class methods as write/read by a fixed name list | medium |
| G4 | Builder-made outside URLs silent | record a blind spot whenever an HTTP client method is called with a non-literal request; resolving the host from config is later | small (blind spot only) |
| G5 | Button → handler | make inline `const handleX = …` handlers points, and JSX `onX={handleX}` a line from the element to it | medium |
| G6 | Button label | store `translate('Key')` keys on the element and look them up in the configured text files | small–medium (after G5) |
| G7 | Validation rules | FluentValidation `RuleFor(x => x.P).Rule(args)` and DataAnnotations as facts: property, rule, constant | medium |
| G8 | Authorisation | `[Authorize]`/`[AllowAnonymous]`/policy as facts on endpoints, plus the app's fallback policy | small |
| G9 | Constant values | store literal values of `const` fields and exports | small |

Recommended order: G1, G2, G4 first (they make a flow **wrong** or **silently short**), then G3, then G5–G9.
After the chosen fixes, one narrow fresh check (D158) of this Add-series path.

Not checked: Kavita (Angular route guards, Angular templates) and nopCommerce (Razor views, D159).
