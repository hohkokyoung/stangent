# Flows (contract `flows` v5, rebuild layer 3)

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

- `<folder>/<id>.json`: one flow, contract `flows` v6. Committed; the same graph, settings and `areas.yaml` write the same bytes (D176).
- `<folder>/<id>.md`: the same flow as fixed sentences filled in by code (D173), with a generated-file marker. Never read back.
  Its "Links" are relative Markdown links to the linked flow's `.md`, wherever it sits (``→ [`shared-x`](../shared/shared-x.md)``).
- `specs/flows/index.json`, always here: every flow (`id`, `name`, `kind`, `home`, `file` (its `.json` under the repo), `start`,
  `point`, counts, the flows it links to) and `skipped`: every start code found that isn't a flow, with its reason (D177 item 5).
  Tools find a flow by id through `file`.

A flow file holds: `contract`, `version`, `commit`, `settings` (`hubCallers` 10, `depth` 8, and `outsideLibraries` when the
project adds any, D302), `id`, `name`, `kind`, `home`
(the folder of its start's code, for areas, D178), `start` (`what`, `at`, `code`, `point`, `triggers`: every button or key that
runs it), `actor` (`who`, `why`, `at`, `label`), `inputs`, `checks`, `steps`, `exits` (an exit may carry `unsure`, D300),
`handled` (D300), `scenarios`, `links`, `guesses`, `unknowns`, `displayOnly`.

**v2 (2026-11-14, D291–D295, D300–D302, D305; one bump for all, unreleased):** step kinds `push` ("pushes X to the browser", ends `push`) and
`file` ("writes a file through M" / "deletes a file through M", ends `file`); Identity store steps sorted by verb ("saves …",
"reads …", else "reads or saves …"); calls into known outside-service libraries are `outside-call` steps; a browser timer's
code that calls the server or changes something has a `runs-later` link "Runs X later, on a timer (setInterval)"; `handled`
lists thrown errors a catch on the way takes (`caughtBy`, `caughtCode`), which are no longer in `exits`; an ASP.NET filter
setting any `context.Result` is an exit (kind `StatusCode` with its constant status, else `Result` with the result's type).
Within v2 (D304, before v2 is released, no further bump): a handled error may carry `catchReturns` (what its catch returns, or the
`return` right after its `try`); a thrown error a catch turns into a new one says so in its `what` ("throws IOException, turned
into RecycleBinException at `file:line`"); a filter's stop names the endpoint's attribute value ("answers 400 for
DisallowRole(roles = ["Read Only"])"), a filter's redirect reads "sends the user to another address (…)" and its challenge
"sends the user to sign in (Challenge)". Every `at` is `path:line` at `commit`, with the `code` written there (D173's proof line).

**v3 (2026-11-14, D306):** a new end kind `waits`: code the walk leaves for a later call is one step "leaves code for later (X): it
runs only when something calls it afterwards, such as a later click, so it isn't walked here", ending `waits` ("kept by X, run by a
later call"), at the first line of the kept code, with that line's conditions; one per keeper `X` in each point. A click whose
handler reaches no server call once that code is left out is skipped as "screen behaviour only", and its reason adds "code it
leaves for a later action isn't walked (kept by X, …, D306)".

**v4 (2026-11-14, D310):** a virtual call on `this` (written `M()` or `this.M()`) runs only the override the object's class can
run: a flow that came in through a known class (an action on `SeriesController`, then the inherited
`RestController.TypedCreated`) narrows `GetResourceById(id)` to `SeriesController.GetResourceById`, certain. A shared flow whose
walk still leaves such a call open (one of several overrides, on its own object) is walked once per receiving class a flow
enters it through: id `<shared id>--<class>` (`shared-restcontroller-typedcreated--seriescontroller`; the class's full name,
slugged, when two receiving classes of one shared flow share a name; overloads of one method take `-2` as any id does), name `<point> on <class>` ("RestController.TypedCreated
on SeriesController"), start "shared code, walked for the flows that run it on SeriesController (D310)". Its `continues` links
point at that walk. A shared flow entered with no receiving class is the one walk it always was, and is written only when some
flow enters it that way. Layer 2's `call-site` facts carry `self: "1"` for such calls (graphs rebuilt with `--full`).

**v5 (2026-11-14, D316):** a request a mutation hook declares (React Query's `useMutation`, a wrapper sending from one such as
Sonarr's `useApiMutation`: D214's `mutation:` deferral, the step condition `case: "useMutation"`) is followed only where a mutation
was started on the way: a step above it calls a value taken from a function's result (`mutate(…)`, `deleteEpisodeFile()` from
`const { deleteEpisodeFile } = useDeleteEpisodeFile(…)`), a function a hook hands back that calls `mutate` (fresh check S7), or
D184's 🟡 "runs X's own work, through a library callback". Anywhere else, on a screen's opening or a click's render of a modal
alike, the hook only sets the request up: it is one step "declares POST {base}/api/v5/command, sent only when a click (or other
code) calls `mutate(…)`, so not followed here", ending `waits` ("declared by useMutation, sent when `mutate(…)` is called"), with
no endpoint, backend or changes under it. A mutation's own function (`mutationFn`, `onSuccess`) reached so is one step "leaves X to
the mutation it sets up: …", also `waits`; an HTTP call with a runtime URL on a line that hands a mutation hook its function is a
"declares …" step too. A point holding such a request walked where none was started is walked again (not "same as") where one is.
A shared flow entered where no mutation was started, whose walk followed a mutation's request only because its entry started one
(here or in shared code it continues into), gets a walk of its own: id `<shared id>--declared` (after a D310 `--<class>`), name
"<point>, where no mutation is started", start "…; entered where no mutation is started, so the requests its mutation hooks declare
aren't followed (D316)"; the walk a starting call enters is the one it always was. Sonarr's `/series/:titleSlug` opening: 1,231
steps → 913, requests 31 → 19 (the 12 gone are PUT and DELETE series, DELETE episodeFile, POST manualimport, POST command …).

## Starts (D171)

| Kind | Starts at | Name (D176 item 2) |
|---|---|---|
| `click` | each `ui-event` handler, one flow per handler, every button that runs it in `triggers` | the label lookup's words ("Names: the label lookup" below, D327, D333): the button's text key through the app's translation files, else its literal text; else the handler's name |
| `screen-opens` | each route (`page`): its guards, its resolvers and loaders, then the screen it shows; each MVC GET action returning a view (`on-load` `mvc`) | "Title (route)" by the label lookup's screen title (D333), else the route; the action's likewise. The id stays the route's |
| `job` | each `is-job` method (commands, scheduled tasks, recurring registrations) | the command's type, the recurring job's id, else the method |
| `handler` | each event handler (`handle` facts) that isn't a job: the flows a sent message links to (D173) | `Handler on Event` |
| `shared` | each hub and depth stop a flow continues into (D189): the point a `continues` link names, walked once; a call written to an interface walks its implementations at the root, one branch each (🟡 when several); built transitively, so a shared flow's own stops get theirs; one per receiving class when its code makes a virtual call on `this` (D310) | the point; `<point> on <class>` |

Not a flow, and said so in `index.json`'s `skipped`: a button with no handler, an inline or prop handler, a handler not on the
map, a click that reaches no server call, save, message or queued work ("screen behaviour only", D171), a screen whose opening
reaches none ("display only", D176 item 5), and on-load code no route's screen reaches.

## Names: the label lookup (D327, D333)

Layer 3 names what a user sees with one lookup (`Labels`), which the PM inventory (layer 5) uses too, so the two never disagree.

- **Translation files**, found anywhere in the repo outside build output and packages: English JSON named `en.json`, `en-US.json`
  or `en_US.json`, flat or nested (nested keys joined with dots, so Angular transloco keys carrying the template's prefix,
  `device-card.save`, are found as written), then .NET resource XML (a neutral or English `.resx`, an `.xml` named for resources such
  as `defaultResources.nopres.xml`), keys looked up ignoring case. JSON first, then path order; the first file holding a key wins;
  `|`-joined keys are alternatives, the first found wins. A value that only refers to another key (`"{{common.cancel}}"`) is that
  key's words.
- **Buttons:** text key, else the words written in the markup, else ❓ with the handler's short name.
- **Fields** (`form-field`): label keys, else label words; a Razor field bound with `asp-for` takes the label of the property it
  reaches (its `property`, the compiler's answer, joined to that property's server `form-field`: `[NopResourceDisplayName]`,
  `[Display(Name)]`); a server-defined field its `label` key. Help: help keys, help words, the property's help, else a key beside the
  label key with a usual suffix (`.Hint`, `Hint`, `HelpText`, `.HelpText`) when a file holds one.
- **Choices:** an enum's members through their `field-choice` labels (in value order); a constant list's items through the lookup
  (a placeholder such as `---` is no choice; options drawn through the field's own template are made at runtime),
  an item that reads as a key (one token joined by dots, dashes or underscores, or run-together capitalised words) and is in no file
  is ❓; choices made at runtime or from the server are said so, not listed.
- **Screens:** "Title (route)": the route's own `title` (a key, or words when it doesn't read as a key); else the screen component's
  outermost element carrying a `title` (also in a body it wraps, two levels down), then its top heading (of a heading written in
  branches, the words without a placeholder); an MVC action's view: a `ViewBag`/`ViewData` slot named for a title,
  then its top heading (`screen-title` facts, code-graph v6).
- **Nothing found:** "❓ <code name>: no visible text found" (with the key when one was given but no file holds it). A flow still takes
  the code name as its name.

`stangent flows labels [--top N]` prints the share named by visible text per kind (buttons: click and submit events; fields not inside a
wrapper's own template; server-defined fields; fields with listed choices, named when every choice is; screens), where the words came
from (`key`, `text`, `model`, `route`, `title`, `heading`), and the first ❓ lines: a floor check of extraction, never measured against a
planted key (D327 item 3).

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
10. code kept for a later call (D306, D201's `kept:`): a function the repo's own code only stores, or one assigned to a property
   (`action.callback = (a) => handler(a)`), runs when something calls it later, so it isn't walked; one step per keeper ends
   `waits` (v3). A function stored on an event property (`reader.onload = …`, `request.onsuccess = …`) is a listener, walked with
   its "when" condition, not kept. The TypeScript extractor marks the call sites: a parameter called only inside a stored function
   (Kavita's `wireBulkCallback` sets each bulk action's `callback` at selection time) counts as kept, and code inside a stored
   function is kept even when a stream or listener inside it would otherwise mark it (the outermost keeping wins).
11. a SignalR push (G10's `send` facts, and a call to the app's wrapper with its `send-arg`, D292): a step "pushes X to the
   browser" ending `push`; the wrapper call isn't walked; a wrapper called with a name made at runtime: "pushes a message named
   at runtime", ❓.
12. a file written or deleted (`file-change` facts, D294: .NET's `File`/`Directory` write, copy, move and delete methods, the same
   names in `System.IO.Abstractions`, NetVips `WriteToFile`, ImageSharp `Save…` with a path, `CopyTo(Async)` onto a
   `FileStream`): a step "writes a file through M" / "deletes a file through M" ending `file`, with its conditions like any
   step (D304; a `condition` fact is kept for .NET's own file calls); the package call on that line
   isn't a second step. An app's own wrapper is walked into, so the call at the bottom is the step.

**Calls on `this` (D310):** each point runs on an object of a known class: a flow's start, an endpoint's action or any point
entered other than through `this` runs on its own class (or one built on it); a call on `this` (layer 2's `self`) keeps the
caller's. A virtual or abstract call on `this` runs only the override that class's nearest class provides (itself, else its nearest
base) and those of classes built on it; `base.M()` stays itself (D201). A hub reached that way names the override it narrowed to
and continues into that point's shared flow, carrying the class. When nothing narrows it (the object could be any of several
classes, as in a shared flow walked for every caller), every override stays a 🟡 branch, its reason adding "it is called on
`this`, and which class the object is isn't known here".

**Thrown errors (D300):** a `throw` is checked against the `try` facts of its own method and then of each caller up the walk
(at the call's line), innermost first; the walk up stops at an HTTP hop, a filter, an on-open hook or the flow's start. A bare
catch or `catch (Exception)` takes anything; a typed catch takes that type and the types built on it (the throw's
`exceptionTypes`); a catch with a `when` filter isn't counted; a catch that throws anything (`throw;`, `throw e`, or a new
error wrapping it) lets it go on up (D304); a new error it throws is what climbs on, and the `what` adds "turned into X at
`file:line`". Taken: the error is `handled`, with `caughtBy`, and isn't an exit or a scenario; `catchReturns` is what the catch
returns (`new HealthCheck(…)`), or the `return` right after its `try` it falls out to (`View(…)`), when there is one. A catch
whose type can't be worked out, or a typed catch facing an error whose type isn't known (a bare `throw;`, D304), leaves it an
exit with `unsure` (❓ on the page).
10. a call into a package (D188); a returned ASP.NET MVC `View(…)`/`Redirect…(…)` ends `screen` (D199): a step `calls X in the package Y`, ending `unknown`, "a package's code, not walked"; not a ❓. A call into a known outside service's client library (D302: namespaces `Azure`, `Amazon`, `Stripe`, `Twilio`, `SendGrid`, `brevo_csharp`, `sib_api_v3_sdk`, `Avalara.AvaTax`, `MailKit`, `System.Net.Mail.SmtpClient`, plus `stangent.yaml`'s `flows.outsideLibraries`, each a namespace and everything under it) is a step "calls M outside the app (package)" ending `outside-call`, except a local builder (`Get…Client`, `Create…Client`, which only makes an object) and a line that already has an `outside HTTP`/`outside SMTP` step (D304). A call that runs an implementation in a plugin or
add-on project other than the caller's (D305: a project whose file sits under a folder named `Plugins`, `Plugin`, `AddOns`,
`Add-ons` or `Extensions`, any case) also gets, first under that implementation's step and at the call's own line, one 🟡
`outside-call` step per outside service the plugin's own code reaches (a listed library's package, `outside HTTP`, `outside SMTP`;
found by searching the plugin project's calls from the implementation, not walked): "may call Avalara.AvaTax.RestClient outside
the app through the plugin Nop.Plugin.Tax.Avalara (AvalaraTaxProvider): AvaTaxClient.CreateTransaction, …", ending `outside-call`
"Avalara.AvaTax.RestClient (plugin Nop.Plugin.Tax.Avalara)". The walk into the implementation itself is unchanged. A redirect
exit names the action it sends to when it names one (`RedirectToAction("List")` → "sends the user elsewhere (List)", D305). An ASP.NET Identity manager's call is "saves", "reads" or "reads or saves the Identity user store" by its method's first word (D291); a `Check…SignIn…` call is "reads or saves" (lockout writes the store, D304).

Before an action runs, its access filters (global, controller, action: the `auth` facts' `filterType`) and the controller's own
`OnActionExecuting` overrides are steps `runs first`, walked like calls. A route's guards are the first steps of its screen.

A point already on its branch is `backTo` that step (a loop, D176 item 1); one already walked elsewhere in the flow is
`sameAs` that step, not walked again. At 8 real calls a branch ends `depth`, with a `continues` link. Every `continues` link
(a hub's or a depth stop's) carries `flow`: the `shared` flow that walks on from there (D189). A reader, or the scorer, follows
it to see the shared code's steps; its actor is "the flow that continues here". A child component whose walk
reaches no HTTP call, save, message or queued work is display only: named in `displayOnly`, not shown as steps (D176 item 5).
Steps are numbered in walk order, depth first; `from` is the step they come from (null: the start).

**Conditions:** each step carries the conditions of its own call's `condition` fact, innermost first, quoted exactly (D174):
`if`, `else`, `case`, `catch`, `loop`, `null`, `unless` (a guard earlier in the block, D182). The Markdown reads each one
(`FlowFiles.ConditionText`): "only if `x`", `else` "is false", `unless` "is false (an earlier guard)", `null` "is null" (the
`x ?? throw` side, D420), and the PM view's picker and `truth-map.json` show the same reading, never the bare code. **Scenarios:** the main path, then
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
framework or declaring tests) is test code. After the fresh check (D250): a redirect written outside a controller keeps the request's
area; a redirect to a URL a method builds from `Request.Path` is the same page; `mutate` from the app's own mutation hook starts
it, certainly (and, D316, a request the hook declares is followed only below such a start); `$.ajax({ ...options })` follows the spread to its callers' url and verb; `Activator.CreateInstance(t)` cast to a
type is one of its implementations; a stream condition names the gating operators before the step (`filter(…)`).

## Known gaps

- Code inside a function handed to the repo's own code that keeps it, or assigned to a property, isn't walked where it's handed
  over: it ends `waits` (D306), and whichever later click calls it is its own flow only when the walk finds that click; a listener's
  (`connection.on`), a timer's (`setTimeout`, `setInterval`, D212) or a mutation hook's (`useMutation`, and a wrapper sending from one, D214) carries a condition (D240); a call to a handler interface's method is one 🟡 `message` step, not a walk into every handler (D201).
- Dispatch isn't narrowed by a generic controller's type arguments (Sonarr save-indexer: 32 🟡 `Test` branches); step 6 found
  the largest such line, `Settings.Validate()` (one of 76), can't be: `Settings` is typed by a plain interface (not built, D248).
- X4: a runtime value beside a fixed route of the same shape (`/Queue/{*}` against `/Queue/bulk`) is still "2 endpoints fit":
  the value's type isn't recorded by the TypeScript extractor's URL builders.
- ASP.NET Identity's managers' async calls are steps ending `database` "the Identity user store", 🟡 (D200); the verb is a guess
  from the name, so a `Generate…` call that writes is filed as a read (D291).
- A package call is a step, not walked (D188); only a known outside-service library's is an `outside-call` (D302); a library not
  on the list is missed. TypeScript's package calls aren't recorded.
- A plugin's outside services (D305) are found only when the plugin is reached through an interface or virtual call the graph
  dispatches; a plugin wired up some other way stays unseen, and a project outside a `Plugins`/`AddOns`/`Extensions` folder isn't
  taken for one. Its listed calls are those its own project's code reaches (calls into the app's core aren't searched).
- A browser timer becomes a `runs-later` link only when its code reaches a server call or a change the walk can follow (D295).
- An error thrown inside a shared flow is checked only against that shared flow's own code: a catch in the flow that continues
  into it isn't seen (D300).
- A file write through an image library not on D294's list is missed.
- Navigation (D192): a returned redirect or a navigation the function returns after is an exit; any other is a step ending
  `screen` (`router.navigate…`, `location.href`, `navigate(…)`, `history.push`), not yet linked to the screen's own flow.
  `<Link>`/`routerLink` in markup isn't read.
- (Exits now include an app's own helper that ends the request, D186, and a filter's `context.Result` refusal, D187.)
- A shared flow runs once per point, not per caller: what a caller passes in isn't known there, so its conditions aren't narrowed.
  Only the class of the object it runs on is carried, for virtual calls on `this` (D310); a virtual call on another object (a
  field, a parameter) isn't narrowed by what the caller holds.
- A point is walked once per flow (`sameAs` after that), whatever class it runs on: a flow reaching one base method through two
  different classes shows the first class's overrides under both (D310).
- Removing `areas.yaml` moves flows back to `specs/flows/`; the area pages stay until removed by hand.
- Flow files are large on big repos (Sonarr ~160 MB, nopCommerce ~196 MB with the Markdown), shared flows about a third of Sonarr's.
- Angular templates' child components have no call-site lines, so a screen's children are reached only through code.
