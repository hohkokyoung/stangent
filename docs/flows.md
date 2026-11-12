# Flows (contract `flows` v1, rebuild layer 3)

A **flow** is one thing a person or the system does, laid out by code from the code graph: who starts it, what they
enter, what's checked, the steps with the conditions around them, where each branch ends, and the flows it leads to.
No AI anywhere (D155, D176). Decisions: D170–D180, D183, D184, D186–D189; plan and scores: `phases/r3-flows.md`.

## Commands

| Command | What it does |
|---|---|
| `stangent flows build` | Builds every flow from `.stangent/graph.sqlite` and writes its files where the layout below puts them, removing files that moved or are gone. Prints counts by kind, guesses, ❓, files written, removed or unchanged, and with an `areas.yaml` the layout: areas, folders, flows per folder, the `shared/` count and the start flows in `unplaced/`. |
| `stangent flows show <id> [--format md]` | One flow as written by the last build, found through `index.json`'s `file`: its JSON, or its Markdown. |
| `stangent flows check` | D177's bar items 3–5 by code: every 🟡 has a reason and every ❓ says why; the files are byte for byte what the graph builds now; every start is a flow or a stated reason. Exit 1 with the problems when one fails. |
| `stangent flows guesses [--top N] [--cause <id>]` | Step 6 (D235–D237): every 🟡 and ❓ line on a flow by code line, in a cause and a bucket, ranked by start flows touched; each run-time line's `dependsOn`, each app-specific line's `question`. Exit 1 (`flows_guesses_failed`) when a line fits no cause, a reason sits on the wrong kind of step, or a dependency or question is missing. See "Guesses and ❓". |

Output follows `docs/cli-conventions.md`. A missing graph is `no_graph` (exit 3): run `stangent graph build` first.

## Files (D180, D230, D232)

Contract `flows` v1's paths. Until `specs/areas.yaml` exists, every flow is flat in `specs/flows/`. Once it does, `flows build`
reads it (`docs/areas.md`) and puts each flow under its area: one writer for flow files.

| Flow | Folder |
|---|---|
| a start flow (click, screen opening, job, handler) in an area | `specs/areas/<area>/` |
| a start flow tied and unanswered, or in no area | `specs/areas/unplaced/` (listed by `flows build` and `areas check`) |
| a shared flow whose reaching start flows all sit in one folder | that folder (an `unplaced/` start counts as its folder) |
| any other shared flow | `specs/areas/shared/` |

`<area>` is the area's name as a flow id is made (D176): ASCII letters and digits lowercased, every other run one dash
(`Settings › Indexers` → `settings-indexers`), `area` if nothing is left. Two names giving one slug: the first in ordinal
order keeps it, the others take `-2`, `-3`; `shared` and `unplaced` are never an area's. A rename, a tie answer or a shared
flow gaining a second user moves files; git shows them as renames. Each area's `page.md` beside its flows is written by
`areas check`, never by `flows build`.

- `<folder>/<id>.json`: one flow, contract `flows` v1. Committed; the same graph, settings and `areas.yaml` write the same bytes (D176).
- `<folder>/<id>.md`: the same flow as fixed sentences filled in by code (D173), with a generated-file marker. Never read back.
  Its "Links" are relative Markdown links to the linked flow's `.md`, wherever it sits (``→ [`shared-x`](../shared/shared-x.md)``).
- `specs/flows/index.json`, always here: every flow (`id`, `name`, `kind`, `home`, `file` (its `.json` under the repo), `start`,
  `point`, counts, the flows it links to) and `skipped`: every start code found that isn't a flow, with its reason (D177 item 5).
  Tools find a flow by id through `file`.

A flow file holds: `contract`, `version`, `commit`, `settings` (`hubCallers` 10, `depth` 8), `id`, `name`, `kind`, `home`
(the folder of its start's code, for areas, D178), `start` (`what`, `at`, `code`, `point`, `triggers`: every button or key that
runs it), `actor` (`who`, `why`, `at`, `label`), `inputs`, `checks`, `steps`, `exits`, `scenarios`, `links`, `guesses`,
`unknowns`, `displayOnly`. Every `at` is `path:line` at `commit`, with the `code` written there (D173's proof line).

## Starts (D171)

| Kind | Starts at | Name (D176 item 2) |
|---|---|---|
| `click` | each `ui-event` handler, one flow per handler, every button that runs it in `triggers` | the button's text key through the app's `en.json` (or `en-US.json`, `en_US.json`; first file in path order holding it), else its literal text, else the handler's name |
| `screen-opens` | each route (`page`): its guards, its resolvers and loaders, then the screen it shows; each MVC GET action returning a view (`on-load` `mvc`) | the route; the action's route |
| `job` | each `is-job` method (commands, scheduled tasks, recurring registrations) | the command's type, the recurring job's id, else the method |
| `handler` | each event handler (`handle` facts) that isn't a job: the flows a sent message links to (D173) | `Handler on Event` |
| `shared` | each hub and depth stop a flow continues into (D189): the point a `continues` link names, walked once; a call written to an interface walks its implementations at the root, one branch each (🟡 when several); built transitively, so a shared flow's own stops get theirs | the point |

Not a flow, and said so in `index.json`'s `skipped`: a button with no handler, an inline or prop handler, a handler not on the
map, a click that reaches no server call, save, message or queued work ("screen behaviour only", D171), a screen whose opening
reaches none ("display only", D176 item 5), and on-load code no route's screen reaches.

## The walk (D172, D176, D184)

A flow is a tree walked forward from its start. For each point, its **call sites** (`call-site` facts, D184) and the facts at
the same lines are read in source-line order; each line gives, in this order:

1. an HTTP call the graph matched (`http-match`): a step `calls VERB url` (🟡 with the match's reason when it isn't ✅; for a URL
   its callers give, D190, only the values given by the flow's own code: an `origin` on the branch, reached, or called by a
   point on it or the component or hook holding one; none such → every value). A button whose onclick calls a script function
   with the route (D191) runs that function first, 🟡, and the route is the request on the script's line that sends to that
   argument (its `call-open`), 🟡; never reached → at the button. A component's fields whose initialisers call something are
   walked when the component is, first (D193). Then the
   endpoint's action as the next step, at the line its name is written on. The route hop isn't a call. Calls on that line aren't walked.
2. a table read or saved (`access` with a mapped table): ends `database`.
3. an outside call (`call` facts from `http-services`): ends `outside-call`.
4. queued work (`queue`, `job`): a step `queues X, which runs later`, linked `runs-later` to the job's flow (D173).
5. an early exit (`exit`): one of `exits`, with its conditions (D174, D176 item 4).
6. a route's on-load hook (`on-load`): a step `runs on open`, walked into.
7. each call (a field or property read, such as an Angular signal `this.seriesId()`, is none, D189; one holding a function
   written in place, `getPageUrl = (n) => …`, is): a constructor of an event some handler handles ends `message`, one `message` link per handler (the bus is never
   walked); a callee with 10 or more callers, counted through the interface or base member it implements, is a `hub`, shown
   and not walked, unless its own body reads or saves a table, which ends `database` (D172's order) and still continues
   into the hub's shared flow (D196); otherwise each
   implementation it dispatches to (several → one 🟡 branch each) is a step, walked into. A call through a value taken from
   another function's result (`const predicate = getFilterTypePredicate(type)`, then `predicate(…)`) whose function layer 2
   couldn't name (the call site's `fromResult`) is 🟡 "calls a value taken from X's result": X stands for it and is walked (D210).
   An element inside one of the repo's components that draws nothing while a prop is unset (`if (!isOpen) return null`, then
   `{children}`) is 🟡 "shown only when Modal draws it" (the call site's `closedBy`, D213).
8. a ❓ the graph recorded there: ends `unknown`, with its reason; an outside HTTP call whose URL is built at runtime ends
   `outside-call`, still a ❓ (D198).
9. a navigation (`navigate` fact, D192): a step "goes to X" ending `screen`.
10. a call into a package (D188); a returned ASP.NET MVC `View(…)`/`Redirect…(…)` ends `screen` (D199): a step `calls X in the package Y`, ending `unknown`, "a package's code, not walked"; not a ❓.

Before an action runs, its access filters (global, controller, action: the `auth` facts' `filterType`) and the controller's own
`OnActionExecuting` overrides are steps `runs first`, walked like calls. A route's guards are the first steps of its screen.

A point already on its branch is `backTo` that step (a loop, D176 item 1); one already walked elsewhere in the flow is
`sameAs` that step, not walked again. At 8 real calls a branch ends `depth`, with a `continues` link. Every `continues` link
(a hub's or a depth stop's) carries `flow`: the `shared` flow that walks on from there (D189). A reader, or the scorer, follows
it to see the shared code's steps; its actor is "the flow that continues here". A child component whose walk
reaches no HTTP call, save, message or queued work is display only: named in `displayOnly`, not shown as steps (D176 item 5).
Steps are numbered in walk order, depth first; `from` is the step they come from (null: the start).

**Conditions:** each step carries the conditions of its own call's `condition` fact, innermost first, quoted exactly (D174):
`if`, `else`, `case`, `catch`, `loop`, `null`, `unless` (a guard earlier in the block, D182). **Scenarios:** the main path, then
one per exit with the conditions on the way to it, innermost first (D179). **Checks:** the `rule` facts on the properties of each
endpoint's request body (D179). **Actor:** D179's rule on the first endpoint the flow calls: an access filter whose code or name
claims access, `[AllowAnonymous]`, `[Authorize]`, else the app's fallback policy (the action's project's, else the repo's only
one), else 🟡; a screen with no endpoint: its route guards, 🟡; a job or handler: the system.

## Guesses and ❓ (D175)

`guesses` lists every 🟡 with its reason: a step (an HTTP match's failed test, overloads, one of several implementations, a save
read off a repository method's name no proof followed), a `message` link (handlers found by the event's type), a `runs-later`
link (a command matched to its job by type), a `screen` link (an address no screen matches, or several screens fit), the
actor. Each guessed link carries its own reason (step 6). `unknowns` lists every ❓ with why. `flows check` fails on any with no
reason.

Code that certainly runs, only later or only when something happens, isn't 🟡 but a condition on its step (D240): "only when
the mutation is started (`mutate(…)` is called)", "after `setTimeout`'s delay", "only when the stream sends a value (`x`)",
"only when the event comes (`connection.on`)", "only while `isOpen` (Drawer draws nothing otherwise)". 🟡 means "this may be
wrong" only.

"One of N" names what it depends on: "…; which one runs depends on the `IProvider` the call is made on at run time" (D239).

### `stangent flows guesses` (step 6, D235–D237)

Every 🟡 and ❓ line on a flow, counted by **code line** (one guess in the graph, however many flows show it), sorted by a fixed
table of patterns on its reason (`src/Stangent.Flows/GuessReport.cs`) into a cause and one of D166's four buckets, ranked by
the start flows it touches. `--top N` (default 20) lists the top lines, `--cause <id>` every line of one cause.

| Bucket | Means | Causes |
|---|---|---|
| missing general rule | code could settle it with a rule for any app | overload, navigation Add, Identity store, handlers by type, queued command, library callback, function result, endpoints fit, onclick, MVC view folder, no screen matches, generated client; ❓ outside call not followed, no endpoint fits, view not found, no job runs |
| app-specific | a person answers once for this app | a table read as a job's queue, actor with no guard; ❓ no such service, a setting found in no config file |
| unknowable | stays ❓, saying so | reflection |
| only known at run time | depends on the user's setup or data (D239: runtime evidence not built) | one of N, URL a parameter, screen chosen at runtime, several screens; ❓ outside address at runtime, frontend URL at runtime, command type at runtime, view chosen at runtime |

Each run-time line carries `dependsOn`, each app-specific line a `question`. The command fails (`flows_guesses_failed`) when a
line fits no cause (D237 item 1), a reason sits on a kind of step its cause can't (item 2 by code: a queued-command reason on a
redirect), or a run-time or app-specific line has no dependency or question.

Rules step 6 added to layer 2 so fewer lines need a guess: a generic repository's method followed through its implementation
to a database library's own call (linq2db, Dapper, EF Core…) proves the read or save (`access-proof` facts); an interface
member written in a base class that doesn't list the interface dispatches to it; views a plain project's build copies out
(`Content`/`None` with `CopyToOutputDirectory`) are compiled by the Razor SDK's generator, borrowed from a loaded web project
or the SDK; a URL whose only fixed part is the API root (the leading segments more than half the endpoints share) is ❓; an
onclick's target is matched first by the verb its script function sends; every point in a test project (one referencing a test
framework or declaring tests) is test code.

## Known gaps

- Code inside a function handed to the repo's own code that keeps it isn't walked where it's handed over; a listener's
  (`connection.on`), a timer's (`setTimeout`, `setInterval`, D212) or a mutation hook's (`useMutation`, and a wrapper sending from one, D214) carries a condition (D240); a call to a handler interface's method is one 🟡 `message` step, not a walk into every handler (D201).
- Dispatch isn't narrowed by a generic controller's type arguments (Sonarr save-indexer: 32 🟡 `Test` branches); step 6 found
  the largest such line, `Settings.Validate()` (one of 76), can't be: `Settings` is typed by a plain interface (not built, D248).
- X4: a runtime value beside a fixed route of the same shape (`/Queue/{*}` against `/Queue/bulk`) is still "2 endpoints fit":
  the value's type isn't recorded by the TypeScript extractor's URL builders.
- ASP.NET Identity's managers' async calls are steps ending `database` "the Identity user store", 🟡 (D200).
- A package call is a step, not walked (D188), and never an `outside-call` (only HTTP clients are, G4); TypeScript's package calls
  aren't recorded.
- Navigation (D192): a returned redirect or a navigation the function returns after is an exit; any other is a step ending
  `screen` (`router.navigate…`, `location.href`, `navigate(…)`, `history.push`), not yet linked to the screen's own flow.
  `<Link>`/`routerLink` in markup isn't read.
- (Exits now include an app's own helper that ends the request, D186, and a filter's `context.Result` refusal, D187.)
- A shared flow runs once per point, not per caller: what a caller passes in isn't known there, so its conditions aren't narrowed.
- Removing `areas.yaml` moves flows back to `specs/flows/`; the area pages stay until removed by hand.
- Flow files are large on big repos (Sonarr ~160 MB, nopCommerce ~196 MB with the Markdown), shared flows about a third of Sonarr's.
- Angular templates' child components have no call-site lines, so a screen's children are reached only through code.
