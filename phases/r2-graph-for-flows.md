---
phase: r2
name: graph-for-flows
status: in progress
consumes: { code-graph: 5 }
provides: {}
---

# Rebuild layer 2: is the code graph ready for flows? (D156, D154, D159)

Checked 2026-11-08 on **Sonarr** (React + .NET, Dapper) and **Kavita** (Angular + .NET, Entity Framework),
so each gap can be called Stangent-wide or one app's habit. nopCommerce (Razor) not checked yet (owner:
save tokens; Razor is its own step). No paid AI. Graphs built with this branch's code at `be858cd`:

| Repo | Commit | Points | Lines | Blind spots | Load errors | Time |
|---|---|---|---|---|---|---|
| Sonarr | `0bffefe` | 38,898 | 180,020 | 96 | 0 | ~50 s |
| Kavita | `2d22536` (pinned, shallow clone, no 📜) | 32,358 | 153,841 | 35 | 0 | ~70 s |

## Bottom line

Both paths reach from the screen to the save. Kavita's frontend half is clean (exact verb and URL);
Sonarr's breaks on its own verb attributes and base-URL constant. The **Stangent-wide** gaps are on the
back half and in the facts: which table a save writes, outside calls (missing with no blind spot),
events sent to the browser, and labels, validation and authorisation, which aren't read on either app.

## 1. One action per repo: click → database

**Sonarr, "Add series"** (`AddNewSeriesModalContent`, button `onPress={handleAddSeriesPress}`).
**Kavita, the star button "Add to Want to Read"** (`series-detail.component.html:99`, `(click)="toggleWantToRead()"`).

| Step | Sonarr | Kavita |
|---|---|---|
| Route → screen | works: `page:tsconfig.json\|/add/new` → modal in 4 `renders` | works (not re-traced; 51 pages, 303 templates) |
| Button → handler | **missing**: inline `useCallback` isn't a point; only "component calls `useAddSeries`" | partly: `SeriesDetailComponent.template` `calls` `toggleWantToRead`, but not from which element (the template has 180 lines out) |
| Handler → HTTP call | partly: `useAddSeries` (react-query, not Redux) 🟡 → `ANY /api/v5/Series` **and** `ANY /api/v3/Series`; code says `POST`, `apiRoot = '/api/v5'` | works: `toggleWantToRead` → `ActionService.addMultipleSeriesToWantToReadList` → `MemberService.addSeriesToWantToRead` 🟡 → `POST /api/want-to-read/add-series` only |
| HTTP → endpoint | **wrong**: `ANY /api/v5/Series` routes to Add, Delete, Update and Get (`[RestPostById]` derives from `HttpPostAttribute`, `[RestGetById]` implements `IActionHttpMethodProvider`; neither read) | works: → `WantToReadController.AddSeries` |
| Endpoint → service | works: → `IAddSeriesService` → `AddSeriesService` → `SeriesService.AddSeries` | works: → `IUserRepository.GetUserByUsernameAsync`, `HasAccessToSeries`, `IUnitOfWork.CommitAsync` |
| Save → table | partly: reaches `BasicRepository`1.Insert` as the open generic; **0 table points** (Dapper `TableMapping` not read) | partly: `uses` `AppUserWantToRead` + `AppUser.WantToRead`, but **no table `AppUserWantToRead`**: it has no `DbSet`, only a navigation and `ToTable` in the model snapshot. 62 tables found |
| Write vs read | missing: `Insert` not classed as a write | missing: the write is `user.WantToRead.Add(…)` + `CommitAsync`; nothing marks it a write |
| Events | works: `triggered-by` → `SeriesAddedHandler.Handle` and 2 others | missing: 0 `triggered-by`; Kavita sends events to the browser with SignalR (`SendMessageAsync(MessageFactory…)`, 34 files), not read |
| Background work | — | partly: `BackgroundJob.Enqueue(() => ScrobbleWantToReadUpdate…)` is a plain `calls`, not marked "runs later" |
| Outside call | **missing, silently**: SkyHook via `_requestBuilder.Create()` + `_httpClient.Get`, no fact, no blind spot | **missing, silently**: Flurl in 23 files; no C# outside-call fact or blind spot (the 417 `call` facts are the frontend's) |

## 2. Facts code can read

| Need | Sonarr | Kavita |
|---|---|---|
| Visible label | missing: `translate('AddSeriesWithTitle')` key not stored | missing: `t('add-to-want-to-read')` in `ngbTooltip` not stored (key in `assets/langs/en.json`) |
| Authorisation | missing: `[Authorize(Policy="UI")]`, `[AllowAnonymous]` not stored | missing: `[Authorize]` on `BaseApiController` not stored |
| Route guards | none in the app | partly: `routes` `uses` `authGuard`, but not which route has it (`canActivate` at lines 16, 58, 82) |
| Validation | missing: 15 FluentValidation rules in `SeriesController` | missing: 103 DataAnnotations in DTOs (`[Required]`, `[Range]`…); this action's DTO has none |
| Constant values | partly: constants are points, values not stored | same |
| Published events | works (`IHandle<T>`, 1,188 lines) | missing (SignalR) |

## 3. Combined gap list (nothing fixed yet, D159)

All fixes are code only (D155), no paid AI. Cost is build effort. "Where" says what the two apps show.

| # | Gap | Where | Recommended fix | Rough cost |
|---|---|---|---|---|
| G1 | Custom verb attributes → `ANY` | Sonarr only | read `IActionHttpMethodProvider` / `HttpMethodAttribute` base types and `IRouteTemplateProvider` through Roslyn | small |
| G2 | Base URL from an exported constant | Sonarr only | resolve an exported string constant used as base URL | small |
| G3 | Save doesn't reach a table | **both** (Dapper map; EF tables without `DbSet`) | EF: entities reached through navigations and `ToTable`; Dapper: `Mapper.Entity<T>("Name")`; generic `Repository<T>` calls resolve to `T`'s table | medium |
| G3b | Write vs read | **both** | a fixed rule: repository `Insert/Update/Delete`, EF `Add/Remove` on a set or navigation + `SaveChanges/Commit` = write; else read | small, after G3 |
| G4 | Outside calls silent | **both** (request builder; Flurl) | blind spot for any HTTP client call with a non-literal URL, plus Flurl's string-extension calls as `call` facts | small–medium |
| G5 | Button → handler | **both** (React inline handlers; Angular template-level only) | React: inline handlers become points; both: the element (`onPress`/`(click)`) becomes the line's source | medium |
| G6 | Button label | **both** | store `translate('k')` / `t('k')` keys on the element; look them up in configured text files | small–medium, after G5 |
| G7 | Validation rules | **both** (FluentValidation; DataAnnotations) | rules as facts: property, rule, constant | medium |
| G8 | Authorisation and guards | **both** | `[Authorize]`/`[AllowAnonymous]`/policy on endpoints (inherited from base controllers); Angular `canActivate` per route | small |
| G9 | Constant values | **both** | store literal values of `const` fields and exports | small |
| G10 | Events to the browser (SignalR) | Kavita seen; Sonarr also has SignalR | hub sends as `publishes` facts: message name, from where | small–medium |
| G11 | Background jobs look like direct calls | Kavita (Hangfire) | mark `BackgroundJob.Enqueue/Schedule` lambdas as "runs later" | small |

Recommended order: **G4, G3 + G3b, G8, G5 + G6, G7** (Stangent-wide, they make a flow silently short or
leave out what a user sees), then G9–G11, then G1 and G2 (one app's habit, small, but Sonarr is the bench).
After the chosen fixes, one narrow fresh check (D158) of these two paths, cost said first.

## 4. Fixed on the owner's go (2026-11-08): G1–G8, `code-graph` v5

Owner: "start batch" with G1 and G2 added. Code only (D155), no paid AI. Contract `code-graph` → v5; 1a reopened;
1b, 1c, 1d, 2a, 2b, 2c and 3 marked `needs review` (D20), none of their code changed. Tests: 2,155 .NET + 45 extractor, all pass.

| # | What changed | Before → after on the real graphs |
|---|---|---|
| G1 | verbs from attributes derived from `HttpXAttribute` or implementing `IActionHttpMethodProvider`; templates from a base constructor or `Template` property; overrides of `[NonAction]` aren't actions | Sonarr `ANY /api/v5/Series` (→ Add, Delete, Update, Get) → `POST /api/v5/Series` → `AddSeries` only; `ANY` endpoints 84+ → only real ones. **Endpoint ids changed** |
| G2 | a wrapper's own body shows the API root it adds | `useAddSeries` → v3 **and** v5 → v5 only; frontend calls to v3: 5 left (other wrappers) |
| G3 | EF tables reached through navigations; `Entity<T>("Name")` maps; `writes`/`reads` lines (not walked) | Sonarr tables 0 → 41, `SeriesService.AddSeries` `writes` `Series`; Kavita 62 → 66, `WantToReadController.AddSeries` `writes` `AppUserWantToRead` |
| G4 | outside calls through Flurl or an app's own client: a `call` fact or ❓ | blind spots Sonarr 96 → 328, Kavita 35 → 116 (Sonarr: 110 of the new ones are in tests); `SkyHookProxy.GetSeriesInfo` now ❓ "outside HTTP GET through IHttpClient" |
| G5 | a React component's own handlers are points it `uses`; a handler using a hook's result `calls` the hook; `ui-event` facts | Sonarr `SpinnerErrorButton onPress` → `AddNewSeriesModalContent.handleAddSeriesPress` → `calls` `useAddSeries`; Kavita `button click` → `SeriesDetailComponent.toggleWantToRead`. `ui-event` facts: Sonarr 1,725, Kavita 1,120 |
| G6 | text keys and literal text on each `ui-event`; a container element gets only its own label | Sonarr `AddSeriesWithTitle`; Kavita `add-to-want-to-read` / `remove-from-want-to-read`. Keys aren't turned into words yet |
| G7 | `validation` detector: DataAnnotations and FluentValidation as `rule` facts | Sonarr `SeriesResource.TvdbId` `GreaterThan(0)`, `SetValidator(SeriesExistsValidator)`, `Title` `NotEmpty` (PostValidator); Kavita 323 attribute rules |
| G8 | `auth` facts on actions, `fallback-auth` on the project, `guard` facts on pages | Sonarr: `FallbackPolicy` requires a signed-in user (scheme `API`); Kavita `AddSeries` `authorize` from `BaseApiController`; 38 page guards (`/library/:libraryId/series/:seriesId`: `authGuard`, `libraryAccessGuard`) |

Both paths now read end to end from code alone:

- **Sonarr:** `/add/new` → `SpinnerErrorButton onPress` [`AddSeriesWithTitle`] → `handleAddSeriesPress` → `useAddSeries` → `POST /api/v5/Series`
  (signed-in user, by fallback) → `SeriesController.AddSeries` [rules on `SeriesResource`] → `AddSeriesService` (❓ outside call to SkyHook)
  → `SeriesService.AddSeries` → writes `Series` → `SeriesAddedHandler` and 2 more.
- **Kavita:** `/library/:libraryId/series/:seriesId` [`authGuard`, `libraryAccessGuard`] → `button click` [`add-to-want-to-read`] →
  `toggleWantToRead` → `ActionService` → `MemberService` → `POST /api/want-to-read/add-series` [`[Authorize]`] → `AddSeries` → writes `AppUserWantToRead`.

Still open: G9 constant values, G10 SignalR messages, G11 background jobs; also minimal APIs' `RequireAuthorization()`, React
guard wrappers, text keys → words, and test-only outside-call blind spots counted with the rest. Not yet checked by a fresh agent (D158).

## 5. Fresh check (D158), 2026-11-08

One fresh agent, one round, read-only; ~171K tokens (said ~100–150K). Tests re-run: pass. 10 findings; 1–3 confirmed by hand on the graphs.

| # | Severity | Finding | Fix |
|---|---|---|---|
| F1 | wrong | override chains: a derived override's verb attribute on the base is missed (`GeneralSettingsController.SaveSettings` → `ANY`, the `PUT` goes to the base method); a base method still routed after a `[NonAction]` override (`GET /api/v3/Series/{id:int}` → both) | walk most-derived methods, skip overridden bases, read verb/route/NonAction/auth along the override chain |
| F2 | wrong | EF `Entry(x).State = Modified` gives no line (30 sites; Kavita `SeriesRepository.Update` has none); `ExecuteDelete/Update` read as `reads` | treat those as writes; an unknown DbSet method is neither |
| F3 | wrong | navigation writes fire on any in-repo list (Sonarr: 67, e.g. `ReleaseSearchService.GetSceneSeasonMappings` `writes Episodes`); many-to-many adds name the wrong table | require the owner type to have a table; join adds as join writes or skipped |
| F4 | wrong, older | `{id:int?}` cut at `?` in `FrontendHttpDetector.Segments`, so Sonarr's Edit series `PUT` matches nothing | split the query only outside braces |
| F5 | misleading | a JSON-converted property became a table (`HighlightSlot`) | skip `.Property(…)`/`HasConversion`/owned types |
| F6 | misleading | Flurl `WithHeader` chains treated as URL-building; one `{*}` URL became a call fact | only path/query calls change the URL; call facts need no `{*}` |
| F7 | misleading | lazy route arrays named `routes` walked twice; copies show no guard | don't root-walk any `loadChildren` target |
| F8 | minor | class `[AllowAnonymous]` + method `[Authorize]` read as authorize (ASP.NET: anonymous) | any AllowAnonymous on the chain wins |
| F9 | minor | `.When()` marks rules after it; every rule fact has the `RuleFor` line | condition per chain position; line from the rule's name |
| F10 | minor | name words without a boundary (`All` ↔ `Allow…`); test mocks add ~70 write facts | word must end the name or be followed by a capital |

Holds: G1 (one `ANY` left, F1), G2 (97 of 97 wrapper calls on v5), G4 (outside-call spots real HTTP), G3 on Sonarr (41 tables = 41 maps), G5/G6 (all handler ids resolve), G7 (bar F9). Nothing fixed yet: owner's call.

## 6. F1–F10 fixed, and the framework's own answer as the key (D164), 2026-11-08

Owner: "okay do it". Code only, no paid AI. Tests: 2,156 .NET + 46 extractor pass; `phases check` ok. Still `code-graph` v5
(its consumers were already `needs review`); this round moves more endpoint ids on Sonarr (the calendar feed) and table ids on Kavita.

| # | Checked on the rebuilt graphs |
|---|---|
| F1 | `GET /api/v3/Series/{id:int}` → `SeriesController` only; `PUT /api/v5/settings/general/{id:int?}` → `GeneralSettingsController.SaveSettings` |
| F2 | Kavita `SeriesRepository.Update` writes `Series`, `SeriesMetadata`; `MediaErrorRepository.DeleteAll` writes `MediaError`; `AnnotationRepository.Update` writes `AppUserAnnotation` |
| F3 | Sonarr `GetSceneSeasonMappings` writes nothing (was `Episodes`) |
| F4 | Sonarr Edit series reaches `PUT /api/v5/Series/{id:int?}` |
| F5 | no `HighlightSlot` table; Kavita tables = the snapshot's 81 (join tables included) |
| F6 | the GitHub releases URL is a call fact, linked as "no service in this repo" |
| F7 | `/genres` only as `/browse/genres`, with its guard |
| F8–F10 | fixture tests (no case on either app) |
| also | the cross-check found Sonarr's calendar feed at `GET /Sonarr.ics`: a template built from a property set the line before. Now `GET /feed/v5/calendar/Sonarr.ics` |

**Cross-check on every build** (`.stangent/graph-crosscheck.json`):
- Sonarr: v3 and v5 OpenAPI files, 0 disagreements (240 and 242 operations). No EF.
- Kavita: OpenAPI 522 operations = 522 endpoints, 0 disagreements. EF snapshot: 2 tables no rule finds, `AspNetRoles` and
  `AspNetUserRoles` (from Identity's generic base context); the snapshot supplies them.

Still open: G9 constant values, G10 SignalR messages, G11 background jobs, text keys → words, nopCommerce (Razor). No second fresh round.

## 7. Second fresh check (D158), 2026-11-08

One fresh agent on `ea483ef` + `41ed292`, read-only, ~141K tokens. Tests pass. 5 findings; R1 and R2 confirmed by hand.

| # | Severity | Finding | Fix |
|---|---|---|---|
| R1 | wrong | F2 went too far: a DbSet call whose name isn't on the lists records nothing, so the app's own query extensions (`Includes`, `ApplyStatsFilter`, `IsLikedBy`…, ~60 sites on Kavita) lose their read; `ChapterRepository.GetChapterAsync` has no `reads` | a non-write call on a DbSet that returns a query, a list or a Task is a read |
| R2 | misleading | many-to-many adds (53 on Kavita, e.g. `lib.AppUsers.Add(user)` granting library access) write nothing; 9 join tables have no lines | read the two types each join table links from the snapshot's `HasOne` block, and write that table |
| R3 | misleading, no case here | the snapshot logic assumes the snapshot and the DbContext share an assembly; with migrations in their own project, guesses survive and the cross-check misfires | tie a snapshot to its `[DbContext(typeof(X))]` and key on that context's assembly |
| R4 | minor | column facts doubled for snapshot entities (1,678 for 839) | add columns once |
| R5 | minor | any OpenAPI file counts as the repo's own (a vendored client spec would list all as missing); an odd file could throw after the graph is saved | catch per file; skip a file almost none of whose paths fit |

Holds: F1/F8 (every documented operation is in the graph, checked by parameter names too, so Sonarr's 0 isn't loose matching),
F2 writes (all 38 point at real tables), F3, tables (81 = the snapshot's, none mapped twice), F4–F7, F9, F10, a deterministic report.
Older gaps found: inline `loadChildren: () => [...]` gives no page (Kavita `/theme`); `RuleForEach` isn't read. Nothing fixed yet: owner's call.
