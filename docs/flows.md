# Flows (contract `flows` v1, rebuild layer 3)

A **flow** is one thing a person or the system does, laid out by code from the code graph: who starts it, what they
enter, what's checked, the steps with the conditions around them, where each branch ends, and the flows it leads to.
No AI anywhere (D155, D176). Decisions: D170–D180, D183, D184, D186–D189; plan and scores: `phases/r3-flows.md`.

## Commands

| Command | What it does |
|---|---|
| `stangent flows build` | Builds every flow from `.stangent/graph.sqlite` and writes `specs/flows/` (below). Prints counts by kind, guesses, ❓ and files written, removed or unchanged. |
| `stangent flows show <id> [--format md]` | One flow as written by the last build: its JSON, or its Markdown. |
| `stangent flows check` | D177's bar items 3–5 by code: every 🟡 has a reason and every ❓ says why; the files are byte for byte what the graph builds now; every start is a flow or a stated reason. Exit 1 with the problems when one fails. |

Output follows `docs/cli-conventions.md`. A missing graph is `no_graph` (exit 3): run `stangent graph build` first.

## Files (D180)

- `specs/flows/<id>.json`: one flow, contract `flows` v1. Committed; the same graph and settings write the same bytes (D176).
- `specs/flows/<id>.md`: the same flow as fixed sentences filled in by code (D173), with a generated-file marker. Never read back.
- `specs/flows/index.json`: every flow (`id`, `name`, `kind`, `home`, `start`, `point`, counts, the flows it links to) and
  `skipped`: every start code found that isn't a flow, with its reason (D177 item 5).

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
read off a repository method's name), a `message` link (handlers found by the event's type), a `runs-later` link (a command
matched to its job by type), the actor. `unknowns` lists every ❓ with why. `flows check` fails on any with no reason.

## Known gaps

- Code inside a function handed to the repo's own code that keeps it isn't walked where it's handed over; a listener's
  (`connection.on`), a timer's (`setTimeout`, `setInterval`, D212) or a mutation hook's (`useMutation`, and a wrapper sending from one, D214) is 🟡; a call to a handler interface's method is one 🟡 `message` step, not a walk into every handler (D201).
- Dispatch isn't narrowed by a generic controller's type arguments (Sonarr save-indexer: 32 🟡 `Test` branches).
- ASP.NET Identity's managers' async calls are steps ending `database` "the Identity user store", 🟡 (D200).
- A package call is a step, not walked (D188), and never an `outside-call` (only HTTP clients are, G4); TypeScript's package calls
  aren't recorded.
- Navigation (D192): a returned redirect or a navigation the function returns after is an exit; any other is a step ending
  `screen` (`router.navigate…`, `location.href`, `navigate(…)`, `history.push`), not yet linked to the screen's own flow.
  `<Link>`/`routerLink` in markup isn't read.
- (Exits now include an app's own helper that ends the request, D186, and a filter's `context.Result` refusal, D187.)
- A shared flow runs once per point, not per caller: what a caller passes in isn't known there, so its conditions aren't narrowed.
- Flow files are large on big repos (Sonarr ~160 MB, nopCommerce ~196 MB with the Markdown), shared flows about a third of Sonarr's.
- Angular templates' child components have no call-site lines, so a screen's children are reached only through code.
