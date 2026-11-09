# Code graph (contract `code-graph` v5)

The map of the code that impact analysis walks (decisions D25–D28, frontends D30–D32, history D34–D42).
It's built by code from the code and its git history; it's never committed and has no AI in it. v2
(Phase 1b) added frontends: components, templates, pages, and the `renders` and `maps-field` lines.
Python and Flask (D127) grew v4 in place: new id forms and one new line kind, in the same shape.
v3 (Phase 1c) adds 📜 `changes-with` lines from git history and `meta.history`. v4 (the overhaul, 1.1 and 1.2) adds
`opens-screen` lines from Playwright specs, `meta.dirtyFiles` and `meta.notInCommit`. v5 (rebuild layer 2,
`phases/r2-graph-for-flows.md`) adds what flows need: `writes` and `reads` lines, the facts in "What flows read" below,
the `validation` detector, a React component's own handlers as points, and real verbs from an app's own verb attributes.
Rebuild layer 3's step 1 (`phases/r3-flows.md`, D171–D176) grew v5 in place: what runs on load, what a job is, `queues` lines,
conditions and early exits, and ✅ for a URL match code proves.

## Where it lives

- `.stangent/graph.sqlite`, git-ignored and tagged with the commit it was built from (D25). Its
  file layout has its own version (`meta.schemaVersion`, still 3: v4 added two optional `meta` values and no
  table). An older layout is rebuilt rather than migrated.
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
| `page` | `page:<tsconfig path>\|<route>`: an Angular Router or React Router route, with `route-render` on also one written with `render` or `children`; with `root-screens` on, also `page:<tsconfig path>\|(no route) <Component>` | the TypeScript extractor |
| `project` (Python) | `project:<path/to/requirements.txt>`, or `project:python` when no dependency file is above the code | the Python extractor |
| `type`, `method`, `property`, `field` (Python) | `py\|<file>#<Name>`, e.g. `py\|shop/service.py#OrderService.place`; a nested function is `outer.inner`; module and class variables are fields | the Python extractor |
| `endpoint` (Flask) | `endpoint:python\|<VERB> <route>`, or `endpoint:python:<dir>\|…` for a project in a folder | `flask-routes` |
| `view` | `view:<path/to/X.cshtml>`: a server-rendered Razor view, spanning its lines (N1) | `razor-views` |
| `form` | `view:<path>#form<n>`: a form in a view, in the order written; the handler its submit buttons run (N1) | `razor-views` |
| `project` (plain scripts) | `project:<C# project folder>/scripts`: that project's own `.js` and its views' inline scripts (N5) | the TypeScript extractor |

Test methods (xUnit, NUnit and MSTest attributes; `it`/`test` cases inside `describe` blocks, named
`Suite › case`; Python's `test_…` in `test_*.py` and `*_test.py`, or on a `TestCase`) are marked `isTest`. Ids are the same ones the
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
| `writes`, `reads` | method → table it saves to or reads (v5): `DbSet<T>` calls, `Add`/`Remove` on an entity's navigation collection, a generic repository's write- or read-named methods (`IBasicRepository<Series>.Insert`) | no |
| `contains` | project → file → type → member, table → column | no (structure) |
| `imports` | a Python file → the file or symbol it imports | no (structure) |
| `opens-screen` | a Playwright spec's `goto` → the screen it opens; stored apart (below) | no, and no other reader sees it |
| `queues` | code that queues work → the job that runs it later (D173): a queued command's type → its handler (🟡, `jobs`), a background-job lambda → its method (✅, `background-jobs`); the one line kind that may point at itself, for a job that queues itself again | yes, like `triggered-by` |

### Labels

- ✅ `certain`: **proved by code** (D175): the compiler proved it, or one exact match of constant text. In Python, where there's no
  compiler: one binding in scope, in the same module or through a relative import. A frontend call or a form's target is ✅ when
  its verb is constant, its path is constant text (only route-parameter values from runtime), its API root is constant, and
  exactly one endpoint fits (below, `http-match`).
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
- **Settings** (`stangent.yaml`, defaults shown): `history: { enabled: true, window: 10, minShared: 3, minShare: 0.2, maxFiles: 50, botAuthors: [], followRenames: true }`.
  The defaults were tuned on nopCommerce and Sonarr (D43); at window 10, three shared commits is always at least 30%.
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

## Enums, for comparing tables (C.2, D124)

With `table-facts` on (`fsd.compareTables`), `graph build` also reads each enum's name, members and values from the C# and TypeScript extractors, and writes them to `.stangent/graph-enums.json`, with the commit they were read at. A value the compiler can't work out is null, never a guess.

**Kept apart.** They aren't points, lines or facts, and the file isn't the graph. So the impact walk can't see them, and `graph export` gives the same bytes with the switch on or off, which a test holds. `code-graph`'s version doesn't move for them. CI sharing an exported graph doesn't get them: `fsd compare-tables` then says to run `graph build`. Turned on with no file there, the next build reads every project again. Off, the file is removed.

## Screens with no route: `root-screens` (D127)

A React app can show a component straight from its root, with no route: `token ? <Routed/> : <SignIn/>`. With the component
`root-screens` on (`components: { root-screens: true }`, off by default because it adds pages), each one is a `page` point named
`(no route) <Component>`, with a ✅ `routes-to` line to it, so it gets a reference page and can be a candidate for an FSD section.
Two components of one name are told apart by file. Worked out from the JSX by the TypeScript compiler. No component or folder name decides.

The root is what `render`, `createRoot(…).render` or `hydrateRoot` from `react-dom` mounts, outside tests. Its outermost component is walked, not counted, unless
it renders nothing of its own: then it's the only screen. Library wrappers (providers, routers) and elements are walked through. Inside:

| What the code says | Counts as |
|---|---|
| On another branch from the routes: the other side of `?:`, or another `return` | a screen |
| Always shown beside the routes, or wrapped round them | layout, not a screen |
| Has routes in its own body | the path to the routes: walked, not counted |
| Already routed elsewhere | no second page |
| No routes under this root at all | every component the root shows is a screen |
| Shown by a condition beside the routes (`{open && <X/>}`) | ❓, could be a screen or a pop-up |
| `<Picked/>` from a prop or a variable, `{views[name]}`, a component wrapped in a call | ❓, never a guess |

❓ is a blind spot from detector `root-screens`. With the component on, `graph build`'s result has `rootScreens: { screens, unresolved }`.

**What code can't tell.** With no routes, layout looks like a screen. A spinner shown alone by an early return is a screen by this rule, and only a person can say otherwise.
A prop or state holding an element (`{view}`) has no JSX to read and is taken for text, with no ❓.

Off, the output is the same as before the component existed.

## Routes written with `render` or `children`: `route-render` (D127)

Older React Router writes a route as `<Route path="/x" render={props => <X/>} />`, `children={<X/>}` or `<Route path="/x"><X/></Route>`.
With the component `route-render` on (`components: { route-render: true }`, off by default because it adds pages), each is a `page`
with a ✅ `routes-to` line, detector `route-render`, to every component named in that JSX, or in the function `render` names.
A route naming `component` or `element` keeps that, and nested `<Route>`s stay routes of their own.

No component of the app to read (`render={picked}` from a prop, only library tags such as a redirect, only `<div>`s) still gives the page, with a ❓ from detector `route-render`, never a guess. With the component on, `graph build`'s result has `routeRender: { routes, unresolved }`.

**Who's read.** A plain React app (a `package.json` naming React, no tsconfig) is read in a repo with Python, or with `root-screens` or `route-render` on. Any other repo keeps the map it had. With a tsconfig, an app is always read.

Off, the output is the same as before the component existed.

## Screen tests: 🟡 `opens-screen` (D115)

`stangent workflows tests` resolves a test's gotos with this same method, and writes nothing to the graph (`docs/spec.md`, D149).

An existing Playwright spec whose `goto` matches a screen's route is a line from the spec and line to the `page` point. It says a
test exists: evidence strength `read`. Only a real run can make it `ran`, and the graph never runs anything.

`stangent graph screen-tests [--screen <route or page id>]` lists `links` (with `spec`, `line`, `goto`, `route`, `label`, `strength`,
`linesMatchCommit`, a `note` saying why when that's false, and a `test` citation) and `unresolved`, plus counts: `specs`, `gotosResolved`, `blindSpots`. `PlaywrightLinks.Answer` is the same in
code. An unknown screen is `not_found`.

- **Which files:** `*.spec.*` and `*.test.*`, Playwright's own default names, that git tracks or doesn't ignore. A file counts if it imports
  Playwright, or has a `playwright.config.*` in its folder or above. Another runner's tests are left alone.
- **Which calls:** `goto` on a receiver named exactly `page` (`page.goto`, `this.page.goto`), not in a comment or a string, whose whole
  argument is one string. `http://localhost` and other loopback addresses count as the app.
- **Which screens:** the same segments as a route (`:param` and `*` match any one). Within a frontend the fewest wild segments win, so
  `/settings` isn't also `/:id`.
- **Which frontend:** a spec inside a frontend links only to that frontend (the nearest around it that has screens; one at the repo's
  root holds everything). A spec outside every frontend links only when exactly one frontend matches, and a `:param` match counts, so
  `/orders/new` with React `/orders/new` and Angular `/orders/:id` is ❓. A link never crosses apps on a guess.
- **Everything else is ❓,** with `detector: playwright-specs`, and is never guessed: a built URL, another receiver (it may be a page
  object), another address, a hash route, a relative URL, a percent-encoded path (a router decodes it and this read doesn't; one in the
  query is fine), a route no screen has, only another frontend's screen, several frontends' screens, a Playwright spec with no `goto`,
  and a spec or folder that can't be read (a link with no target, no permission).

**Kept apart.** The lines are `CodeGraph.ScreenTests` (`meta.screenTests`), not edges, and specs aren't points. So the impact walk,
scoring, reference pages, ticket starting points and the 1d splitter can't see them, the same way 📜 lines are kept from the walk. An
impact answer doesn't list them yet. A repo with no specs exports no `screenTests`.

**Known gaps.**
- `goto` in a helper or setup file (`cart-helpers.ts`, `login.setup.ts`) isn't read.
- A link says where a test lands first. It doesn't follow redirects (a `/` that redirects stays `/`) or the clicks after.
- A real `Page` named anything but `page` (`leader`, `adminPage`) is ❓.
- Razor and Blazor screens have no `page` points (D65), so specs on those apps are all ❓.
- Screens are matched without the app's base path, and the runner's `baseURL` isn't read.
- Ports are ignored: `http://localhost:4200/x` and `http://localhost:3000/x` are the same `/x`, so a port can't choose between frontends.

## Detectors (D17)

Detectors are switched on per repo in `stangent.yaml`. `detectors: []` (the default) runs all of
them; list names to run only those; `[none]` runs none. Per-detector settings go in `detectorSettings`:

| Detector | Settings |
|---|---|
| `aspnetcore-routes` | — |
| `microsoft-di` | — |
| `publish-handle` | `handlerInterfaces: [Ns.IHandler`1]`, added to the built-ins (MediatR, MassTransit); `busInterfaces: [...]` for message buses that route by type name, whose same-named events in other assemblies also link (e.g. eShop's `IIntegrationEventHandler`1`) |
| `entity-tables` | `entityBaseTypes: [Ns.BaseEntity]`, for ORMs mapped by convention |
| `config-keys` | — |
| `validation` | — (v5: rules as `rule` facts, below) |
| `jobs` | `jobInterfaces: [Ns.IExecute`1, Ns.IScheduleTask]`: an app's own job interfaces, added to the built-ins (Quartz's `IJob`, Coravel's `IInvocable`; a .NET `BackgroundService`'s `ExecuteAsync`) |
| `conditions` | — (conditions and early exits, below) |
| `on-load` | — (MVC GET actions returning a view, below) |
| `http-services` | `services: ["stock-api=Shop.Stock"]`, for service names without an Aspire AppHost |
| `playwright-specs` | — (below) |
| `flask-routes` | — (under Python, below) |
| `frontend-http` | `wrappers: ["useApiQuery:path", "useApiMutation:path:method"]`: in-house call helpers as `name:urlProperty[:verbProperty][@VERB]` (a plain `name` takes the URL as its first argument; `#N` names the Nth argument instead of a property, e.g. `send:#1:#0`; `@VERB` gives the verb, else it's read off the name: delete/remove, put/update, patch, post/save/mutation, else GET; a verb read off the name is 🟡, D175) |

`aspnetcore-routes` also takes `groupConventions: ["Ns.IEndpointGroup=/api/{type}"]` for route groups
mapped by convention rather than in code (CleanArchitecture registers each `IEndpointGroup` at
`/api/<ClassName>` by reflection). Values containing `{` must be quoted in YAML.

Each detector extracts facts per project (with Roslyn), then links once across the whole graph
(without Roslyn). That split is what lets incremental builds re-extract only changed projects.

## What flows read (v5, D154)

Facts code reads to lay out a flow; no AI. Each is a row in the `facts` table (`owner`, `kind`, `data`).

| Kind | On | Data | From |
|---|---|---|---|
| `ui-event` | the component (React) or template (Angular) | `element`, `event` (`onPress`, `click`), `handler` (a point id; `inline`; `prop:…`), `keys` (text keys passed to `translate`/`t`/the `translate` pipe, `\|`-separated), `text` (literal text and label attributes) | the TypeScript extractor |
| `ui-event` | a view (Razor) | `element` (`button`, `input`), `event` (`submit`, `click`), `handler` (the `form` point a submit runs, or the page an `onclick` opens; `""` for a plain button); a submit button whose inline `onclick` ends `return false` is a `click` (V2), `keys` (keys passed to anything typed `…Localizer`: nop's `@T("…")`, `@Localizer["…"]`), `text`, `id`, `name`, `type`, `built` (`script`: written by a script's text, so a guess, W1) | `razor-views` |
| `form` | a `form` point | `method` (as written; else POST when the form tag helper builds it or `Html.BeginForm`, GET for a plain `<form>`, T1), `route`, `action`, `controller`, `area` (tag helper values, constants resolved), `url` (a literal `action`), `runtime` (`yes`: a target made at runtime), `via` (`onclick`: read from a button's `onclick`, V2). A button's own target with no form in its file has `method` `""`: linking takes it from the forms around its partial (V3) | `razor-views` |
| `view` | a view | `path` (in its project), `model` (the `@model` type), `controller`, `area` (from its folder), `kind` (`mvc.1.0.view`, `mvc.1.0.razor-page`) | `razor-views` |
| `dom-event` | the function binding it, or the view (inline scripts) | `selector` (`#id`, `.class`, `document`), `event`, `handler` (a point id; `inline`), `cancels` (`yes` when a statement of the handler's own body calls `preventDefault()` or, for jQuery, returns `false`, V2) | the TypeScript extractor |
| `guard` | a page | `guard` (id), `name`, `via` (`canActivate`, `canMatch`…); a parent route's guards hold for its children | the TypeScript extractor |
| `auth` | a controller action | `rule` (`authorize`/`anonymous`), `policy`, `roles`, `from` (`method`, `class X`): `[AllowAnonymous]` on the method wins, `[Authorize]` is read up the base controllers | `aspnetcore-routes` |
| `auth` (an app's own filter, N3) | a controller action | `rule` `filter`, `filter` (`CheckPermission`), `args` (by parameter name, constants and enum members resolved: `permission = "Accounts.Manage", deny = Json`), `stage` (`authorization` when the filter runs there, else `other`), `filterType` (the filter class's point, D184; `""` off the map), `basis` (the evidence: `denies`, its code, its own or a base class's in the repo, sets `context.Result` to a refusal — challenge, forbid, 401/403, access denied, sign-in, or at the authorisation stage any other 4xx such as not found or bad request (V4); `name`, its name says Authorize, Authorization, Permission, Access or Role; 🟡 `stage`, it only runs at the authorisation stage; 🟡 `result`, an action filter that sets `context.Result`), `from` (`method`, `class X`, or `global` on the project for `options.Filters.Add…`). An MVC filter attribute (from `TypeFilterAttribute`/`ServiceFilterAttribute`, or a filter itself) with none of these isn't one. Only `denies` and `name` claim access. `[AllowAnonymous]` doesn't remove it: only the filter's own code could | `aspnetcore-routes` |
| `fallback-auth` | the project | `authenticatedUser` (`required`), `schemes`: `options.FallbackPolicy = …`, which holds for actions with no marker of their own | `aspnetcore-routes` |
| `rule` | the property checked | `rule` (`Required`, `GreaterThan`, `SetValidator`…), `args` (constants as values, enum members by name: `DataType(EmailAddress)`), `via` (`attribute`/`fluentvalidation`), `set` (`PostValidator`), `message` (any `WithMessage…`: a constant's text, else the expression), `conditional`, `modifiers` (the rule's other modifiers by name, `|`-joined: Sonarr's `AsWarning` means it only warns, V5), `severity` (a constant `WithSeverity(…)`'s member). A chained call on the rule's options (`IRuleBuilderOptions`: `WithMessage`, `When`, an app's `AsWarning`) is a modifier, one on the rule builder a rule (T12); by its `With…`/`When…`/`Unless…` name only when the type isn't known (N4) | `validation` |
| `access` | a method | `entity`, `mode` (`write`/`read`), `via`; linking turns it into `writes`/`reads` when the entity has a table | `entity-tables` |
| `on-load` | a component, a hook, a page or an MVC action | what runs as a screen opens (D171, D176): `framework` (`react`, `angular`, `router`, `mvc`), `hook` (`useEffect`, `useLayoutEffect`, a component's own `use…` hook, `componentDidMount`, `ngOnInit`, `resolve`, `loader`, `View`), `handler` (the point that runs: an effect's own point `Component.effectN`, the method, the resolver's `resolve`, the action), `deps` (an effect's dependencies, `\|`-joined as written), `runs` (`first render`; `first render, then again when a dependency changes`; `first render, then after every render` for an effect with no list; `first render, then every render` for a hook; `before the screen opens`; `when the page is requested`), `key` (a resolver's key), `view` and `viewFrom` (MVC: `name` given, `action` the default (the action's name), `runtime` chosen at runtime, `view` empty), `verbs` | the TypeScript extractor; `on-load` (MVC, while linking) |
| `is-job` | the method a job runs | a system job (D171): `kind` (`command`: a generic job interface's handler, `command` its type; `scheduled`: a non-generic one; `hosted`: a `BackgroundService`; `recurring`: a Hangfire `RecurringJob.AddOrUpdate`, with `id`, `schedule` (constant text) and `scheduleCode` (the schedule's code when it isn't text: `Cron.Daily`)), `interface` | `jobs`; `background-jobs` (recurring) |
| `queue` | the method that queues | a command made in a call's argument (`Push(new RefreshSeriesCommand(…))`, also inside a lambda or a list) and handed to a method taking any command: a parameter typed as the job interface's constraint (`where TCommand : Command`), a type parameter constrained to it, or a list of either. `type`, `via` (`IManageCommandQueue.Push`), `arg` (as written). Linking draws `queues` to each `is-job` of that `type`, else ❓. One passed along from a parameter, field, property or type parameter isn't queued by this call; one whose type is made at runtime (`Deserialize<Command>(…)`) is ❓ | `jobs` |
| `condition` | the calling method or function | a call under at least one condition of its own member (D174): `call` (`Type.Method`; C#: calls to the repo or to anything outside `System.*`, `System.Net.Http` kept; TypeScript: every call), `target` (its point, when on the map), `conditions` (JSON, innermost first: `kind` `if`/`else` (also `?:`), `case` with the `switch` value as `text` and the label as `case`, `catch` (its declaration and `when` filter), `loop` (its header as written), `null` (C#'s `x ?? throw`), `unless` (after a guard: an earlier `if` in the same block or `switch` case, with no `else`, that always returns, throws, breaks or continues; the call runs only when its condition is false); `text` quoted exactly; `line`) | `conditions`; the TypeScript extractor |
| `exit` | the method or function | an early exit (D174, D176's fixed list): `kind` (`BadRequest`, `NotFound`, `Unauthorized`, `Forbid`, `Challenge`, `Redirect` (also `LocalRedirect…`), `StatusCode` with a constant code of 400 or more, `throw`), `result` (the method or result type: `RedirectToRoute`, `NotFoundResult`), `status`, `text` (the returned expression or the `throw`, exactly), `exception` (the thrown type), `conditions`. Only an ASP.NET Core result that the member returns (through `await`, casts and `?:`; a `return`, an expression body or a lambda's body) counts; so does a returned call to the app's own helper that sets a constant 4xx/5xx `Response.StatusCode` or returns only such results, two deep (D186, `result` the helper's name); a filter setting `context.Result` doesn't | `conditions`; the TypeScript extractor (`throw`) |
| `call-site` | the calling method, function or component | where each call is written (D184), so a flow can quote its line: `target` (the point called; each candidate, 🟡, when the compiler couldn't pick an overload), `label` (`0` ✅, `1` 🟡), `kind` (`calls`; TypeScript also `renders`, a JSX tag), at the line the call expression starts (a chain's first line). One per call, not per line kind: impact never reads it | the C# extractor; the TypeScript extractor |
| `http-match` | the calling method, function or form | each frontend call's or form's match, its verdict and why (D175): `endpoint`, `label` (`certain`/`guessed`), `reason` (`""` when ✅; else each failed test: `verb read off the wrapper's name…`, `verb unknown`, `2 endpoints fit (GET /api/v3/series and GET /api/v5/series)`, `path built at runtime (…)`, `API root read at runtime ({base} stands for /api)`, a form's `method taken from the forms around its partial`, `controller or action taken from the view's own folder and name`, `opened by a button's onclick script…`, a generated client's `matched by nswag's naming…`), `verb`, `url`. A line is ✅ when any call behind it is. Written while linking | `frontend-http`, `razor-views` |

Also read by flows:
- **Runs on load (D171, D176).** Every React effect written directly in a component's or hook's body runs on first render; its
  function is a point of its own, `Component.effectN` (N counts that body's effects from 1), which the component `uses`, so impact
  still reaches what it calls. An effect anywhere else (inside a condition, in a plain function) or with dependencies that aren't
  a written list is ❓, never guessed. `componentDidUpdate`, `ngOnChanges` and `ngAfterViewInit` aren't read.
- **Verbs for D175.** A call's `verbFrom`: `literal` (written at the call), `library` (fixed by the API: `http.get`, `axios.post`,
  `fetch` with no method), `setting` (a wrapper's `@VERB`), `name` (read off the wrapper's name), `runtime`. Only the first three are
  constant. A const object's property (`export const environment = { apiUrl: '/api/' }`) is a constant value, as written in that file.
- **Handlers are points (G5).** A function a React component or hook (D184: `use…`) declares in its own body (`const handleSave = useCallback(…)`) is
  `Component.handleSave`; the component `uses` it, so impact and screen slices still reach what it calls, one step deeper.
  A value bound from a call (`const { addSeries } = useAddSeries()`) used in a handler gives a `calls` line to that function; when the
  function's returns hand that name back from a function of its own body (`return { saveProvider }`, through `return useOther()`,
  `...useOther()` and locals holding them, four calls deep), the line goes to that function instead (D184).
- **Wrappers' API root (G2).** A configured wrapper whose body passes its URL to a function returning `urlBase + apiRoot + path`
  puts that root on its calls (`{base}/api/v5/series`), so only that API version matches.
- **Verbs (G1).** Attributes derived from `HttpPostAttribute` etc., or implementing `IActionHttpMethodProvider` with literal
  `HttpMethods`, give the verb; templates come from a base constructor (`: base("{id:int}")`) or a `Template` property.
  Overrides of a `[NonAction]` method (`Controller.OnActionExecuting`) aren't actions.
- **Outside calls (G4).** Through Flurl or an app's own client type (`IHttpClient`, `…HttpClient`): a whole literal URL is a
  `call` fact; otherwise ❓ `outside HTTP <VERB> through <client>…`, naming the URL's known start. Never silent.
- **Tables (G3, D164).** EF's own `*ModelSnapshot.cs` first: each top-level `modelBuilder.Entity("Ns.Type", b => b.ToTable("Name"))`
  is a table (✅); owned types inside it aren't; a name that isn't a type (a join table) is a table with no entity. Then `DbSet<T>`
  (✅), an app's own `Entity<T>("Name")` map (🟡), and entities EF reaches through navigations (🟡; not properties configured as
  columns or owned; dropped where a snapshot exists and doesn't hold them).
- **Writes and reads.** Writes: `DbSet` Add/Update/Remove/Attach, `context.Add/Update/Remove(x)`, `Entry(x).State =` (not
  Unchanged/Detached), `ExecuteDelete`/`ExecuteUpdate` back to their DbSet, `Add`/`Remove` on a mapped entity's collection
  (a many-to-many add writes the join table the snapshot's `HasOne` pair names; without a snapshot, nothing), and a generic
  repository's write-named methods. Reads: any other call on a DbSet (LINQ, EF, the app's own query extensions; not `Entry`)
  and read-named repository methods. A name word must be whole (`All`, `AllAsync`, never `Allow`).
- **Server-rendered views (N1, N2).** Read from the C# the SDK's own Razor compiler makes for each `.cshtml` (its source
  generator, already run inside the compilation the graph loads), so markup, tag helper values and the view's C# are what the
  build sees; nothing is parsed twice. A view `uses` its `@model` and whatever repo code it names (`asp-for` properties,
  injected services); `renders` its partials (`<partial>`, `Html.PartialAsync`: beside the view, then the shared folders; a
  name with a folder or an extension is relative to the view) and view components (by type, or by name to the class MVC's
  rules make a component). Each form is a `form` point linked to the endpoint it posts to (`calls-http`): by route name (a
  conventional route's `name`), by action and controller (the view's own folder and name by default, 🟡), or by a literal URL; the
  endpoint with the form's verb wins over `ANY` at the same path (T10). A button's `asp-action`, `formaction` or `formmethod` makes its
  own `form` point (T3); `@Url.Action(…)`/`@Url.RouteUrl(…)` with constants is read as its action or route, any other server value is ❓.
  Such a button with no form in its file takes the method of each form around the partial that renders it, 🟡; with none, ❓ (V3).
  A submit button with no form submits the form around the `<partial>` that renders its view, through partials of partials: the partial
  `calls` that form point, 🟡 (T9); with none, ❓. A button that cancels its own submit doesn't (V2): an inline `onclick` ending
  `return false`, or a script binding to its id whose handler always cancels (`dom-event` `cancels`). Such a button, or a
  `type="button"`, with one constant `@Url.Action`/`@Url.RouteUrl` in its `onclick` gets a `form` point for the page it opens
  (`via: onclick`, 🟡: by GET, else by any verb the action serves, since what its script does with the URL is a guess). Markup
  inside a `<script>`'s text (a grid column's `'<button …>'` string) isn't read as the page's, except a submit button there: it
  submits the form around the script (or, through T9, around the partial), 🟡, `built: script`, unless its tag's own text returns false (W1).
  An action's or view component's `View(…)`/`PartialView(…)` `renders` its view by MVC's lookup: `Views/{controller}/{name}`,
  `Views/Shared/{name}`, `Areas/{area}/Views/…` first for an area, `Components/{component}/{name}` for a component. MVC's `{controller}`
  is the request's, so a component's view is looked up in the folder of each controller whose pages show it (through the views that
  invoke it, their partials and components), and so is a partial of a view outside a controller folder (Shared, component views),
  each 🟡 (V1); a path
  (`~/Plugins/X/Views/Y.cshtml`) by its longest tail that names a view in the project; a call to a base class helper that forwards to
  `View(…)` (`NopViewComponent.ViewAsync(model)`) as a `View()` of the caller (T4). With an app's own `IViewLocationExpander` (themes),
  every lookup is 🟡 and a view of the same path under another root (`Themes/X/Views/…`) is a 🟡 candidate too (T8). Never silent: a `.cshtml` the compiler
  made nothing for, a Razor Page, a form or view name made at runtime, and any lookup that finds nothing is ❓.
- **Areas.** `[Area]` is read up the base controllers; a conventional pattern with an `{area}` token (ASP.NET's
  `{area:exists}/{controller}/{action}`) serves the actions that have an area. Every action is an `mvc-action` fact
  (`controller`, `action`, `area`, `verbs`), which forms resolve against.
- **Plain scripts (N5).** Each C# project's own `.js` with no tsconfig (a server-rendered app's `wwwroot/js`) and its views'
  inline `<script>` blocks are a frontend of their own, read as browser scripts by the same extractor. Skipped: `lib`,
  `lib_npm`, `vendor`, `bower_components`, `gulp`, minified files, `gulpfile.js`, `*.config.js`, anything inside a tsconfig
  project. jQuery's `$.ajax`/`$.get`/`$.post`/`$.getJSON` are calls like `fetch`. An inline script's code belongs to its view
  (lines are the view's); a value the server writes into it (`'@Url.Action("Add")'`) is made at runtime, so the call is ❓. A
  view's button whose `id` a script binds (`$('#check-email').click(checkEmail)`) `calls` that handler, 🟡: the view's own
  scripts first, else the project's when only one binding fits. A handler is a function only when it is one or its body is that
  single call, else `inline` (T7). A global declared in several plain scripts gets a 🟡 line to each declaration (T2).
- **Which context a snapshot speaks for.** Its `[DbContext(typeof(X))]`, even in another project; its tables take X's assembly,
  and only X's navigation guesses are dropped. A snapshot without the attribute speaks for its own assembly.

## Cross-check against the framework's own answer (D164)

Every `graph build` compares the graph with what the framework wrote down, when the repo has it, and writes each disagreement to
`.stangent/graph-crosscheck.json` (git-ignored; counts in the build result as `crossCheck`). It never changes the graph: a stale
file would mislead it.
- **OpenAPI.** Each `*openapi*.json` / `*swagger*.json` outside `node_modules`, `bin`, `obj`: its operations against the endpoints of
  the project whose folder holds it (the whole repo when none does), by verb and path shape (`{id:int?}` = `{id}`; `ANY` fits any
  verb): `missingFromGraph` and `notInOpenapi`.
- **EF snapshot.** Per DbContext with a snapshot: entities our rules found that it doesn't hold (`foundByRulesOnly`), and ones it
  holds that no rule found (`inSnapshotOnly`).
- A file that can't be read, or of whose operations fewer than one in five fit any endpoint (a client's spec for someone else's
  API), is listed as `ignored` with the reason and counts for nothing.

Not read yet: hooks that rerun on change
(`componentDidUpdate`, `ngOnChanges`), jobs scheduled by type name, minimal APIs' `RequireAuthorization()`, React route-guard wrappers, resolving text keys to the words in the
language file, views compiled only at runtime (nopCommerce's plugins: a ❓ per view), Razor Pages' handlers as endpoints,
links (`<a asp-action>`) and redirects as navigation, and a partial looked up through the requesting controller's folder rather
than the view's own.

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

### Python (D127)

**Parsing only.** The bundled extractor (`py-extractor/` beside the stangent assemblies, standard library only) runs as `python3 -I -B` and calls `ast.parse`. Nothing it reads is imported or run, so an app's code, its packages and its environment play no part. `python`, the component, switches it off (`stangent components`). Off, a repo's graph has no Python in it.

- **Found by:** `.py` and `.wsgi` files. Each belongs to the nearest folder above it with `pyproject.toml`, `setup.py`, `setup.cfg`, `requirements.txt` or `Pipfile`, else to `project:python`. A repo with none has no Python project.
- **Skipped:** virtualenvs under any name (`pyvenv.cfg` or `bin/activate` inside), `__pycache__`, `site-packages`, tool caches, `dist`, `build`, `*.egg-info`, `node_modules`, and a C# project's `bin` and `obj`.
- **Python missing** → `python_missing` in `loadErrors`; the rest of the graph still builds. It needs 3.9 or newer (`python_too_old`). The Python version is part of the fingerprint, because it decides what parses.
- **A file it can't parse** (Python 2, say) is ❓, a warning and a count. It never stops the build.
- **Notebooks aren't read.** Each `.ipynb` is a ❓ and is counted in `notebooksOmitted`, never opened.

**What a call becomes.** Every call lands in exactly one count, in `graph build`'s `python`:

| Count | What it means | Line |
|---|---|---|
| `callsCertain` | One binding in scope, in the same module or through a relative import. `self.x()` found on the class or a known base. A local or `self.attr` holding an instance of a known class. | ✅ `calls` |
| `callsGuessed` | The same, but it rests on an absolute import (where it lands depends on how the app is started), a type hint (never enforced), a value a function returns, or a method that an outside base could have defined first. | 🟡 `calls` |
| `callsOutside` | Proved to land in a package or a built-in. | none |
| `callsNotInProject` | The receiver's type isn't known, but no project code has that name, so it can't land in the project. | none |
| `callsUnresolved` | The rest: a parameter or variable called, a method on an unknown value that project code does have a name for, a name bound to different things, `getattr`, `eval` and the like. | ❓ |

A name bound twice to different things is never picked between. Subclasses add `overrides` and `dispatches-to`, as in C#. A class call adds `uses` on the class and `calls` on its `__init__`.

**Imports.** `imports` lines are structure and aren't walked: what changes behaviour are the calls and uses. A relative import is ✅. An absolute one is searched from the file's folder up to the project's: one match is 🟡, several are ❓, and none is outside the project, unless a project file elsewhere fits the name and it isn't a standard module. That's ❓ too (`import x could be …`), because only `sys.path` could say.

**Flask (`flask-routes`).** `@app.route` and `@bp.route`, `@app.get` and the other verb shortcuts, `methods=`, `add_url_rule` with a function or `Cls.as_view()`, `Blueprint(url_prefix=)`, `register_blueprint(url_prefix=)` (which replaces the blueprint's own), and blueprints inside blueprints. Each verb and path is an endpoint with a `routes-to` line, so pages, slices and impact see them as they see ASP.NET's. `<int:id>` becomes `{id}`, and `<path:rest>` becomes the catch-all `{*rest}`. A constant URL or methods list is followed to its one assignment, even through an import.

- ✅ when the object is provably a Flask app or a registered blueprint, 🟡 when that rests on an absolute import.
- ❓, and no endpoint: a URL built at runtime, `.route` on something not provably Flask, `add_url_rule` with no `view_func`.
- ❓, with an endpoint: a blueprint not seen registered (🟡, at its own prefix: it may be registered through an import that can't be followed), a prefix built at runtime (the path holds `{prefix}`), and methods built at runtime (the verb is `ANY`).
- `flask_restful` and its relatives are ❓: their routes aren't read.

**React in plain `.js`.** A repo with Python also has any `package.json` naming React, with no tsconfig beside it, read as a frontend (`project:<path>/package.json`), with the settings its toolchain assumes. Its `fetch` and axios calls reach Flask's endpoints through `frontend-http`, unchanged. A repo with no Python doesn't get this, so its graph is the same as before. `python` also counts `frontendCalls`, matched, unmatched and built at runtime.

**Known gaps.**
- `sys.path` changes aren't read, so a blueprint imported that way shows as not seen registered, though it's really served.
- A URL segment that mixes text and a parameter (`/file-<name>.txt`) is an endpoint no frontend call can match.
- No `maps-field` lines: Python has no typed responses to match frontend fields against.
- Django, FastAPI and the like aren't read. Flask's templates aren't on the map; a view calling `render_template` makes a screen page.
- A call on a decorated function goes to the function, not the wrapper. Assigning over a function from outside its module (`mod.f = g`) isn't seen.

## Impact answers (contract `impact-report` v5, D27)

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
- **`fsdSections`** (v5, Phase 2b): the FSD sections the change reaches through the reference pages. A page is reached
  when a symbol one of its statements cites is changed or affected; its links in `specs/trace.yaml` lead to sections,
  each with its link states and whether every link has a person's decision ("unverified" otherwise, D53). Top 10;
  `--expand fsdSections` lists the rest. `status: none` when the repo has no pages or links. See `docs/spec.md`.
- Every answer is validated against `schemas/impact-report.v5.json` before it's printed (v1–v4 are kept for history).

`stangent graph show <id or Type.Member>` lists a point's lines in and out, and its blind spots.

## Line citations (1.1)

`stangent graph where <id or Type.Member>` gives a symbol's file and lines at the commit the graph was built from. In code,
`SymbolLines.Where(graph, symbol)`. The lines are the ones the extractors already stored, so nothing is read again.

Each match has `id`, `kind`, `name`, `file`, `startLine`, `endLine`, `commit`, `linesMatchCommit`, `generated`, `alsoIn`, `note`
and `citation`. `citation` fills `citation` in an `evidence` record (`docs/foundation.md`); the match's `commit` fills `commit`.
The answer around them has `commit`, `head`, `repo`, `dirty`, `dirtyFiles` and `ambiguous`. `repo` is the origin's name, else the folder's.

| Asked for | Answer |
|---|---|
| A method, property, field or type | Its declaration's first and last lines (attributes and modifiers included in C#). |
| Overloads, or a file compiled into several projects | Every match, `ambiguous: true`. None is picked; ask by id for one. |
| A partial type | The first part only: the first file by name, as the graph stores it. `alsoIn` lists files holding members outside those lines, and `note` says so. Each member has its own lines. |
| A TypeScript or Python symbol | The same, from its extractor. A Python declaration's first line is its first decorator. |
| An endpoint, page, table, column or config key found in code | The one line it was found on, so `startLine` equals `endLine`. |
| A file or project, or a config key with no declaration | `startLine` and `endLine` null. A file still cites, without lines; a point with no file doesn't. |
| Code in a checked-in generated file | Its real lines, `generated: true`, judged by file name (`.g.cs`, `.Designer.cs`, `.generated.`, `.gen.`). Source generators' output isn't on the map, so it's not found. |
| Nothing matches | `not_found`, exit 3. |

**Dirty trees.** A build records the files on the map that differ from the commit, tracked or untracked (`meta.dirtyFiles`, and
a count in the build's stats). The answer's `dirty` is true when there are any, and a match in one of them has
`linesMatchCommit: false` and `citation: null`. A match in an untouched file still cites: its lines do match. A change off the map
(a README, or a lock file the build's own restore wrote) isn't recorded, because no line the graph holds can be wrong for it.

**Files the commit doesn't contain.** A file on the map that isn't in the commit (`git ls-tree`), such as a gitignored generated file
that's compiled in, can't match it. It's in `meta.notInCommit`, not `dirtyFiles`, so `dirty` stays false; a match in one has
`linesMatchCommit: false`, `citation: null`, and a `note` saying why.

**Clean or don't know.** An export always writes both keys: `[]` is none, `null` is not known. A graph built outside git, or an export
or database from before a key existed, loads, has `linesMatchCommit: false` and no citation with a `note` saying to rebuild, and
`dirty: null` if `dirtyFiles` is what's missing.

**Known gaps.**
- A partial part with no members leaves no trace, so `alsoIn` misses it.
- A partial method or property holds its first part only, with no `alsoIn`. A TypeScript overload holds one signature's lines.
- A generated file with a plain name isn't marked.
- A file edited and put back during a build isn't seen. A changed project file can change the graph, not its lines, and isn't recorded.
