# The PM inventory (layer 5, contract `inventory` v2: the App frame, D362)

Every control a user can touch, named by the words they see, grouped per screen: built from the code graph by code, no AI
(D325–D329, D334). It is the list layer 5's link step reads against an FSD (D325); it is also useful alone, e.g. diffed
between releases.

## Commands

```bash
stangent inventory          # build: writes specs/inventory.json and specs/inventory.md, prints the counts
stangent inventory check    # D334 item 7; exit 1 inventory_check_failed with the problems
```

Both need the code graph (code-graph v6, D329, D333); without one they fail `no_graph`, exit 3. Output follows
`docs/cli-conventions.md`: one JSON envelope; `inventory` returns `commit`, `counts` and `files` (`written`, `unchanged`).

## Files

- `specs/inventory.json`, canonical JSON: `contract`, `version`, `commit`, `counts`, `screens[]` (each with `id`, `title`,
  `named`, its own `lines`, `dialogs`, `menus`, `pointers`), `sharedParts[]` (with `appearsOn`), `notPlaced[]`, `skipped[]`
  (each a line plus its `reason`), `setAside[]`.
- `specs/inventory.md`: generated, never read back. The counts at the top (screens, lines by kind, share named by visible
  text, ❓, skipped by reason, set aside, shared parts, Not placed, dialogs and data menus found), then one `##` per screen
  with its own lines, a pointer line per shared part it uses ("uses Series Actions (23 actions)"), and a `###` per dialog
  and menu it opens; then "Shared parts" (each with "Appears on N screens: …"), then "App frame (on every screen)", then "Not placed". Skipped lines are never in
  the `.md`. A title made from a code name is marked "_(code name)_".

A line holds: `kind` (`button`, `menu-entry`, `field`, `server-field`), `name` (the words, or "❓ code: why"), `named`, `source`
or `why`, `help`, `choices` (the names, or why they aren't listed), `at` (`file:line`), `code`, `handler` (buttons and menu
entries), `part` (the part it is written in), and `merged` (display-name labels it shows, below).

## Rules

**Lines (D326, D334 items 5–6).** From facts outside test code: each `ui-event` that is a button by `Labels.IsButton` (click
and submit events, D333 item 6); each `menu-item`; each `form-field` not inside a wrapper's own template (D332 item 1: the
wrapper's uses are the fields a user sees); each server-defined `form-field` as below. Every name comes from the label lookup
(`Labels`, D327, D333), so the inventory and layer 3's click names never disagree.

**Parts (D334 item 1).** A frontend component with its template, or an MVC view, partial or view component. A line belongs to
the part its fact sits on (a template to its component; a member of a component to that component; anything else is a part of
its own). Parts with no lines are walked through, never listed.

**Reach (D334 item 1).** A screen (`LabelReport.Screens`: pages, and MVC GET actions returning a view) starts at its routed
components (or its action's views) and reaches parts through `renders` (a view component stands for the views its methods
render) and through **dialogs opened from code**: from a part's own members, a walk along frontend `calls` lines of at most 4
hops; a method on it holding a `uses` line to a component the part doesn't render opens that component as a dialog. A dialog's
own `renders` belong to the dialog. Server-rendered views have no walk.

**Data menus (D334 item 2).** `menu-item` facts with `via: data`, one menu per `menu` key, belong to the member holding the list:
the member the list is assigned to when its code says so and the graph has it (`this.seriesActions = [`, `const GROUPS = [`), else
the member holding the facts. A method holder is its own reader; any other is read by whatever holds a `uses` line to it, its
builder aside. A screen reaches the menu when the walk from one of its parts reaches a reader. Titled by the holder's name said
readable, a leading `get`/`build`/`create` taken off (`getSeriesActions` and `seriesActions` both give "Series Actions"),
marked code name. Menu entries written in markup are lines of their part.

**Folding and placement (D328, D334 item 3).** A part rendered by exactly one other part folds into it, recursively; dialogs,
data menus and a screen's routed component never fold. A group reached from one screen is listed under it (its own lines, or
inside the dialog it sits in); from two or more under "Shared parts" with "appears on" and a pointer line on each screen; from
none under "Not placed". A part's title: its own top heading or titled element (`screen-title` facts), else its class name
(a trailing `Component` off) or view name (a view component's `Default` view by its folder) said readable, marked code name.

**Server-defined fields (D334 item 4).** One with a display-name attribute (`DisplayName`, `Display`, any `…DisplayName`) shows
only through a view field bound to its property (`property`, D333 item 3): merged into that line (`merged`), never listed twice;
one no field binds is set aside, reason `unbound-model-label`. Any other (a `Label =` argument, Sonarr's `FieldDefinition`) is
drawn by a generic form from the server's list: listed per declaring class under "Not placed" ("Newznab Settings, 12 fields").

**The skip list (D326, D334 item 5, narrowed by D335)**, fixed for every repo. Buttons and menu entries whose visible words
(trimmed, ignoring case and a trailing `…`/`:`; a button with no words by what its markup writes, such as `×`) are close,
cancel, back, go back, dismiss, × (`close`); expand, collapse, expand all, collapse all, show/read/see more/less (`expand`);
next/previous/first/last page, select/deselect/unselect all (`table`); or whose handler's
short name, a leading `on`/`handle` and a trailing `Click`/`Press`/`Clicked`/`Modal`/`Dialog` taken off, is close, cancel,
dismiss, back, goBack (`close`), expand, collapse, toggleExpand, toggleCollapse, showMore, showLess (`expand`), sort, sortBy,
changeSort, updateSort, pageChange, changePage, nextPage, previousPage, prevPage, firstPage, lastPage, setPageSize,
pageSizeChange, selectAll, toggleAll, deselectAll (`table`); a close-type handler name skips only a line showing no words of its
own (D335: a "Download" button with a `close` handler stays). Fields labelled page size, items per page or per page when bound
to no server model property (one that saves to a model is a setting, D335), labelled select all, or bound to `selectAll` (`table`). Server-defined fields aren't on it. Filters, search and view modes stay in.

**`inventory check` (D334 item 7).** The files byte for byte what the graph builds now; every ❓ says why; every skip reason on
the fixed list; every set-aside reason known; every control fact outside tests (`ui-event` button, `form-field` other than a
wrapper's insides, `menu-item`) exactly one line, skipped line, set-aside entry or merged label.

**App frame (on every screen)** (D362 (3), v2): what the app's root draws or opens, on every screen, so it isn't "Not
placed". The root is a part no other part renders, no screen starts at and no code opens as a dialog, that is mounted:
used from the top level of a file that calls the framework's mount function (`bootstrapApplication`, `bootstrapModule`,
`createRoot`, `hydrateRoot`, `ReactDOM.render`), named in an NgModule's `bootstrap: [..]`, or a view `_Layout.cshtml`. A
part in the root's own file that nothing renders is the frame too (a router's layout element: Sonarr's
`<Route element={<PageLayout />}>`). Everything those reach (renders, dialogs, data menus), not placed so far, moves from
"Not placed" into the frame; JSON `appFrame` (`title`, `roots`, `groups`), counts `appFrame`. On the clones (2026-11-16):
Kavita 4 groups, 59 lines (`AppComponent`); Sonarr 2 groups, 53 lines (`App`, its `PageLayout` and the sidebar menu);
nopCommerce no root.

## Known gaps
- A server app with several layouts (nopCommerce: public `_Root`, `_AdminLayout`, `_AdminPopupLayout`, set by
  `Layout = "…"`) has no single frame "on every screen": no root, so its shells stay "Not placed". A frame per layout is
  a later step.
- A data menu read only from a template's property (Kavita's `navService.navItems` in the nav header) has no reader the
  walk sees: "Not placed" even when its readers are in the App frame.

- The 4-hop walk misses a dialog opened through an event bus, a router outlet or a callback held in data (it lands in "Not
  placed"); a service called from many places places its dialogs on many screens (Shared parts).
- Razor `EditorFor` templates and partials rendered through code paths the graph doesn't follow are "Not placed".
- Data menus built in C# (an admin menu, install code) have no walk: always "Not placed".
- A menu entry whose name is a key the app prefixes at runtime is shown as written when it reads as words (`edit`), or ❓.
- The skip list is English only; a wrongly skipped control is counted in `skipped` for anyone to read; it is never tuned per app.
- Titles keep a template's placeholders (`Edit {{name}}`).
- A shared dispatcher service that opens dialogs for several record types (Kavita's `ActionService.handleXAction`) places every one
  of them on every screen that uses it (D336).
- Dialogs opened from script (`displayPopupContentFromUrl(Url.RouteUrl(…))`) and partials rendered by name in controller code
  (`RenderPartialViewToStringAsync("Name", …)`) aren't followed: "Not placed" (D336).
- A route written as an inline array (`loadChildren: () => [{ path: '', component: X }]`) isn't a screen, so X is "Not placed".
- Titles can come from a not-found branch or a section heading (step 2's title rule).
- Server-defined fields marked hidden (`Hidden = Hidden`, `Hidden = X.Hidden`, `Hidden = true`) are set aside, reason `hidden-field` (D336).
