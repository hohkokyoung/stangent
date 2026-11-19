# Pages (contract `pages` v2, rebuild layer 4)

Each area's `specs/areas/<area>/page.md` (D230, D233) holds a **developer page made by code, no AI** (D278–D285): what each
click does, at a glance, with a way into its flow file. It answers "what does this click touch and where can it stop?" faster
than reading the flow file's step list. The PM's product is layer 5's FSD comparison, not this page. `pages summary`, the
item cutter layer 5's judge reads, stays (below). Decisions: D270–D289; plan and numbers: `phases/r4-pages.md`.

## Commands

| Command | What it does |
|---|---|
| `stangent areas check` | Writes every area's `page.md` (D230). The developer page goes between D233's two layer 4 lines and the page opens with its note (always, since D381). |
| `stangent pages chart <area> [--proposed] [--format md\|counts\|json]` | The area's developer page without writing it: `md` (default) is the note and body as `page.md` holds them; `counts` a table of nodes, boxes, diamonds, scenario rows, shared guards and changes per changing click, then totals; `json` everything. Works with the switch off. |
| `stangent pages verify [<area>]` | Gate 1 of D280 (below), every area or one. Exit 1 (`pages_verify_failed`) with the report when anything is missing, a rebuild differs or a page on disk isn't current. |
| `stangent pages summary <area> [--proposed] [--format json\|md\|counts]` | The area's item summary by code (below). Writes nothing. |

`<area>` is the name in `areas.yaml` ("Series › Add New") or its folder (`series-add-new`).

## Switch and settings

- **On** (D381; was off by default under D285 until a person read the pages). No AI, no cost.
- **`pages.frameworkShare`** (percent, default 10, 1–100, D281): a change (save, delete, message, queued job, outside call)
  that **more than** this share of the app's start flows pass through, **and at least 10 flows** (fixed), is a framework
  change: listed under "Changes made in shared code" with `file:line` and its flow count, not drawn, and it doesn't make a
  click "changing". Measured: nopCommerce's customer and log saves 85% of 948 flows; business saves 7% or less.
- **`pages.chartCap`** (nodes, default 60, at least 1, D282, D286): a chart with more nodes isn't drawn; the page says how many it
  would have had. The table and lists are written as usual, so nothing is lost.
- Shared guards (stops) use the flows' hub threshold (`hubCallers` in the flow index, 10 by default): a stop at a line that
  many start flows or more have is listed under "Guards shared with other flows".

```yaml
components: { pages: true }
pages: { frameworkShare: 10, chartCap: 60 }
```

## The page

**The note** (D283), a YAML block between `---` lines that opens the file: `area`, `madeBy: code, no AI`, `commit` (the
flow index's), `settings` (`hubCallers`, `callDepth`, `frameworkSharePercent`, `chartCap`), `clicks` (`charted`, `oneLine`,
`tooLargeToDraw`; every start flow counts, screens, jobs and handlers too) and `steps` (`certain`, `guessed` 🟡,
`cantTell` ❓: a step a ❓ sits on, a step guessed, the rest). Counts, never a percentage; no date anywhere, so a rebuild from
the same flows, `areas.yaml` and settings is byte for byte the same.

**The top of the page** (D313). Under the area's name and counts: the developer page's marker line (no "layer 4 words go
below" line: nothing of it is kept, and readers took it for a missing section; the end line stays, as the end of the last
part), one legend line (what 🟡 guessed, ❓ can't tell and "step N", the Nth step in that flow's own file `<flow id>.md`, mean),
then (D314) a part of a split area (`<Area> › …`, its parent the longest other area name it starts with, as D227 finds it)
says `**Part of [Series](../series/page.md).** Its other parts: [Series › Add New](../series-add-new/page.md), …`, and a split
area's page has **Parts of this area**, one line per part, linked, with its start flows by readable name and kind (`- [Series ›
Details](../series-details/page.md): 7 start flows: screen `/series/:titleSlug`; clicks "handleDeleteEpisodeFile",
"handleMonitorEpisodePress" ×2, …`; handlers and jobs by name without namespaces, `SeriesAddedHandler.Handle on SeriesAddedEvent`,
`MoveSeriesCommand`; every flow named, none cut); links are relative to the page's folder; then (D316) **Elsewhere**, below;
then **Clicks that change something**, the index: one line per changing start flow (title, its id linked to `<flow id>.md`,
chart or too large to draw, nodes, scenarios), with up to two sub-bullets from its flow file, by code:

- `start here:` the entry and the requests. A start in a C# method (a handler's `Handle`, a job's `Execute`, a server-made
  screen's action) shows the method and the line its flow file starts at (`SeriesAddedHandler.Handle` at `…/SeriesAddedHandler.cs:20`;
  for a job that line is the class or its registration, as the flow file has it). Any other start shows its frontend line
  (the click's `onPress`/`(click)` line, a screen's route line, a Razor button). Its requests are its own `http` steps: a
  matched one is the endpoint's method and path (🟡 when the match was guessed) and the action it lands on, the next step's
  line: `` `frontend/…/AddNewSeriesModalContent.tsx:367` → `POST /api/v5/Series` → `src/Sonarr.Api.V5/Series/SeriesController.cs:195` ``;
  one to this app the graph couldn't match is ❓ with what it asked for (`❓ `POST /api/{*}``); one to another host
  (`api.github.com`) is an outside call, not listed; a click or screen with none is `❓ request`. Several requests go one per
  sub-bullet, three at most, then `+N more`. A server-made screen (nopCommerce) opens at the route its flow's name gives
  (`ANY /Admin/Product/List/{id?}` → `ProductController.List` at `…:948`), `❓ request` when the name is the action's. A
  handler or job with no request names none.
- `changes:` the chart's change boxes, each once, in chart order: saves, files, pushes and queued work (first three, then
  `+N more`; "which runs later…" left off), `calls outside:` the services, `sends` the events. D314: a change or event whose box
  sits under a deciding condition says so, with the nearest condition (the innermost on its path) said for when it holds, D311's
  way (a known shape in words, else its code; an else's or guard's as "`x` is false"), and its line, `…` when there are more:
  `queues MoveSeriesCommand (only when `queueMove`, `SeriesService.cs:336`)`, `saves Series (only when no monitoringOptions,
  `EpisodeMonitoredService.cs:30`, …)`. The same change also made unconditionally somewhere stays plain; outside services stay plain.

Start flows are in kind order (screens, clicks, handlers, jobs, the rest) and within a kind small to large by chart nodes,
too large to draw last, ties in the old order; the body's parts and "What code couldn't see" follow the same order. Each part
is the same bytes as before, only moved.

**A changing click** (D271): it saves (or may save: a step that "reads or saves" shows as "may save 🟡", D291), deletes, sends a
message, pushes to the browser (D292), writes or deletes a file (D294), queues work (a browser timer whose code calls the server
or changes something too, D295), calls an outside service, or has a ❓ step, leaving out framework changes. Its part, in this order, nothing folded:

1. One line: its flow id, kind and step counts (`137 steps: 🟡 2 guessed, ❓ 2 can't tell`).
2. **The flowchart** (D273), Mermaid: the click as the start; a box per change, the same change (same words, same line) in
   several places drawn once with all its steps, the same words on different lines separate boxes (D288); a diamond per
   condition that decides one, with everything under that condition nested under its one diamond, so a loop is drawn once
   with its body inside it (D288); conditions stack outer first, as the code nests them; the field checks as one "All checks pass?" diamond; a
   diamond per stop's own condition into one "Refused" end (a redirect gets its own end); an outside service's failure as a
   dashed line from its call; what runs afterwards as one box per change. Labels are code's own, in full, escaped for
   Mermaid. Not drawn: reads, shared guards, framework changes, setup steps (a save whose only condition is that nothing is
   saved yet). Above the cap: one line with the node count instead, then **Changes**, one line per box it would have drawn.
   A call the flow file marks "as in step N" (`sameAs`) carries step N's changes under its own conditions (D296): the same
   conditions add its step to the first box; others are a box of its own, `saves ReadingList (step 32, as in step 31)`. Boxes
   that would read the same on one chart each add their code line, `(steps 98, 105, `GenericAttributeService.cs:140`)` (D297).
   A diamond reads positively, never "not: x" (D310): an else or a guard (`unless`) on a condition that isn't a plain `!x` shows
   the condition itself with its lines swapped, so the guard `if (string.IsNullOrEmpty(dto.Title)) throw …` puts the save under
   "string.IsNullOrEmpty(dto.Title)? no" and "yes" goes past it; a plain `!x` reads `x?` as before; a stop's diamond with several
   conditions says such a one as "(x) is false" ("string.IsNullOrEmpty(dto.Title) is false and readingList.Tags == null?").
3. **Scenarios**, one row per path: `main` (the steps that *can* run on it: some run only on one branch, D288); `D` each
   deciding condition's other side (what it skips), its id the condition's diamond on the chart (`D8` in the table is `D8` in
   the chart, so the ids have gaps where a diamond is a stop's); `K` each field's checks (the only place they are listed; D314: a flow whose whole K set, two rows or more, field, rules and
   line, is the same as an earlier flow's on the page, in page order, the clicks then the shared parts, has one row instead,
   `| K1–K8 | same checks as [<that flow's title>](#<its heading's anchor>) (K1–K8), `<its id>` | 8 fields, as written there |
   checked at step 131 | Refused |`, keeping where it checks them; a different set, or a single row, is written out);
   `S` each outside service failing; `X` each other stop or redirect; `U` each setup step. Columns: what decides it (code
   and `file:line`, outer conditions first), the steps it passes or skips, how it ends. In a flow whose main path has more
   than 20 change steps (D308), a row names only the shorter list, the steps it passes or the ones it skips ("passes steps 2,
   917–921; skips the main path's other 80 steps"), with runs of three or more as ranges; in any flow, a row skipping more than
   5 changes counts them instead of naming each ("skips 12 changes, at the steps it skips, which run only when …"). What an `X` row passes is worked out
   from where the exit sits in its method's code (D305): the steps of that method written above it (with everything under
   them), under the exit's own conditions as well as the method's, not from step numbers (an exit's step is its method's own,
   which comes before all of the method's steps). A redirect that passes changes is how the work ends, not a refusal: "Ends
   after the work: sends the user to List" … "ends by sending the user to List, after the saves (steps 79, 86, …); stops at step
   2 (`ProductController.cs:1420`)"; one that passes none stays "Sent elsewhere". The exit's box is drawn after those steps too.
   A child step in another file (a filter, an implementation run) is placed by the next child in the exit's own file. A `D` row states its condition
   positively and names what it skips (D297): "When `X` is false" … "skips saves GenericAttribute (steps 98, 105), which run
   only when it is true"; a negated simple condition reads plainly (`!hasLicense` is false → "`hasLicense` is true"). No `D`
   row for a condition whose other side is only a stop whose own `X` row already carries the same condition (its code at the
   same line) (D310): an else's or guard's other side is the `if` before it, an if's its else (counted only when a stop carries
   that else), and a change on that other side keeps the row. Kavita's reading-list save loses "When
   `string.IsNullOrEmpty(dto.Title)` is true … skips the save", since the stop row "throws ArgumentException" lists it; the
   diamond stays.
4. **Outside services failing**: per service, each failure's condition and stop.
5. **What runs afterwards on its own**: per change, the handlers and jobs it sets off, each named by class and method (its start
   point's, else its name) with its id a link to its file (D314): `after sends SeriesDeletedEvent (step 19): `SeriesController.Handle`
   ([`handler-seriescontroller-handle-6`](../settings/handler-seriescontroller-handle-6.md)), …`; over 12, the line ends `, 18 flows:`
   and each is a sub-bullet under it; none is ever cut; a message sent in shared code (a framework
   change, not drawn) or with no change step still lists its receivers (D293); a timer's code too (D295); and each message sent
   in a shared flow it continues into, "after sends X (in shared code Y) (step N): …", at the step that continues there (D304).
5a. **Errors handled on the way (not stops)** (D300): per error and the catch that takes it, `handled: IndexerException at
   `BroadcastheNetParser.cs:26` (step 412), …, caught by `HttpIndexerBase.cs:252``, saying only what is known (D304): when the
   catch itself ends the request, ", which then answers BadRequest (`…:293`)" (that stop is its own row); when the catch, or the
   code right after its `try`, returns, ", which then returns View(…)"; never "carries on". A stop whose catch can't be told
   carries ❓.
6. **Guards shared with other flows**: each stop with its flow count (`in 13 of 392 flows`).
7. **Changes made in shared code**: each framework change, its steps, `file:line` and flow count. The same change at the same
   line, made directly and again through shared code ("saves NamingConfig (step 70)", "saves NamingConfig (in shared code
   RestController.TypedCreated) (step 137)"), is one line with all its steps (D306). Since `flows` v4 (D310) a shared flow
   holding a virtual call on `this` is walked per receiving class (`RestController.TypedCreated on SeriesController`), so a
   click no longer shows another controller's override's saves (Series › Add New's "saves NamingConfig" through
   `NamingConfigController` is gone; its real one, `GetSeriesFolder` → `GetConfig` saving a default, stays a setup row).

**Titles** (D306): start flows of one area whose titles read the same each add what tells them apart, round by round until none
do: their handler or component method (`runs when SeriesAddedEvent is sent (SceneMappingService.Handle)`), then their start's
code line (`` `EpisodeSearchService.cs:17` ``), then a C# method's parameter types (`(EpisodeSearchCommand)`), then the flow id.
The chart's start box reads the same as the heading. One-line flows are told apart the same way.

**Shared work, shown once** (D306, D307): a shared flow the area's start flows continue into (directly or through other shared
flows) that is longer than 30 steps and that 3 or more of them reach, or that is longer than 150 steps (D309; a helper of 30
steps or fewer stays inline, under each click's own conditions), gets a part of its own when it brings a flow something to
show (a save, push, file, plugin call in shared code, or a message sent there; a shared flow that only stops moves nothing, since
no flow's part showed shared code's stops). Every flow continuing into it, directly or through lighter shared code, shows one
line instead of its contents: `runs RefreshSeriesService.Execute (see its part): 12 changes, 3 stops (step 40)`, naming the
changes when there are 3 or fewer (`1 change (saves AppUser), 0 stops`); its changes are every change it leads to (its own and in
shared code it runs, each line once), its stops its own. The line is a box on the chart (one per part, with every step that
runs it) and is in the scenario rows like any change; when every change in the part is a framework change (D281), it is listed
under "Changes made in shared code" instead, as those changes were. Its messages' receivers are listed in the part, not under
each flow. The parts follow the flows' parts under `## Shared work, shown once` (a contents line each: why it has a part, chart
size or one line), each opening with `<!-- shared <id> -->` (flows keep `<!-- flow <id> -->`) and `## Shared work: <name>`, then
`` `<id>` · shared · run by N of this area's start flows, S steps: N changes, M stops ``, then the same chart, scenarios and
lists as a click's, under the same cap, or one line with its stops when nothing in it is drawn. Parts are in name order; the
same name twice adds its code line. Nothing is dropped: it moves to one place (changes D279's "nothing folded"). A part read on
its own doesn't say which flow's conditions led there; the flow's line into it does.
**Less noise, by code (D311).** (1) A list line repeated word for word inside one part (a too-large chart's **Changes**, a row's
skipped changes): the same change at the same line, or the same shared part, run at several steps under different conditions
(each its own box on a chart, "as in step 712") is one line with every step: `runs LocalEpisodeCustomFormatCalculationService.ParseEpisodeCustomFormats
(see its part): … (steps 712, 720, 732, …, 1030)`; the same words on another line stay apart. (2) The page's closing **Shared
flows** list leaves out shared flows that change nothing, stop nothing and call nothing outside, nor does anything they continue
into (only plain calls, package code and filters: no database step, read or save, no server call, file, message, push, queue or
plugin call, no stop, no ❓), counted in one line, `Left out: 38 that change nothing, stop nothing and call nothing outside (for
example `SqlBuilder.AddClause`, …)`, the heading saying `(21 of 59)`; developer page only. (3) Stops of one exception
kind thrown in one class (a `throw` stop, not an answer or redirect) are one `X` row, and under a one-line flow one sub-bullet,
listing every stop and, compactly, the conditions all share once and each place's others: "Stops: throws
ArgumentOutOfRangeException (7 places in ReadingListFilter)" … "when `comparison` matches no case, at `ReadingListFilter.cs:28`
(when not: …); `:53` (…); `:71`, `:110` (when condition)". The chart keeps each stop's diamond. (4) Common condition shapes are
said in words by code, a short fixed list, both sides: `string.IsNullOrEmpty(x.Title)`/`IsNullOrWhiteSpace` → "Title is empty" /
"Title is not empty"; `x == null` (`is null`, `=== undefined`) → "no x" / "has x"; `x != null` → "has x"; `!xs.Any()`,
`xs.Count == 0` → "xs is empty"; `xs.Any()`, `xs.Count > 0` → "xs has items"; a flag member `x.IsLocked` (Is/Has/Can/Should/
Allow/Enable…) → "IsLocked" / "not IsLocked"; a name is its last member; any other shape (`a == b`, `files.Count > 100`, a call)
stays raw. Diamonds show the phrase (D310's flips and yes/no lines unchanged: the guard reads "Title is empty? no" → the save); a
`D` row's scenario and "which run only when …" use it, its "What decides it" stays raw code and line; an `X` row's says
"Title is empty: `string.IsNullOrEmpty(dto.Title)` (`file:line`)"; stop, guard and service lines "when no body (`body == null`)".

Empty lists are left out. **Other start flows are one line each**: why there's no chart (only reads or shows; its changes are
drawn in its clicks' charts; only changes made in shared code; only setup steps), anything it changes, its step counts.
Under it (D287), one sub-bullet per stop of its own, `stops at step 14 (`ProductController.cs:880`): RedirectToAction("List")
when product == null` (the same text and condition share one, listing every stop), then its handled errors (`handled: …`) and
receivers (`runs afterwards on its own: …`), then `+ N shared guards` for its stops at
a line `hubCallers` or more start flows have, counted only. Then **What code couldn't see**: every ❓ of the area's start flows with its step and line.

**Step numbers** (D284): every box but the start, "Refused" and "All checks pass?" ends with its flow-file step numbers,
`saves Series (step 133)`, `SkyHookProxy calls outside the app: … ❓ (step 37)`; a diamond carries the step that writes its
condition. A row names the steps it passes (`passes steps 37, 58, 133`) and every stop says `stops at step 6
(`src/…/RestController.cs:131`)`; a stop the flow file gives no step takes the first step on its line (nopCommerce's
billing redirect at `CheckoutController.cs:633` is step 107, D288). A save in shared code carries the step that continues
into it. A field check run mid-flow says `checked at step N`, the first step calling a `Validate…` method of the type its
file is named after (or `I` + that name; Sonarr's `AddSeriesValidator` checks run at step 131); a check bound to the request
says `checked before the steps (no step number)`. Step numbers hold for the build the note's commit names.

**Elsewhere** (D316), by code from the flow files and the graph's endpoints. The controllers the area's start flows land on
themselves (an endpoint one of their own `http` steps is matched to, or the MVC action a server-made screen starts in; a
controller reached only through shared code, such as a lookup helper, isn't the area's), in file order; under each, its other
endpoints that no start flow of this area calls, directly or through the shared flows it continues into, each with the start
flows of the app that do (their own requests, the shared flows they reach, or the action they start in), by area and readable
name (a click's label quoted, a screen's address as code), linked to their flow files, three at most, then `+N more`.
Endpoints called by the very same start flows share one line; the endpoints no start flow calls are one line, `no start flow
calls: …`, each with its line. Each list stops at 15 endpoints and says `+N more` (the called ones by route, the uncalled
counted), never dropping one silently. Kavita Reading Lists, under `ReadingListController`: `` `DELETE /api/ReadingList`
(`ReadingListController.cs:226`): called by Customize: ["deleteList"](../customize/…) `` and `` no start flow calls: `POST
/api/ReadingList/create` (`:248`), … `` (create and add-to-list run from `ActionService`'s menu callbacks, which the walk leaves
for later, D306). Gate 1 reads nothing in it.

## Gate 1 (D280), `pages verify`

Free, by code. For every changing click, each change of its flow file (change steps, steps a ❓ sits on, unknowns with no
step, queued work, and the saves in shared code it continues into, D289; D305: and the plugin outside calls there, "may call …
through the plugin …", which carry the kind word "outside") is on the page by step number, or with no step by
its line: in a change box of its chart (above the cap, its **Changes** list), its "Changes made in shared code" list or a
setup row. D304: the step must be named on one line (a box, a list item, a row) that also carries the change's kind word
(`save`, `outside`, `sends`, `queues`, `pushes`, `file`), and "as in step N" doesn't name step N. A step named anywhere else ("skips step 133" in a row) doesn't count; a change with neither a step nor a line
is reported, as is a start flow with no readable file (`what`: `flow file`) (D289). `changes` names each change once per
area: a flow's own by flow, step and line, a save in shared code by its line however many flows reach it; each is still checked in every flow. Each stop (exit) is on the page as
`stops at step N (`file:line`)`, with the step worked out as the page does. A one-line
flow's changes must be in its line, its own stops in the sub-bullets under it (D287); its shared guards are counted
(`oneLineSharedGuards`), not checked. Since flows v2: each change a repeated call carries (D296) by the repeat's step; each
handled error on a line under its part's handled list (or a sub-bullet) naming its line and the catch (D300); and every
receiver the flow file names (its `message` and `runs-later` links, and D304 the `message` links of the shared flows it
continues into) under "What runs afterwards on its own" (or a sub-bullet), on a line naming its own send's step (D293, D304);
D314: a sub-bullet's step may be on the line it sits under (the nearest line above it indented less), and a one-line flow's block
takes sub-bullets at any depth.
D306: a flow's line into a shared part stands for that part's changes and messages: it must be on a line naming its step and
"see its part", and the page must hold the part's `<!-- shared <id> -->` (`what`: `shared part`); each shared part is then checked
like a click, its own changes, stops, handled errors and receivers within its part (from its marker to the next marker or
heading). A
screen's changes, stops, handled errors and messages drawn under its clicks count as theirs. Then the page is built twice from fresh reads and the two
compared byte for byte, and compared with the file on disk (`onDisk`: same, stale or missing). The bar is 100% on all 7
pilot areas.

D311: every shared flow the area's start flows reach is a link under "Shared flows" or, quiet by its flow file (checked again),
counted in the `Left out: N that …` line, N matching (`what`: `shared flow`).

Report: `ok`, `changes`, `stops`, `handled`, `receivers`, `missing`, `oneLineSharedGuards`, and per area `ok`, `sameBytes`, `onDisk`, `flows`, `charted`,
`tooLarge`, `oneLine`, `sharedParts`, `changes`, `stops`, `handled`, `receivers`, `oneLineSharedGuards`, `sharedFlows`, `sharedLeftOut`, `missing` (each `flow`, `what` change/stop/handled/receiver/line/flow file, `step`, `at`, `text`).

## On a clone

```bash
# stangent.yaml: components: { pages: true }
stangent areas check                                  # writes every specs/areas/<area>/page.md
stangent pages verify                                 # gate 1 on every area
stangent pages chart "Series › Add New" --format counts   # nodes per changing click
```

## The summary (D251–D253, D260, D261)

**Sections, in page order** (D253): "In short" (empty since D276; the id stays); one per screen (its opening flow and the clicks D219 places on it; a click on
several screens sits with the first), headed by the screen's address; "Clicks on no screen of this area"; under "On its own",
one per message the area's handlers answer ("When SeriesAddedEvent is sent") and one for its jobs; then "Can't tell from the
code", code's list of every ❓ (no AI). Section ids are stable: the screen's flow id, `clicks`, `on-<message>`, `jobs`, `in-short`.
A section whose item lines pass 40,000 characters (`PageSummary.SectionLimit`, about 10K tokens) splits by flow, in order,
into parts as full as the limit allows (one flow over it stays alone): `<id>-part-<n>`, "<title> (n of k)" (D264). Each part
has its own items, so items its flows share are repeated in each.

**Items, per start flow** (D251, D260), each with an id, its kind, a fixed sentence, `file:line` and the code there, its label
and **every condition on the way to it** (its parents' too, outermost first, read as the flow's Markdown reads them):

| Id | Kind | From |
|---|---|---|
| `W` | who | the flow's actor: "anyone signed in", "anyone, signed in or not", "the app itself, on its own", or the access rule as written (the writer words it); one item per wording; in "In short" when the whole area shares it |
| `T` | start | "starts with the click "Add {title}"", "opens the screen /add/new", "runs as a job (…)", "runs when X is sent" |
| `A` | save, read, message, outside-call, queue, screen, later | database, message, outside-call and queue steps; screen and runs-later links; a message names its handlers. Saves inside shared code the flow continues into are kept, once per save, with only the conditions on every path to it; reads there aren't followed |
| `S` | shows | a translation call with a quoted key (`translate`, `t`, `T`, `instant`, `GetResource`, `GetResourceAsync`, `Localize`), turned into English through the translation files D228 reads (D252); never a component's name |
| `U` | unknown | a ❓ with no item of its own (one on a step marks that step's item instead) |
| `X` | exit | early exits: "stops: throws …", with only the exit's own conditions; one item per what it throws and its own conditions' code, the other places counted (`alsoAt`, D264) |
| `K` | check | checks grouped by the field they check: "checks SeriesResource.TvdbId: GreaterThan(0), SetValidator(…)" |

Calls that end nowhere (package code, hubs, caches) give no item. An item's label is its own step's: 🟡 when that step is
guessed (or a message's handlers were found by type), ❓ when a ❓ sits on it. **Must cite:** who, saves, messages, outside
calls, exits, check groups, and any 🟡/❓. Identical items in a section are one item listing their flows; a screen's copy of a
click's item that holds all the click's conditions is one item too (D260). Reads and shown texts are one item per table or
wording in a section, keeping only the conditions every place shares, with the other places counted (D265).

**JSON** (`pages summary`, contract `pages` v2; its shape is v1's): `contract`, `version`, `commit` (the flow index's), `area`, `folder`,
`sections` (each `id`, `title`, `kind` (in-short, screen, clicks, message, jobs), `group` ("" or "On its own"), `flows`, `items`
(each `id`, `kind`, `text`, `flows`, `at`, `code`, `label` (certain, guessed, unknown), `reason` for 🟡/❓, `conditions` (each
`at`, `code`, `kind`, `case`, `text`), `mustCite`, `alsoAt` when merged)), `cantTell` (section and item of every ❓), `missing` (start flows with no
file, listed, D111), `counts`. Not committed: code rebuilds it (D259).

## The old reference pages (D258)

`specs/reference/` (Phase 2a) retires one reader at a time. When layer 4 passes its bar, the old page writer stops being run
and people read `specs/areas/<area>/page.md`; the old files stay frozen for old linking (until layer 5) and tickets (until
layer 6), and go in layer 7's cuts on the owner's OK.

## Known limits

- A one-line flow's shared guards are only counted, not listed (D287).
- A screen's changes and stops are excused by place: when a click on that screen has a change or stop at the same
  `file:line`, the screen's own copy isn't drawn or checked on the screen's part, and its step numbers (which differ from
  the click's) show only under "What code couldn't see", if at all (R2 of the D288 check).
- A stop inside a `catch` carries the step of the call that leads to it, as the flow file says, not of the outside call it
  catches (Sonarr's `SkyHookProxy.cs:160` catch says step 30, its call is step 86); "passes" for it is worked out from
  that step (R9 of the D288 check).
- "Passes" in a row is worked out from flow order and conditions: changes earlier than the stop and on no condition the stop
  isn't under. A loop or a catch can make it say a step was passed that wasn't, on some runs.
- The framework share and the cap are fitted to three repos (D281, D282) and could be wrong on a fourth, hence settings.
- A condition true on only some paths to a save in shared code isn't carried (D260).
