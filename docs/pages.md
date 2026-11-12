# Pages (contract `pages` v1, rebuild layer 4)

Each area's `specs/areas/<area>/page.md` (D230, D233) gets a few plain paragraphs for a person who doesn't read code: a PM,
a BA, a developer new to the app. **Code decides what the page says; an AI only words it.** Code cuts every start flow of
the area into numbered items; the writer turns them into sentences, each naming the items it rests on; code rejects any
sentence that cites nothing, states a 🟡/❓ as certain, drops a condition, or leaves a must-cite item out. The linking judge
(layer 5) will read the same items, never the AI's words. Decisions: D251–D265; plan, bar and numbers: `phases/r4-pages.md`.

## Commands

| Command | What it does |
|---|---|
| `stangent pages summary <area> [--proposed] [--format json\|md\|counts]` | The area's summary by code, no AI: its sections in page order, each with its numbered items. Byte for byte the same from the same flows and `areas.yaml`. `<area>` is the name in `areas.yaml` or its folder. `--proposed` places flows by `specs/areas.proposed.yaml` (a look before the areas are confirmed, D263). `md` prints the items as a person reads them; `counts` how many, per section, with `chars` (the item lines the writer would read, before the rules). Writes nothing. |
| `stangent pages sections <area>` | Each section and its state against its current items: `new`, `changed`, `checked`, `fallback`. "In short" is listed last. Needs the writer's model and effort pinned (below). |
| `stangent pages prepare <area> <section> [--retry-failed]` | Writes the section's bundle to `.stangent/pages/<folder>/<section>/writer.md` and says what runs next: the writer, or nothing (`current`; `fallback` when it kept the code's list with these same items, unless `--retry-failed`; `waiting` for "In short" while another section isn't current; `off` when the switch is off). |
| `stangent pages check <area> <section> [--usage <claude result>]` | Checks `writer.json` (below). A failure gets one retry with the errors (`retry`, exit 1); a second failure keeps the code's list (`fallback`). Either way the result is recorded in `specs/pages-runs.json` and the page's words are rebuilt. |
| `plugin/scripts/spec-run.sh --max-cost <usd> --pages <area>` | The batch driver (D60, D162): every section but "In short" in parallel (`--jobs`), then "In short" alone; `--estimate` first; `--max-cost` is required (D126). |

Output follows `docs/cli-conventions.md`. No `areas.yaml` is `not_found` (exit 3); a page with no layer 4 lines is
`page_markers_missing` (run `stangent areas check`).

## Switch and settings

- **Off by default:** `components.pages: true` in `stangent.yaml` turns `pages prepare` on (D120); it also needs `ai` on.
  `pages summary` always works.
- **Model and effort pinned (D161, D256):** `ai.steps.stangent-area-writer: { model: claude-sonnet-5-5, effort: medium }`
  (or the global `ai.model`/`ai.effort`). Nothing pinned: `sections`, `prepare` and the driver refuse; there's no default.
- **Cost:** a paid run needs `--max-cost` (D126). `stangent cost estimate --step "pages check"` estimates from earlier calls.

## The summary (D251–D253, D260, D261)

**Sections, in page order** (D253): "In short"; one per screen (its opening flow and the clicks D219 places on it; a click on
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

**JSON** (`pages summary`, contract `pages` v1): `contract`, `version`, `commit` (the flow index's), `area`, `folder`,
`sections` (each `id`, `title`, `kind` (in-short, screen, clicks, message, jobs), `group` ("" or "On its own"), `flows`, `items`
(each `id`, `kind`, `text`, `flows`, `at`, `code`, `label` (certain, guessed, unknown), `reason` for 🟡/❓, `conditions` (each
`at`, `code`, `kind`, `case`, `text`), `mustCite`, `alsoAt` when merged)), `cantTell` (section and item of every ❓), `missing` (start flows with no
file, listed, D111), `counts`. Not committed: code rebuilds it (D259).

## The writer and the checks (D254, D255, D262)

One call per section (D254), on Claude Sonnet 5.5 at medium effort (D256). The bundle holds the instructions
(`src/Stangent.Spec/Instructions/area-writer.md`), the output schema (`schemas/area-section-output.v1.json`) and the items, each
with its numbered conditions and **must cite** marked. "In short" gets the other sections' checked sentences and the items they
cite, as `§n:ID`. The answer is paragraphs (an optional short `lead`) of sentences, each `{text, items, conditions}`, where
`conditions` marks every condition of every cited item `said` or `always`.

Code rejects: a schema error; a sentence citing no item or an id not in the bundle; a sentence citing a 🟡/❓ item without one of
the hedge words (probably, likely, may, might, appears, seems, could, perhaps, possibly, not known, unknown, isn't known, unclear,
can't tell, at run time); a cited item's condition left unmarked, or a mark on an item the sentence doesn't cite; a line break,
HTML or Markdown in a sentence or lead; a must-cite item no sentence cites. One retry with the errors, then the section keeps
the code's list and says so.

**Hash** (D254): the bundle (items, rules, schema), the model and the effort. Unchanged: no call. A model change re-runs every
section (D256); any section's change re-runs "In short".

## Files

- `specs/pages-runs.json` (committed, D259): per `<area>#<section>`, `words` (checked or fallback), `hash`, `model`, `effort`,
  `retried`, `writer` (usage), `spentUsd` (all attempts), and `paragraphs` (checked) or `errors` (fallback).
- `specs/areas/<area>/page.md`: the words between D233's two lines are rebuilt from the runs on every check: a contents line,
  each section's paragraphs with each sentence's items in a hidden `<!-- A1 A4 -->` note, the code's list where a section isn't
  written or fell back, then "Can't tell from the code". Everything outside the two lines is `areas check`'s, byte for byte.
- `.stangent/pages/<folder>/<section>/`: the run's bundle, answer, feedback and state (not committed).

## The old reference pages (D258)

`specs/reference/` (Phase 2a) retires one reader at a time. When layer 4 passes its bar, the old page writer stops being run
and people read `specs/areas/<area>/page.md`; the old files stay frozen for old linking (until layer 5) and tickets (until
layer 6), and go in layer 7's cuts on the owner's OK.

## Known limits

- Big areas still give big pages after D264/D265: on the 7 pilot areas 125 sections, ~5.2M characters of items, the biggest
  single-flow section 388K (Checkout's one-page form), roughly $7 a run (`questions.md`, 2026-11-12).
- A condition true on only some paths to a save in shared code, or to some reads of a table, isn't carried (D260, D265); an
  exit's path conditions are left to the flow's file (D264).
- What a screen shows is only the texts its flows hold (D252): a value shown with no label isn't on the page.
