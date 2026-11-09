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

## 8. R1–R5 fixed, 2026-11-08

Owner: "Yes, fix R1–R5". Code only. Tests: 2,156 .NET + 46 extractor pass. Rebuilt graphs:
- R1: Kavita `reads` 1,109 lines; `ChapterRepository.GetChapterAsync` reads `Chapter` again (any non-write DbSet call reads).
- R2: many-to-many adds write their join table, read from the snapshot's `HasOne` pair: `AccountController.InviteUser`,
  `UpdateAccount`, `LibraryController.AddLibrary` write `AppUserLibrary`; 22 writes to `AppUserLibrary`, `ChapterGenre`, `ChapterTag`.
- R3: a snapshot speaks for the context its `[DbContext(typeof(X))]` names, in X's assembly (fixture: a snapshot in Shop.Web for
  a Shop.Core context); the cross-check groups by context.
- R4: column facts 1,678 → 839.
- R5: each OpenAPI file read on its own; one that can't be read, or where under one in five operations fit (someone else's API), is `ignored`.
Cross-check after: Sonarr 0 disagreements; Kavita 0 on the API, 2 true on EF (`AspNetRoles`, `AspNetUserRoles`, from Identity's base context).
Not done: the older gaps the check found (inline `loadChildren` pages, `RuleForEach`), G9–G11, nopCommerce. No third fresh round.

## 9. G9–G11 closed, and nopCommerce (server-rendered MVC with Razor), 2026-11-08

Owner: "close g9 - g11 then run on nopcommerce". Code only. Tests: 2,157 .NET + 47 extractor pass. Commit `7181f4d`.

| # | Built | On the real graphs |
|---|---|---|
| G9 | `constants` detector + extractor: `value` facts for `const` and `static readonly` fields and top-level TS consts; TS enum members resolve as values | Sonarr 422, Kavita 561 values; nopCommerce `NopRouteNames.Standard.REGISTER` = `Register` |
| G10 | `signalr` detector: `message:signalr|<name>` points, `sends` (direct, or through an app's wrapper behind an interface) and `listens` (frontend `connection.on`, a list's items included) | Kavita 34 messages, 33 joined server → screen (`ProcessSeriesAsync` sends `ScanSeries`, `MessageHubService` listens); Sonarr `receiveMessage` joined |
| G11 | `background-jobs` detector: Hangfire `Enqueue`/`Schedule`/`AddOrUpdate` as `job` facts (runs, via, id, schedule) | Kavita 147 jobs (`ScheduleTasks` → `scan-libraries`, `backup`); a schedule from settings shows empty |

**nopCommerce** (shallow clone at pinned `77d112f`, 130 MB, `benchmark/configs/nopcommerce.yaml` and `.global.json`): 41 projects,
43,020 points, 199 blind spots, no load errors, ~45 s. Traced **Register** (`Views/Customer/Register.cshtml`, a form post):

| Step | Result | Evidence |
|---|---|---|
| Screen (Razor view), form, button, label | **missing, silently** | 0 points from 986 `.cshtml` files; no blind spot. `<form asp-route="@NopRouteNames.Standard.REGISTER" method="post">`, `<button id="register-button">@T("Account.Register.Button")</button>` unread |
| Form post → endpoint | works | `POST /{lang}/register` → `CustomerController.Register(RegisterModel, …)`; the GET goes to `Register(string)` |
| Action → service → writes | works | → `CustomerRegistrationService.RegisterCustomerAsync` → `IRepository<T>.InsertAsync`; writes `Customer`, `CustomerPassword`, `CustomerCustomerRoleMapping`, `GenericAttribute`, … (159 tables by `BaseEntity` convention) |
| Events | works | plugins' `EventConsumer.HandleEventAsync` via `triggered-by` |
| Validation | partly | 1,213 rule facts; `RegisterModel.Email` `NotEmpty`, `IsEmailAddress`. Noise: nop's `WithMessageAwait` read as a rule; `DataType(10)` shows the enum's number |
| Authorisation | **missing** | no `[Authorize]` in nopCommerce: `[CheckAccessPublicStore]`, `[CheckPermission(StandardPermission…)]`, `[AuthorizeAdmin]` are its own filter attributes, unread |
| Outside calls | works | ❓ PayPal (35), Zettle (21) clients flagged |
| Controller → view | missing | `return View(…)` (47 in `CustomerController` alone) doesn't link to its `.cshtml` |

**Gaps for the owner (nothing fixed yet, D159):**

| # | Gap | Recommended fix | Rough cost |
|---|---|---|---|
| N1 | Razor views unread, silently: no screen, form, button or label for any server-rendered page | read `.cshtml` with Razor's own parser (`Microsoft.AspNetCore.Razor.Language`, deterministic): a `view` point per file; `@model`; `<form asp-route/asp-action/asp-controller method>` → the endpoint; buttons and `@T("key")` labels as `ui-event`-style facts; partials and view components as `renders`; until built, a blind spot per view file | medium–large |
| N2 | `return View(…)` doesn't reach its view | link an action to `Views/{Controller}/{Action}.cshtml` (or the named view) by MVC's lookup rules | small, with N1 |
| N3 | an app's own access filters unread | any filter attribute on an action or its controllers (`TypeFilterAttribute`, `IAuthorizationFilter`, names with Authorize/Permission/Access) as an `auth` fact with its arguments' values | small |
| N4 | validation noise | message modifiers by prefix (`WithMessage…`); enum arguments by member name | small |
| N5 | plain JavaScript (291 `.js` files outside `lib`, e.g. `public.ajaxcart.js`; jQuery AJAX in views) unread | read `.js` outside a tsconfig with the same extractor (allowJs), and inline `<script>` from N1 | medium |

Recommended order: N3, N4 (small, now), then N1 + N2 together (the screen half of every server-rendered flow), then N5.

## 10. N1–N5 fixed, 2026-11-08 (D165)

Owner: "lets do it in new session", on the order N3, N4, then N1 + N2, then N5. Code only, no paid AI. Tests: 2,158 .NET + 48 extractor
pass; `phases check` ok. Each fix has a fixture in `tests/fixtures/shop` (`AccountController`, `Views/`, `Areas/Admin/`, `wwwroot/js/`).

| # | Built | nopCommerce | Elsewhere |
|---|---|---|---|
| N3 | an app's own MVC filter attributes (`TypeFilterAttribute`/`ServiceFilterAttribute` subclasses, or filters themselves) are `auth` facts (`rule: filter`) with their arguments by name, when they run at the authorisation stage or their name says Authorize/Permission/Access/Role; `[AllowAnonymous]` doesn't remove them | 8,166 filter facts on 1,478 actions: `CheckAccessPublicStore(ignore = true)` on `Register`, `CheckPermission(permission = "…")`, `AuthorizeAdmin`; captcha, "save last page" and language filters left out | Kavita +255: its own `DisallowRole(roles = ["Read Only"])`, `SeriesAccess(seriesIdKey = "seriesId")`, `LibraryAccess`… Sonarr none |
| N4 | `With…`/`When…`/`Unless…` are modifiers by prefix (`WithMessageAwait` sets the message, as text or expression); enum arguments by member name | `DataType(EmailAddress)`, not `DataType(10)`; no `WithMessageAwait` rules | — |
| N1 | views read from the C# the SDK's Razor compiler makes (already inside the loaded compilation): `view` points, `@model` (`uses`), what the view's code names, `form` points → endpoints (`calls-http`), buttons as `ui-event` facts with localizer keys, partials and view components (`renders`), inline `<script>` kept for N5; a `.cshtml` the compiler made nothing for, a Razor Page and any lookup that fails is ❓ | 779 views; 295 forms, 290 reach an endpoint, 5 ❓; 835 buttons (724 with text keys); 1,150 `renders` from views | no `.cshtml` in either |
| N2 | `View(…)`/`PartialView(…)` in actions and view components `renders` the view MVC's lookup finds (area, shared, `Components/{name}`, a deployed path by its tail); a base class's or helper's `View()` is ❓ | 541 actions render their view | — |
| N5 | each C# project's own `.js` (no tsconfig; `lib`, `lib_npm`, `vendor`, `gulp`, minified, `*.config.js` skipped) and its views' inline scripts are a plain-scripts frontend; jQuery `$.ajax/get/post/getJSON` are calls; `dom-event` facts for bindings; a button whose `id` a script binds `calls` the handler (🟡); a server-written URL in a script is ❓ | 2 plain-scripts frontends (`Nop.Web`, `Misc.Forums`); 180 bindings; 37 buttons joined to their handler; script calls are nearly all ❓ (URLs passed in or written by the server) | Sonarr +1 ❓: `$.ajax(options)` inside its own `createAjaxRequest` wrapper (calls through the wrapper match as before) |

**Register, end to end** (`stangent graph build` on nopCommerce, then `.stangent/graph.sqlite`):

| Step | Point | Evidence |
|---|---|---|
| Screen | `view:…/Views/Customer/Register.cshtml` | `uses` `RegisterModel`; rendered by both `CustomerController.Register` actions (N2) |
| Button | `ui-event` line 459 | `button submit`, `id register-button`, keys `Account.Register.Button`, handler `…Register.cshtml#form1` |
| Form | `…#form1` line 18 | `POST`, `route Register` (`NopRouteNames.Standard.REGISTER`, a constant the compiler resolved) |
| Endpoint | `endpoint:Nop.Web\|POST /{*}/register` | `calls-http` ✅ (the route's `{lang}` segment is built at runtime, so `{*}`) |
| Action | `CustomerController.Register(RegisterModel, …)` | `routes-to`; auth: `CheckAccessPublicStore(ignore = true)` on the method; `WwwRequirement`, `HttpsRequirement`, `CheckAccessClosedStore`… from base controllers; `ValidateHoneypot` |
| Service | `ICustomerRegistrationService.RegisterCustomerAsync` → `CustomerRegistrationService` | `dispatches-to`; calls `ICustomerService.UpdateCustomerAsync`, `InsertCustomerPasswordAsync` |
| Write | `CustomerService.UpdateCustomerAsync` | `writes` `table:Nop.Core\|Customer` |

**Changes against the plan, said plainly (D155):**
- **N1 doesn't use the standalone Razor parser.** `Microsoft.AspNetCore.Razor.Language` (last standalone release 6.0.36) keeps its syntax tree
  internal. The SDK's own Razor compiler already runs as a source generator inside every compilation the graph loads, so the graph reads
  its output: the same parser and version the build uses, no new package, constants and types already resolved.
- **Two routing faults fixed on the way, both general.** `[Area]` is now read up the base controllers (nopCommerce puts it on
  `BaseAdminController`), and a conventional pattern with an `{area}` token (ASP.NET's `{area:exists}/{controller}/{action}`) serves
  the actions that have an area. Before, nopCommerce's admin actions had no area and were served by the non-area route; now they're under
  `/Admin/…`. Sonarr's and Kavita's endpoints are unchanged.
- **Section 9's 291 `.js` files** included `lib_npm`; nopCommerce's own scripts are about 25.
- **A full nopCommerce build takes ~54 s warm (all 43 projects re-read), up from ~45 s**: reading the Razor compiler's output for
  779 views. The first, cold run took ~110 s (restore and first load). A view's project re-extracts when one of its `.cshtml` files
  changes; they're now in the project's fingerprint.

**Blind spots on nopCommerce, 199 → 641**, all said where they were silent: `razor-views` 323 (171 views in 17 plugins compiled only at
runtime, and most of the rest are lookups of those plugins' views), `frontend-http` 120 (script URLs passed in or written by the server).

**Sonarr and Kavita rebuilt:** no point or line lost; cross-check Sonarr 0, Kavita 2 (Identity tables, as before). Added: `mvc-action`
facts (468, 522), Kavita's own filters (above), Sonarr's one ❓ (above).

**Left, not fixed** (by the owner's rule, only general ones are worth doing; none is needed for Register):

| Gap | General? |
|---|---|
| views compiled only at runtime (nopCommerce's plugin projects use the plain SDK): ❓ per view | no: a plugin architecture's choice |
| Razor Pages' handlers aren't endpoints: ❓ per page | yes, for Razor Pages apps; none of the three uses them |
| links (`<a asp-action>`) and `RedirectToAction` as navigation | yes: D154's "hand-off" stop for server-rendered apps |
| a server-written URL in a script (`'@Url.Action("Add")'`) is ❓, not resolved to its endpoint | yes, a common MVC pattern |
| a partial looked up through the requesting controller's folder | partly: shared views calling controller-specific partials |
| an inline handler isn't a point: its calls belong to the view | yes, but a flow can still join button → binding → view |

## 11. Third fresh check (D158) of N1–N5, 2026-11-08

One fresh agent on `af0a8e2` + `0474b12`, read-only, against the existing nopCommerce graph (no rebuild). **~199K tokens, over the
120–170K said.** Sampled ~40 forms, 20 buttons, 14 action → view lines, all 8,166 auth facts, all 37 button → script joins, the routing
area split, and Register step by step. Plugin forms couldn't be checked (compiled only at runtime). 13 findings, all general;
1–4 confirmed by hand (source line and graph row). Nothing fixed yet: owner's call.

| # | Severity | Finding | Fix suggested |
|---|---|---|---|
| T1 | wrong ✅ | a tag-helper form with no `method` is read as GET (`RazorViewsDetector` `?? "get"`); ASP.NET's form tag helper writes `post`. 48 forms reach the display action, not the `[HttpPost]` one; Admin "Clear cache" is ❓ though its endpoint exists | `post` when the tag helper builds the form; GET only for a plain `<form>` |
| T2 | wrong ✅ | a plain-script global declared in two files resolves to one: `addAntiForgeryToken`, `setLocation`… in both `admin.common.js` and `public.common.js`; 74 ✅ calls go to the admin file, 17 from storefront pages that never load it | several declarations → 🟡 to each (or by which layout loads which script) |
| T3 | wrong ✅ | a button's `formaction`/`formmethod` is ignored: Save, Save & continue and Delete all post to the form's action (`ProductSpecAttributeAddOrEdit.cshtml:36–65`) | `formaction` gives the button its own target; server-written → ❓ |
| T4 | silent gap | 0 of 87 view components reach their view: they call the base helper `NopViewComponent.ViewAsync`, which has the only ❓ | `Components/{name}/Default` per derived component (🟡), or ❓ on each |
| T5 | silent gap | access filters at the action stage whose names don't say Access/Permission are missed (`[ValidateIpAddress]` on every admin action, `[ForceMultiFactorAuthentication]`, `[ValidatePassword]`), and so are global filters (`options.Filters.Add<…>`) | global filters as `auth` `from: global`; an action filter that sets `context.Result` → 🟡 or ❓ |
| T6 | misleading | non-access filters count as `auth` only by their stage: HTTPS/WWW redirects, external sign-out, the honeypot (~3,788 of 8,166) | stage-only matches 🟡, or kept out unless the name or a 401/403/challenge says so |
| T7 | misleading | a script handler collapses to the one repo function it calls (jQuery calls don't resolve): all 37 button → script joins point at helpers (`updateTable` 29) | a handler is a function only when its body is that one call; else inline |
| T8 | silent | nopCommerce's theme rule (`ThemeableViewLocationExpander`) isn't modelled: the theme's `Head.cshtml` wins at run time but has no `renders` line | with a view location expander present, same-named views under other roots as 🟡 candidates, or ❓ |
| T9 | silent gap | 143 submit buttons in partials (form in the parent view) get `handler ""` and no ❓; one ❓ says GET where the parent posts | join to the form around the `<partial>` call (🟡); a button's `name` against action selectors |
| T10 | minor | 130 POST forms link to both the `ANY` and the `POST` endpoint of a path; routing prefers the one with the verb | drop `ANY` when a same-path endpoint has the form's verb |
| T11 | minor | a partial named with a folder and no extension is ❓ (`../ReviewType/List`) though MVC finds it | add `.cshtml` when missing |
| T12 | suspect | any method starting `With`/`When`/`Unless` is a modifier, so a custom `.Within(…)` validator would vanish; none on nopCommerce | decide by receiver: options builder vs rule builder |
| T13 | minor | third-party libraries in a plugin's `Content/js` (`purify.js`, `marked.umd.js`) read as the app's own | skip UMD-wrapped or licence-headed files |

**Holds:** Register, all 7 steps against source; named-route forms; explicit admin POST forms (plus T10's extra `ANY`); forms with no
action are 🟡 and fall back to the view's action; 3 of the 5 ❓ forms are truly unknowable (the other 2 are T1 and T9); localizer keys
14/14; action → view 14/14, `[ActionName]` and area folders included, none ✅; no admin action outside `/Admin/…` and no public one
inside; `ignore = true` on 48 facts; enums by name; `WithMessageAwait` kept as an expression; script file choice; a literal jQuery
URL 🟡 to the right endpoint. Older, outside N1–N5: Install's form reaches `ANY /{*}`; action choice by a button's name
(`FormValueRequired`) isn't modelled, but `routes-to` is 🟡, so it's honest.

## 12. T1–T12 fixed, 2026-11-08 (D167)

Owner: "Yes, fix T1–T12" (T13 skipped). Code only, no paid AI. Tests: 2,159 .NET + 49 extractor pass; `phases check` ok. Fixtures in
`tests/fixtures/shop`: `Views/Account/Settings.cshtml` and its partials, `Components/ShopViewComponent.cs`, `ThemeExpander.cs` and
`Themes/Dark/`, the filters in `AccountController.cs`, the global filter in `Program.cs`, the rule builders in `Shop.Core/Rules.cs`.

| # | Fix | nopCommerce (before → after) |
|---|---|---|
| T1 | a form the form tag helper builds (asp-*, or no `action` of its own) is POST, as ASP.NET writes it; a plain `<form>` stays GET | POST forms 240 → 293; forms reaching an endpoint 290 → 294; Admin "Clear cache" reaches it |
| T3 | a button's `asp-action`, `formaction` or `formmethod` gives it its own target; `@Url.Action(…)`/`@Url.RouteUrl(…)` with constants is read as action/controller/route; anything else the server writes is ❓ | 3 buttons with targets of their own (Save & continue, Delete…) |
| T9 | a submit button with no form submits the form around the `<partial>` (or `Html.PartialAsync`) that renders its view, through partials of partials (🟡); none found → ❓ | 79 partial → form lines; 19 ❓ (buttons a script submits, or in the layout) |
| T10 | the endpoint with the form's verb wins over `ANY` at the same path | form → endpoint lines 420 → 297 |
| T11 | a partial named with a folder and no extension gets `.cshtml` | `../ReviewType/List` found |
| T4 | a call to a base class helper that forwards to `View(…)` (`NopViewComponent.ViewAsync(model)`) renders the caller's view: the name it passes, or `Components/{name}/Default` (🟡) | component → view 0 → 52 (the rest are plugins' views, compiled only at runtime: ❓) |
| T8 | with an app's own `IViewLocationExpander`, MVC's lookup is 🟡 and a view of the same path under another root (`Themes/X/Views/…`) is a 🟡 candidate | the theme's `Head.cshtml` |
| T5 | global filters (`options.Filters.Add<X>()`, `AddService`, `typeof`, `new`) are `auth` facts on the project, `from: global`; an action filter whose code sets `context.Result` counts | `ValidateIpAddress` on every admin action; `ValidatePassword`, `ForceMultiFactorAuthentication`; `PunchOutSessionGuard` global |
| T6 | every filter fact says its evidence, `basis`: `denies` (its code sets a refusal: challenge, forbid, 401/403, access denied, sign-in), `name`, or 🟡 `stage` / `result` | 13,162 facts: denies 5,225, name 338, 🟡 result 3,811, 🟡 stage 3,788 (HTTPS/WWW redirects, external sign-out, honeypot) |
| T12 | a chained call is a modifier when it's on the rule's options (`IRuleBuilderOptions`), a rule when it's on the rule builder; by name only when unknown | unchanged; Sonarr: its 13 `.AsWarning()` are modifiers now, so their messages land on the rule they follow |
| T2 | a plain script's global declared in several files gets 🟡 lines to each declaration | 150 calls (`addAntiForgeryToken`, `setLocation`…) |
| T7 | a script handler is a function only when it is one or its body is that single call; else `inline` | button → script 37 → 1 (36 were helpers picked out of bigger handlers) |

**Past the suggested fix, said plainly (D155):** T3 reads constant `@Url.Action`/`@Url.RouteUrl` targets instead of marking every
server-written one ❓; T9 carries a form down through partials of partials; T5's global filters sit on the project (like
`fallback-auth`), not on every action; T6 keeps stage-only filters as 🟡 `auth` facts with their basis rather than dropping them; T7
applies to plain scripts only, React's handler rule (G5) is unchanged.

**Blind spots on nopCommerce 641 → 767:** T4's plugin components now name their views, which are compiled only at runtime (~100 ❓,
were one ❓ in the base helper); T9's 19 buttons with no form. Form ❓ 5 → 4.

**Sonarr and Kavita rebuilt:** no point or line lost; blind spots 327 and 122, unchanged; cross-check 0 and 2. Kavita's 1,029 `auth`
facts unchanged. Sonarr's `rule` facts 619 → 606 (T12, above).

## 13. Fresh check (D158) of T1–T12, 2026-11-08

One fresh session on `f45cd1d` (code) + `e56a8ac`, read-only, against the graphs already rebuilt with it (nopCommerce, Sonarr, Kavita).
**~95K tokens (estimate), inside the 100–150K said.** No agents, no paid AI, nothing fixed. Sampled 12 forms and all 5 GET forms,
the 3 `formaction` buttons and 22 `<button asp-action>` buttons, 10 of 79 partial → form lines (and every one of the 47 partials
behind them), 12 of 52 component → view lines and the 6 components with none, the theme line, all 13,162 filter facts by filter and
basis (against each filter's source), the one global filter, the `addAntiForgeryToken` fan-out, the remaining button → script line,
all 19 "submit button outside any form" ❓, Sonarr's 13 dropped rules and Kavita's 1,029 auth facts.

**Bottom line: no wrong ✅.** Every ✅ sampled is proved by the code. Ten findings, all labelled honestly as 🟡 or ❓ but some say the
wrong thing: the biggest are V1 (MVC's controller-folder lookup missing for view components: ❓ "not found" on views MVC finds) and
V2 (half of T9's partial → form lines come from buttons that cancel their own submit). All ten are general (none is nopCommerce-only polish); V7
matters mostly for apps that load modules at run time.

| # | Severity | Finding | Evidence (source · graph) | Suggested general fix | Scope |
|---|---|---|---|---|---|
| V1 | misleading ❓ | a view component's view, and partials that view renders, are looked up without the calling page's controller folder (`Views/{controller}/Components/…`, `Views/{controller}/_X`). `RazorViewsDetector.cs:366` uses `r["controller"]`, empty for a component | `BlogMonths`, `BlogTags`, `ProfileInfo` ❓ "view Default not found" though `Views/Blog/Components/BlogMonths/Default.cshtml` and `Views/Profile/Components/ProfileInfo/Default.cshtml` exist; `Components/OrderSummary/Default.cshtml:86` `_GiftCardBox` ❓ "not found" (it is `Views/ShoppingCart/_GiftCardBox.cshtml`, the cart page's folder), so its button `_GiftCardBox.cshtml:12` is ❓ "outside any form" though it sits in `shopping-cart-form` (`OrderSummary/Default.cshtml:27–187`) | for a component's view and what it renders, try `Views/{c}/…` for each controller `c` whose views invoke the component (🟡), then Shared | general (MVC's own lookup) |
| V2 | misleading 🟡 | T9 links a partial to its parent's form when its only submit buttons cancel the submit: `onclick="…; return false;"` popup openers, and hidden refresh buttons whose script click handler returns false. 24 of the 47 partials with a partial → form line are only these | `Category/_CreateOrUpdate.Products.cshtml:61–62` (`OpenWindow('@(Url.Action("ProductAddPopup", "Category", …))'…); return false;`), `:66` hidden `btnRefreshProducts`, its handler `:70–75` returns false · `_CreateOrUpdate.Products.cshtml → Category/Edit.cshtml#form1` and `→ Create.cshtml#form1`, both 🟡. The popup's own target (`ProductAddPopup`) isn't read | a submit button whose inline handler ends `return false` (or a bound handler returns false / calls `preventDefault`) doesn't submit; read a constant `@Url.Action` in it as its target (🟡), as T3 does for `formaction` | general (HTML idiom) |
| V3 | misleading ❓ | a button with its own target (T3) in a partial gets GET: `ButtonTarget` takes `form?.Method ?? "get"` (`RazorViewsDetector.cs:991`) before T9 finds the parent's form | `Setting/_GeneralCommon.Localization.cshtml:13` `<button type="submit" asp-action="UploadLocalePattern">`, rendered inside `GeneralCommon.cshtml:58` (POST form) · form fact `method: GET`, ❓ "form GET Setting.UploadLocalePattern matched no endpoint", though `POST /Admin/Setting/UploadLocalePattern/{id?}` exists (`[HttpPost]`, `SettingController.cs:1813`). 1 row | such a button takes its method from the forms T9 finds around its partial (🟡); none → ❓ about the method, not the endpoint | general |
| V4 | misleading 🟡 | `denies` misses refusals by 404/400, and reads only the filter's own members, not its base class (`RoutesDetector.cs:210`) | Kavita `ProfilePrivacyAttribute.cs:34–36` (`ContentResult` 400) → 19 facts 🟡 `stage`; the `Series/Volume/Chapter/Library/Person/ReadingListAccess` filters refuse in their base `AccessAttribute` (`EntityAccessAttribute.cs:125, 135`, `NotFoundResult`) and read by `name` only (114 facts); a filter like these without an access word in its name would be 🟡 `stage` or gone | at the authorisation stage, any short-circuit result with a 4xx (`NotFound`, `BadRequest`, `StatusCode = 4xx`) is `denies`; read members up the base chain | general |
| V5 | misleading | T12 makes `.AsWarning()` a modifier (right), but nothing on the rule says it only warns: Sonarr shows the message and still saves | `SeedCriteriaSettings.cs:12–14` `GreaterThan(0.0).When(…).AsWarning().WithMessage(…)` · fact line 12 `rule: GreaterThan`, `conditional: yes`, no severity; 13 such rules | keep each rule's modifiers by name (`modifiers: "AsWarning"`) and read a constant `WithSeverity(Severity.X)` as `severity` | general (FluentValidation) |
| V6 | silent gap | T9 doesn't carry a form into a view component rendered inside it, only into partials | `Components/OrderTotals/Default.cshtml:111` `removegiftcard-…` ❓ "outside any form"; `OrderTotals` is invoked at `OrderSummary/Default.cshtml:93`, inside the form | treat `Component.InvokeAsync` inside a form like a `<partial>` for T9 (🟡) | general |
| V7 | silent gap | a global filter's fact sits on the project that registers it; a plugin's applies to the whole host app, which has no reference to it | `Nop.Plugin.Misc.PunchOut/Infrastructure/NopStartup.cs:30` `options.Filters.Add<PunchOutSessionGuardAttribute>(0)` · `auth` on `project:…PunchOut`, `from: global`; nothing joins it to Nop.Web's actions. Same shape as Sonarr's `fallback-auth` on Sonarr.Host while actions are in Sonarr.Api.V3 (a reference exists there) | say in `docs/code-graph.md` that a project's global facts apply to every action the running app hosts; a module loaded at run time → ❓ "applies to the host app" | general rule; matters for apps with run-time modules |
| V8 | minor | section 12's account of the 19 formless-button ❓ ("a script submits, or in the layout") is partly off: 8 are dashboard card buttons with no `type` and no form anywhere (they do nothing), 2 are V1/V6 | `Home/_CustomerStatistics.cshtml:16–19`, `_LatestOrders.cshtml:15`… (`data-card-widget="collapse"`) | a button with no form and no `type` in a view no form reaches: not a submit, no ❓ | general |
| V9 | minor (latent) | with a view location expander, a view named by path (`~/…`, `/…`, `….cshtml`) turns 🟡 and gets theme candidates, but MVC never runs expanders for paths (`RazorViewsDetector.cs:339, 376`) | 0 rows on nopCommerce: its 229 path names are all plugin views the graph can't read | expanders affect only names MVC looks up; paths keep ✅ and get no alternatives | general |
| V10 | minor | `ViewAsync(null, model)`: a constant null view name is MVC's `Default`, read as ❓ | `SelectedCheckoutAttributesViewComponent.cs:19` · ❓ "ViewAsync chosen at runtime: null" | a null or empty constant name → `Default` | general |

Older, outside T1–T12, seen in passing: a script handler named by a function declared inside `$(function(){…})` stays `prop:name` with
no ❓ (8 `dom-event` facts, `Customer/UserAgreement.cshtml:10`, `public.countryselect.js:47`): local functions aren't points. Button →
script joins use `#id` selectors only, so `button[name='plugin-reload-grid']` (`Plugin/List.cshtml:31`) joins nothing.

**Holds:**
- **T1:** the rule is ASP.NET's (the form tag helper writes `post` when it builds the form: asp-* or no `action`); 12 forms right; the
  5 GET forms are 4 written `method="get"` (search boxes) and V3. Admin "Clear cache" (`_AdminLayout.cshtml:118`) is POST to `ClearCache`.
- **T3:** `ProductSpecAttributeAddOrEdit.cshtml:36, 44, 63` reach their own POST endpoints, 🟡 (controller from the view's folder,
  honest); Save and Save & continue keep the form's `ProductSpecAttrUpdate`. `@Url.Action` is read through Roslyn's binding (constants
  only), so a non-constant argument stays ❓. The 22 `<button asp-action="Export…">` buttons get their own targets.
- **T9:** 10 sampled lines reach the right parent forms, nested ones too (`_CreateOrUpdate.Products` → `Create` and `Edit` through
  `_CreateOrUpdate`); the other 23 partials do submit (Maintenance's delete buttons, Campaign's send-test-email).
- **T10:** form → endpoint lines are 272 POST + 25 ANY (forms to verb-less actions); no form has both. 3 forms reach two POST endpoints,
  the plain path and the `/{*}/` language-prefixed one, both kept: right, MVC has both.
- **T11:** `../ReviewType/List` → `ReviewType/List.cshtml`, 🟡.
- **T4:** 52 components reach `Components/{name}/Default` 🟡; 12 sampled right. **T8:** `_Root.Head.cshtml` renders both
  `Views/Shared/Head.cshtml` and `Themes/DefaultClean/Views/Shared/Head.cshtml`, 🟡.
- **T5/T6:** the basis counts add up (denies 5,225 = `AuthorizeAdmin`, `ValidateIpAddress`, `CheckPermission`, `ValidateVendor`,
  `CheckAccessPublicStore`); each of those five sets a challenge or an AccessDenied redirect in its source (e.g.
  `ValidateIpAddressAttribute.cs:96`, `ValidateVendorAttribute.cs:96`, `CheckAccessPublicStoreAttribute.cs:90`), so `denies` does mean
  the code refuses. `CheckAccessClosedStore` is `name` (it redirects to "store closed", `:134`). `CheckLanguageSeoCode` is 🟡 `result`:
  an SEO redirect, and its basis says so. nopCommerce has one global filter in its source, and it is found.
- **T2:** `addAntiForgeryToken`: each file's own call ✅ to its own copy; 59 views 🟡 to both. **T7:** single-call bodies resolve
  (`Plugin/List.cshtml:31` → `showThrobber`); longer bodies are `inline`; the one button → script line left is `_AdminLayout` → `showThrobber`.
- **T12:** Sonarr's 13 dropped rules are exactly its 13 `.AsWarning()` calls (source grep); their messages now sit on the rule they
  follow. The only other `IRuleBuilderOptions` extensions across the three repos are nopCommerce's `WithMessageAwait` (modifiers, right).
- **Kavita:** 1,029 `auth` facts, as before, now with a basis each (`KPlus` denies; the access filters by name; `ProfilePrivacy`, V4).

## 14. V1–V5 fixed, 2026-11-08 (D168)

Owner: "sure lets do those" (V1–V5, on the recommendation; V6–V10 left). Code only, no paid AI. Tests: 2,159 .NET + 50 extractor
pass; `phases check` ok. Fixtures in `tests/fixtures/shop`: `Components/PageViewComponents.cs` with `Views/Account/Components/Tips/`
and `Views/Shared/Components/Summary/` (+ `_Gift`), `Views/Account/_Popup`, `_Upload`, a button in `_Orphan`, `OwnedOnly` in
`AccountController.cs`, `Note` in `Shop.Core/Rules.cs`.

| # | Fix | Before → after |
|---|---|---|
| V1 | MVC's `{controller}` is the request's: a view shows on the pages of the controllers whose views reach it (a controller folder, an action's `View()`, then down through partials and the views that invoke a component). A component's view is looked up in each such controller's folder first; so is a partial of a view outside a controller folder (Shared, component views). 🟡 | nop component → view 52 → 55 (`BlogMonths`, `BlogTags`, `ProfileInfo`); `_GiftCardBox` found, and its button now submits the cart form; formless-button ❓ 19 → 17 |
| V2 | a submit button whose inline `onclick` ends `return false`, or whose script binding always cancels (new `dom-event` `cancels`: `preventDefault()`, or jQuery's `return false`, as a statement of the handler's own body), doesn't submit. A button that doesn't submit, with one constant `@Url.Action`/`@Url.RouteUrl` in its `onclick`, gets a `form` point for the page (`via: onclick`), 🟡 | nop partial → form lines 79 → 24 (47 partials → 19; every one dropped checked: each button cancels); 65 onclick targets, all reach an endpoint 🟡 (60 by GET, 5 POST-only actions their scripts post to) |
| V3 | a button's own target with no form in its file has no method of its own: linking takes the method of each form around its partial, 🟡; none → ❓ "its method is unknown" | `_GeneralCommon.Localization.cshtml:13` → `POST /Admin/Setting/UploadLocalePattern/{id?}` 🟡 (was ❓ "GET … matched no endpoint") |
| V4 | at the authorisation stage any other 4xx short-circuit (not found, bad request) is `denies`; a filter's base classes in the repo are read for what it sets | Kavita: all 255 filter facts `denies` (were 16 `denies`, 220 `name`, 19 🟡 `stage`); each of its 9 filters refuses in code (404 or 400). nop unchanged |
| V5 | a rule keeps its other modifiers by name (`modifiers`, `\|`-joined) and a constant `WithSeverity(…)`'s member (`severity`) | Sonarr: 53 rules carry modifiers (`WithErrorCode` 18, `AsWarning` 13, `WithName` 12, `OverridePropertyName` 10); 606 rules, unchanged |

**Past the suggested fix, said plainly (D155):** an onclick target is tried by GET first and, when the action serves no GET, by any
verb it serves (the 5 nop actions its scripts post to; without this they were 5 new ❓); onclick targets cover `type="button"` buttons
too, not only cancelled submits; and markup inside a `<script>`'s text (a grid column's `'<button …>'` string) is no longer read as
the page's own: 23 nop buttons, which had kept 5 partial → form lines with a cut-off `onclick`.

**nopCommerce blind spots 767 → 760.** Sonarr and Kavita rebuilt: no point or line lost, cross-check 0 and 2 (unchanged); Sonarr has no
views, Kavita's only change is the filter basis.

## 15. Fresh check (D158) of V1–V5, 2026-11-08

One fresh session on `1ec352c`, read-only, against the graphs already rebuilt with it (nopCommerce, Sonarr, Kavita). **~60K tokens
(estimate), inside the 50–70K said.** No agents, no paid AI, nothing fixed. Checked: every V1 page-controller line (only 4 exist on
nop, so no sample of 10), 10 of the 65 `via: onclick` forms by hand and the script call behind all 65, every partial with submit
buttons and no form of its own (30, independently of section 14's numbers) and the handlers of 5 dropped buttons, the `<script>` skip
across all nop views, all 255 Kavita filter facts by filter, nop's basis counts, V3's one row, Sonarr's 53 rules with modifiers.

**Bottom line: no wrong ✅, and V1–V5's own lines hold.** One silent gap the `<script>` skip made (W1), two minor latent ones (W2, W3).
All three general.

| # | Severity | Finding | Evidence (source · graph) | Suggested general fix | Scope |
|---|---|---|---|---|---|
| W1 | silent gap | V2's `<script>` skip drops submit buttons that a script builds (grid column renderers) and that really submit the form around them: no button, no partial → form line, no ❓ | `Admin/Views/Common/_Maintenance.DBBackups.cshtml:88, 91` `'<button type="submit" … onclick="submitBackupAction(…)">'`; `submitBackupAction` (`:51–56`) only adds hidden inputs, so the button submits Maintenance's form · the partial has no facts and no `calls` → `#form` line (it had one before, through the cut-off markup: right by luck). Same shape inside a form of the view's own: `Plugin/List.cshtml:277, 279` (install/uninstall, in `plugins-form-local` `:16–325`), `Menu/MenuItemSelectProductPopup.cshtml:104` (Select, in the form `:23–114`); their forms' endpoint lines stay, the buttons are gone | a submit button found inside a script's string isn't the page's markup, but say it exists: a ❓ "submit button built by a script" on the view, or a 🟡 button marked `built: script` in the form around the `<script>` (and T9's partial forms) | general (jQuery/DataTables grids) |
| W2 | minor (latent) | an onclick target is tried by GET first whatever the onclick calls; a helper that posts (`AjaxCart.addproducttocart_details`, `uploadSelectedShipments` → `$.ajax({ type: "POST" })`, `Order/ShipmentList.cshtml:332–336`) to an action that serves both GET and POST would reach the GET one. And the form fact keeps `method: GET` when its line reached POST | the 5 POST-reached rows are exactly these two helpers (2 + 3), so right today; `Views/Product/_AddToWishlist.cshtml:23` fact `method: GET`, line → `POST /addproducttocart/details/…` | write the verb the line reached on the fact (or `""`); when the onclick calls a function whose body posts, try POST first | general |
| W3 | minor (latent) | V4's "any 4xx at the authorisation stage = denies" also counts a 400 for missing input | `Kavita.Server/Attributes/ProfilePrivacyAttribute.cs:34–36` (no `userId` → 400) is an input check; the refusal is `:51–55` (profile not shared → 400). The basis is per filter, so `denies` is still right for ProfilePrivacy; a filter whose only 4xx is an input check would be called `denies` | keep the status in the basis (`denies` with `status: 400/404`), so a flow can tell "not allowed" from "bad request" | general |

Seen in passing, not checked on data: for a partial name with no folder or extension, MVC looks in `Views/{controller}` (the
request's) and Shared, not beside the calling view (that is the Razor Pages rule). `PartialHits` looks beside the view first and
applies V1's page controllers only to views outside a controller folder, so a controller-folder view shown on another controller's
page gets its own folder's partial. nop's component views that render partials beside them all name them with `.cshtml`
(`MainMenu/Default.cshtml:21–31`, `FooterMenu/Default.cshtml:14`), where looking beside the view is MVC's rule, so those lines are right.

**Holds:**
- **V1:** the 4 page-controller lines are all right: `BlogMonths`/`BlogTags` → `Views/Blog/Components/…` (invoked only from
  `Blog/BlogPost.cshtml:16, 18`, `Blog/List.cshtml:15, 17`), `ProfileInfo` → `Views/Profile/Components/…` (`Profile/Index.cshtml:16`),
  `OrderSummary/Default.cshtml:86` → `Views/ShoppingCart/_GiftCardBox.cshtml`. OrderSummary is also invoked from 8 Checkout views, which
  have no `_GiftCardBox`; the partial renders only `if (Model.IsEditable)` (`:81`), the cart page, so no candidate is missing.
  No Shared or Admin view gained a line to a controller folder beyond that one.
- **V2 onclick targets:** 10 sampled reach the URL the onclick opens or sends (`setLocation`, `location.href`, `OpenWindow` popups,
  e.g. `Vendor/_CreateOrUpdate.Info.cshtml:94` → `ANY /Admin/Vendor/AddCustomerToVendorPopup`, `ViewPM.cshtml:40` → privatemessages).
  All 65 by script call: `OpenWindow` 31, `setLocation` 23, `location.href` 6 (all navigations, GET), `uploadSelectedShipments` 3 and
  `AjaxCart.addproducttocart_details` 2 (both post, and both reach POST).
- **V2 dropped lines:** 19 partials keep a partial → form line (matches section 14). Every other partial with submit buttons and no
  form either cancels them (inline `return false`, or a `$('#id').click(…return false)` binding: checked `CustomerRole/_CreateOrUpdate:110–121`,
  `Vendor/_CreateOrUpdate.Info:110–119`, `Menu/_MenuItemCreateOrUpdate:330–339`, `Product/_CreateOrUpdate.Info:581–586`), has its own
  target (V3, onclick popups), or is W1.
- **`<script>` skip:** every nop view's `<script>` tags close (open = close count in every file); `</script` appears only as closers.
  An unclosed script leaves `scriptEnd` at -1 and hides nothing; a `</script` inside a script string ends the script for the browser too.
- **V3:** `_GeneralCommon.Localization.cshtml:13` has method `""` and one line, `POST /Admin/Setting/UploadLocalePattern/{id?}` 🟡.
- **V4:** Kavita's 9 filters all `denies` (255 facts); `DisallowRole` refuses with 400 (`DisallowRoleAttribute.cs:28–38`); nop's
  basis counts unchanged (denies 5,225, name 338, result 3,811, stage 3,788).
- **V5:** Sonarr's 53 rules = `WithErrorCode` 18 + `AsWarning` 13 + `WithName` 12 + `OverridePropertyName` 10; Sonarr has no
  `WithSeverity`, so `severity` is empty everywhere, right.

## 16. W1 fixed, 2026-11-08 (D169)

Owner: "okay lets do it then" (W1, on the recommendation; W2, W3 and V6–V10 left for when a flow needs them). Code only, no paid AI.
Tests: 2,159 .NET + 50 extractor pass. Fixture: `tests/fixtures/shop/Shop.Web/Views/Account/_Grid.cshtml`, rendered inside Settings' form.

| # | Fix | nopCommerce (before → after) |
|---|---|---|
| W1 | markup in a `<script>`'s text is still not the page's, but a submit button written there (`type="submit"`, or a `<button>` with no type) submits the form around the script, or through T9 the form around its partial: a `ui-event` with `built: script`, 🟡; one whose tag's own text returns false doesn't | 5 buttons, the 5 W1 named: `_Maintenance.DBBackups.cshtml:88, 91` → Maintenance's form (partial → form 24 → 25); `Plugin/List.cshtml:277, 279` → `plugins-form-local`; `Menu/MenuItemSelectProductPopup.cshtml:104` → its form. Blind spots 760, unchanged |

Sonarr and Kavita have no Razor views, so they were not rebuilt.

## 17. What layer 3 needs: load, jobs, queues, conditions and exits, ✅ URL rule, 2026-11-09 (D171–D176, D181)

Owner: "Yes, new session" (r3 step 1, `phases/r3-flows.md`). Code only, no paid AI, no agents. Commit `18277b3`.
Tests: 2,165 .NET (6 new, `FlowStartsTests`) + 55 extractor (5 new, `flow.test.ts`) pass. Fixtures: React and Angular projects
in `flow.test.ts`; the shop's `Shop.Core/Jobs.cs` (job interfaces, a queue, a runtime-typed command), `Shop.Web/Guards.cs`
(exits), `Messages.cs` (a Hangfire wrapper), its Razor `AccountController` (GET pages). Rules: D181; docs: `docs/code-graph.md`.

| # | Built | Sonarr | Kavita | nopCommerce |
|---|---|---|---|---|
| Runs on load | `on-load` facts: React effects (each a point, `Component.effectN`), a component's own hooks, `componentDidMount`; Angular `ngOnInit`, route resolvers and loaders; MVC GET actions returning a view. ❓ for an effect outside a body or with unlisted dependencies | 878: 190 `useEffect`, 10 `useLayoutEffect`, 678 own hooks; 0 ❓ | 139: 108 `ngOnInit`, 31 resolvers | 406 MVC actions |
| Is a job | `is-job` facts from job interfaces (`jobInterfaces`; Quartz, Coravel, `BackgroundService` built in) and Hangfire recurring registrations | 38 command handlers (`IExecute`1`) | 27 recurring (`scan-libraries` → `TaskScheduler.EnqueueScanLibraries`) | 17 scheduled tasks (`IScheduleTask`) |
| Queues | 🟡 `queues`: a command made in a queue call's argument → its handler; ✅ for Hangfire lambdas, also through an app's own wrapper passing its expression on | 17 🟡 (18 `Push`/`PushMany` sites); 4 ❓ for commands typed at runtime (the command API, the executor) | 144 ✅, 12 through `ScanJobQueue`; 4 ❓ resolved | none (no queue in code) |
| Conditions | `condition` facts: per call, `if`/`else`/`?:`/case/`catch`/loop, quoted, innermost first | 10,163 | 11,000 | 18,795 |
| Early exits | `exit` facts: D176's framework results returned, and every `throw` | 854 (821 `throw`, 15 `NotFound`, 12 `Redirect`…) | 939 (447 `throw`, 350 `BadRequest`, 69 `NotFound`, 62 `Unauthorized`…) | 2,388 (1,324 `Redirect…`, 962 `throw`, 89 `Challenge`…) |
| ✅ URL rule | `http-match` facts, each 🟡 with the test it failed; a line is ✅ when a call behind it is | calls-http ✅ 0 → **86**, 🟡 129 → 43 | ✅ 0 → **405**, 🟡 445 → 35 | forms unchanged (✅ 248, 🟡 115, each now with its reason); its one script call 🟡 → ✅ |

**Totals** (before → after): Sonarr 40,413 → 40,613 points, 183,499 → 184,151 lines, blind spots 327 → 331 (+4 runtime-typed
commands). Kavita 32,403 points, 156,300 → 156,432 lines, blind spots 122 → 115 (4 Hangfire ❓ resolved through the wrapper; 3 fewer
URL ❓). nopCommerce 44,072 points, 204,784 lines, 760 blind spots, all unchanged (new facts only). Load errors 0; cross-checks unchanged.

**The D177 trial's starts are covered:**
- Sonarr **Add series**: `useAddSeries` → ✅ `POST /api/v5/Series`; `SeriesAddedHandler.Handle` → 🟡 `queues` → `RefreshSeriesService.Execute`,
  an `is-job` (`RefreshSeriesCommand`). **Series details** opens with 9 hooks and 3 effects. Before, only tests reached `Execute` by `calls`.
- Kavita **Want to Read**: all five want-to-read calls ✅. **Series detail** and **the manga reader**: `ngOnInit` facts. **Scan libraries**:
  `is-job` (recurring, `scan-libraries`) on `EnqueueScanLibraries`, which ✅ `queues` `IScannerService.ScanLibraries` through `ScanJobQueue.Enqueue`.
- nopCommerce **Register**: 59 conditioned calls and 7 exits; D174's example comes out exactly: `RedirectToRoute(NopRouteNames.Standard.REGISTER_RESULT,
  new { resultId = (int)UserRegistrationType.Disabled, returnUrl })` under `if _customerSettings.UserRegistrationType == UserRegistrationType.Disabled`;
  `RegisterCustomerAsync` under `if ModelState.IsValid`. **QueuedMessagesSendTask**: an `is-job`. The GET `Register` is an `on-load`.

**What changed in matching, and why:** Kavita's `environment.apiUrl` is now read as written (`http://localhost:5000/api/`), so its calls match whole
routes instead of their tails: 19 lines dropped, all tail matches to the wrong controller (`getChapterMetadata` → `GET /api/ColorScape/chapter` and 4
more; the right `GET /api/Chapter` stays, now ✅); 14 added (1 right, `getExternalSeriesDetails`; 13 🟡 where the path is mostly runtime). Sonarr's
config now states `@GET` on its four query helpers (they fetch with GET when no method is given), as a setting, not code.

**Faults found, general, not fixed (the owner's call):**

| # | Fault | Where it shows | Suggested fix | Size |
|---|---|---|---|---|
| X1 | A call after an early exit (`if (!x) return;`, `if (…) throw …;`) isn't marked as running only when that condition is false: D174 reads the conditions *around* a call, not guard clauses before it | handlers and services written guard-first, on every stack | add an `after-exit` condition: the guard's text, meaning "only if not" | small |
| X2 | A URL whose only fixed part is the API root (`/api/{*}`) matches every endpoint with that many segments, 🟡 | Kavita `saveFilter`, `encodeFilter`: 2 calls → 6 lines | ❓ when no fixed segment past the root | small |
| X3 | The API root comes from the default build's file (Angular `environment.ts`); a build that swaps the file (`fileReplacements`) could change the path and give a wrong ✅ | Kavita: prod uses `${BASE_URL}api/`, same path, so no wrong ✅ today | read `fileReplacements` and require every variant to give the same path | small–medium |
| X4 | A runtime route value next to a fixed route of the same shape is "2 endpoints fit", 🟡, even when the value's type rules the fixed one out | Sonarr `DELETE /api/v5/Queue/{*}` vs `/Queue/bulk` (5 calls); Kavita `getSeries` | use the value's type (a number can't be `bulk`) | small |
| X5 | Hooks that rerun on change aren't read: `componentDidUpdate`, `ngOnChanges`; nor jobs scheduled by type name (Sonarr's `TaskManager`: `TypeName = typeof(RefreshSeriesCommand).FullName`) | Sonarr's 12-hour Refresh series schedule | read when a flow needs it | small each |

Next (D158): a narrow fresh check of these additions only, estimated at 60–100K tokens, on the owner's go.

## 18. Fresh check (D158) of section 17, 2026-11-09

One fresh agent, read only, on the owner's go ("Yes, run the check"); ~116K tokens, over the 60–100K estimate. About 32 spot checks, 30 held.
**No wrong ✅**: 12 random ✅ frontend lines and 10 parameterised ✅ matches (Sonarr, Kavita) each reach their one endpoint; all 17 Sonarr
🟡 `queues` lines match the source's `Push(new …)` one for one; Kavita's ✅ `queues` hold, through `ScanJobQueue` too; `is-job` complete on
Sonarr (38) and nopCommerce (17); React effects 200 of 201 (the other is commented out); Angular `ngOnInit` 108 of 108; conditions quoted
exactly and innermost first; exits right on Sonarr, Kavita and nopCommerce `Register`. The three trial chains hold.

Findings (verified against the graphs), both general, not fixed:

| # | Finding | Example | Fix idea | Size |
|---|---|---|---|---|
| Y1 | An MVC action whose view name is made at runtime gets no `on-load` (only the ❓ for the view): "calls `View(…)`" doesn't need the name | nopCommerce: 5 actions, `CatalogController.Category`, `Manufacturer`, `ProductController.ProductDetails` (**the D177 trial's product page**), `ProductReviewsAdd`, `TopicController.TopicDetails` | write `on-load` for these too, `view` unknown | small |
| Y2 | A job that queues itself again has no line: the graph drops every line from a point to itself | Kavita `TaskScheduler.cs:542` `ScanJobQueue.Schedule(() => EnqueueScanLibraries(force), runAt)` (and 573, 646): "busy, try again in 3 hours" is lost | allow a self `queues` line, or a `requeues` field on the `job` fact | small |

Minor: a recurring job's `schedule` is empty when it's a Hangfire `Cron.Daily` (a method, not text; Kavita `TaskScheduler.cs:168`); an MVC `on-load`
for `View(model)` has `view: ""` where MVC's default is the action's name. Both read as "not known" today, never wrong.
