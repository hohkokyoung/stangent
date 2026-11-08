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
