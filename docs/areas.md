# Areas (contract `areas` v1, rebuild layer 3)

An **area** is a part of the app as its users would name it (Sonarr's Series, Activity, Settings › Indexers; nopCommerce's
Admin › Catalog), holding the flows that start there. Code proposes areas from the app's menu, with folders as the fallback;
a person confirms them once in `specs/areas.yaml`; code checks that file and writes `specs/areas.json`. No AI anywhere (D156).
Decisions: D218–D228; plan and numbers: `phases/r3-flows.md`.

## Commands

| Command | What it does |
|---|---|
| `stangent areas propose` | Reads `.stangent/graph.sqlite` and `specs/flows/index.json`, and writes `specs/areas.proposed.yaml` (never `specs/areas.yaml`, D180). Prints the menus taken, each area with its count of start flows, the ties, any flow it couldn't place, and the areas flagged large. |
| `stangent areas check` | Reads `specs/areas.yaml` and writes `specs/areas.json`. Exit 1 with `problems` when a start flow sits in no area, a tie is unanswered, an answer's flow is gone or names no area, a menu item or folder a flow sits on is in no area, or two areas list the same address, folder or name. |

Output follows `docs/cli-conventions.md`. A missing graph is `no_graph`, a missing index or `areas.yaml` `not_found` (exit 3).

## What goes in an area

- **Start flows only** (D218): clicks and submits, screen openings, jobs and handlers. A shared flow belongs to none; `areas.json`
  lists, per area, the shared flows its starts reach.
- **Screens** (D219): every page a route opens (its routed component) and every MVC action that shows a page (its view;
  display-only ones too). A view path known only at runtime takes MVC's convention, `Views/<controller>` in its project.
- **Homes** (D219): a frontend's root is the deepest folder holding all its screens; a screen's home is the next folder down
  (Sonarr `frontend/src/Series`). A Razor screen's home is the folder after `Views/`, named with its project and the folders
  between, `Areas/` dropped (`Nop.Web › Admin › Product`). A route matching any URL (`*`, `**`) makes no home.
- **The menu** (D221, D226): `menu-item` facts (`docs/code-graph.md`). A menu needs three or more items leading to screens; the
  one reaching most screens is taken first, and one more than half of whose screens a taken menu already reaches is skipped.
  An item matches a screen by its address: a route's path (a `:param` matches any one segment; the fewest params win), or an
  MVC action (`mvc:<area>/<controller>/<action>`, `route:<name>` through a named conventional route). A screen takes the
  deepest item matching it. Names come from the translation files: `en.json` and the like, then .NET XML resources, looked up ignoring case (D228, D229).
- **Menu areas**: each taken menu's top-level item is an area, two with one name merged; addresses leading into an MVC area
  prefix it (`Admin › Catalog`). A home whose screens sit on menu items joins the area holding most of them, so its other
  screens follow (Sonarr's series details join Series).
- **Folder areas**: a home no menu reaches is an area of its own (Kavita's `manga-reader`).
- **Clicks** (D219, D222): a click in a home takes the screens of that home that show it (walking up through what renders,
  opens or calls it). A click in a shared folder takes every screen the walk reaches, or code in another home; more than one
  area is a tie for the person. One only the app's frame shows (the walk reaches code nothing shows) goes to **App frame**;
  one that reaches nothing is a tie with every area. The catch-all route's screen goes to App frame too.
- **Jobs and handlers** (D223, D227): the start flows leading to one, directly or through shared flows, are counted by area;
  the deepest area holding more than half takes it (a split area counts its parts), and a job so placed counts for those it
  leads to, repeated until nothing changes. An even split between exactly two areas is a tie; otherwise it goes to its
  backend folder, the first folder below its class's project (`NzbDrone.Core › Tv`); an interface's job takes its one
  implementation's.
- **Size** (D221, D224, D226, D227): an area over 40 start flows splits, a menu area by its children (`Settings › Indexers`), a
  folder area by its next folder; a home most of whose menu screens sit in one part moves to it; a click two parts could
  hold goes to the area itself. A part left empty folds back; an area with no flows isn't proposed. Over 40 with nothing
  to split by is listed in `large`.

## Files (D225)

`areas.yaml` (and `areas.proposed.yaml`, the same shape, with each area's count as a comment):

```yaml
areas:
- name: 'Series'          # the person's name for it
  menu:                   # menu items' addresses it holds
  - '/series'
  folders:                # screens' homes and backend folders it holds
  - 'frontend/src/Series'
large:
- area: 'Settings'
  flows: 77
  why: 'over 40, nothing to split by'
ties:
- flow: 'click-episodedetailsmodalcontent-handlemonitorepisodepress'
  choose: ['Series', 'Activity']
  answer: ''              # the person's pick, one of choose
```

A flow's screen matches the area listing its menu address; otherwise, or when another area lists a folder holding it deeper
than any folder the menu's area lists, the area listing the deepest folder holding it. So a person **merges** by listing two
sources under one name, **splits** by giving a menu address or a deeper folder its own area, and new flows land by themselves.
An area's **parts** are the areas named `<it> › …` (D227). A tie is keyed by flow id: a renamed flow's answer shows as gone.

`areas.json` (contract `areas` v1, code-made, committed): `contract`, `version`, `commit` (the flow index's), `areas` (each
`name`, its start `flows`, and the `shared` flows they reach), `flows` (every placed start flow → its area), `open` (ties still
unanswered → their choices) and `unplaced`. The same graph, index and `areas.yaml` write the same bytes.
