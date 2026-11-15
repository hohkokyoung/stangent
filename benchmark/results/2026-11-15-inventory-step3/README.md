# Inventory step 3: `stangent inventory` on the three clones (2026-11-15)

Layer 5 step 3 (D334): the PM inventory built from each clone's code graph (code-graph v6 from step 2, not rebuilt), free and
no AI, with the CLI built from `claude/phase2c-spec`. In each clone `stangent inventory` was run twice (identical bytes both
times: the second run wrote 0 files, 2 unchanged) and `stangent inventory check` passed (exit 0; a hand-edited `.md` gives
exit 1 `inventory_check_failed`; no graph gives exit 3 `no_graph`). The inventory files stay in the clones
(`specs/inventory.json`, `specs/inventory.md`); only their count blocks are copied here.

**After D335** (the skip list narrowed: a close-type handler skips only a line with no words of its own; bare next/previous/first/
last dropped; a page-size field that saves to a model kept), re-run on the same graphs, `inventory check` passing on all three:
lines Sonarr 1,291 → 1,292, Kavita 962 → 971, nopCommerce 4,130 → 4,143; skipped 100 → 99 (close 82, expand 6, table 11),
129 → 120 (close 96, expand 7, table 17), 54 → 41 (close 41, table 0); named by visible text 87.0%, 72.5 → 72.7%, 70.8 → 70.9%.
**After D336** (fresh check, 34 items: 21 right, 8 wrong; hidden server fields set aside, dash-joined capitalised words read as
words): Sonarr lines 1,273 (server fields 454, set aside 19 `hidden-field`), named 86.8%; Kavita and nopCommerce unchanged.
The tables below are the first run, before D335.

| | Sonarr | Kavita | nopCommerce |
|---|---|---|---|
| Graph commit | `0bffefe7` | `2d225367` | `77d112f3` |
| Screens | 37 | 34 | 399 |
| Lines | 1,291 | 962 | 4,130 |
| buttons / menu entries / fields / server fields | 410 / 59 / 349 / 473 | 481 / 109 / 372 / 0 | 941 / 178 / 3,011 / 0 |
| Named by visible text | 87.0% | 72.5% | 70.8% |
| ❓ | 168 | 265 | 1,206 |
| Skipped (close / expand / table) | 100 (83 / 6 / 11) | 129 (99 / 7 / 23) | 54 (41 / 0 / 13) |
| Set aside (`unbound-model-label`) | 0 | 0 | 584 |
| Display-name labels merged into a view field | 0 | 0 | 2,072 |
| Shared parts (groups / lines) | 40 / 99 | 67 / 317 | 84 / 1,015 |
| Not placed (groups / lines) | 112 / 610 | 6 / 85 | 48 / 280 |
| of which server-drawn classes / parts / menus | 89 (473 lines) / 22 (137 lines incl. menu) / 1 | 0 / 5 / 1 | 0 / 41 / 7 |
| Dialogs found (opened from code) | 19 | 48 | 0 (Razor has no walk) |
| Data menus placed / found | 1 / 2 | 12 / 13 | 0 / 7 |
| Control facts accounted for by the check | 1,391 | 1,091 | 6,844 |

## Count blocks (from each `specs/inventory.md`)

Sonarr:

```
- Screens: 37
- Lines: 1291 (button 410, menu-entry 59, field 349, server-field 473)
- Named by visible text: 87.0% (1123 of 1291)
- ❓: 168
- Skipped: 100 (close 83, expand 6, table 11)
- Set aside: 0
- Shared parts: 40 (99 lines)
- Not placed: 112 groups, 610 lines (22 parts, 1 menus, 89 server classes)
- Dialogs found: 19; data menus: 2 (1 placed)
```

Kavita:

```
- Screens: 34
- Lines: 962 (button 481, menu-entry 109, field 372, server-field 0)
- Named by visible text: 72.5% (697 of 962)
- ❓: 265
- Skipped: 129 (close 99, expand 7, table 23)
- Set aside: 0
- Shared parts: 67 (317 lines)
- Not placed: 6 groups, 85 lines (5 parts, 1 menus, 0 server classes)
- Dialogs found: 48; data menus: 13 (12 placed)
```

nopCommerce:

```
- Screens: 399
- Lines: 4130 (button 941, menu-entry 178, field 3011, server-field 0)
- Named by visible text: 70.8% (2924 of 4130)
- ❓: 1206
- Skipped: 54 (close 41, expand 0, table 13)
- Set aside: 584 (unbound-model-label 584)
- Shared parts: 84 (1015 lines)
- Not placed: 48 groups, 280 lines (41 parts, 7 menus, 0 server classes)
- Dialogs found: 0; data menus: 7 (0 placed)
```

## Examples (quoted from the `.md`, trimmed)

**Kavita.** A screen with a dialog and a data menu:

```
## Known For (/person/:name)
- Button: ❓ loadFilterByRole: no visible text found · `…/person-detail.component.html:68`
- uses Card Actionables (5 actions): see Shared parts
- uses Series Actions (23 actions): see Shared parts
  … (35 pointer lines in all)
### Dialog: {{personName}} Details
- Field: **Name** · `…/edit-person-modal.component.html:17` `formGroup.name`
- Field: **MAL Id** (help: mal-tooltip) · `…/edit-person-modal.component.html:27` `formGroup.malId`
### Menu: Person Actions _(code name)_
- Menu entry: **edit** · `UI/Web/src/app/_services/action-factory.service.ts:1137` `edit`
```

A shared part: `### Card Actionables _(code name)_`, "Appears on 21 screens: All Smart Filters (/all-filters); All Series
(/all-series); …", 5 actions, all ❓ (`openMobileActionableMenu`, `performDynamicClick`). The data menus built in one method
(`ActionFactoryService._resetActions`, 12 menus) each got their own title from the member they're assigned to: "Series Actions"
(23 entries, 16 screens), "Volume Actions", "Chapter Actions", "Reading List Actions". Not placed: `### App _(code name)_`
(the app shell, 41 actions), `### Menu: Nav Items` (the side navigation's data).

**Sonarr.** A screen whose dialogs are rendered as JSX (React modals are parts, not dialogs from code):

```
## Tags (/settings/tags)
- Button: **Add Auto Tag** · `frontend/src/Settings/Tags/AutoTagging/AutoTaggings.tsx:57` `onEditPress`
- Field: **Remove Tags Automatically** (help: Remove tags automatically if conditions are not met) · `…/EditAutoTaggingModalContent.tsx:146`
- Button: **Save** · `…/EditAutoTaggingModalContent.tsx:236` `handleSavePress`
```

A shared part: `### Confirm Modal _(code name)_`, appears on 20 screens, one line `❓ onConfirm`. A data menu placed:
`### Menu: Sections _(code name)_` on Settings (/settings), entries Media Management, Profiles, Quality…. Not placed, server-drawn:
`### Newznab Settings, 9 fields _(code name; drawn by a generic form from the server's list)_` with **URL**, **API Path** (help:
Path to the api, usually {url}), **API Key**; 89 such classes, 473 lines, as D329 expected. Not placed, components: `Calendar`
(16 actions), `Page Layout` (the shell), `Filter Modal`, `Menu: Groups` (the sidebar's 35 entries).

**nopCommerce.** A screen whose form lives in a partial shared by Create and Edit:

```
## Edit category details (ANY /Admin/Category/Edit/{id?})
- Button: **Save** · `…/Views/Category/Edit.cshtml:27` `cshtml form 1`
- Button: **Save and Continue Edit** · `…/Views/Category/Edit.cshtml:31` `cshtml form 1`
- uses Create Or Update (2 actions, 30 fields): see Shared parts
- uses Table (4 fields): see Shared parts
```

A shared part: `### Table _(code name)_` on 140 admin screens (the grid's own inputs, all ❓ `input`). Not placed: the
`EditorTemplates` (`Int 32`, `Date`, `Decimal`, `Address`, 14 in all), product-page partials (`Add To Cart`, `Product
Attributes`, `Gift Card Info`), `_Root`/`_AdminLayout` (the shells), and `Menu: Fill Base Root Async` (the admin menu, 106
entries, built in C#).

## What looks wrong (general faults, not fixed here)

1. **The skip list catches real controls (D334 item 5 as fixed).** nopCommerce: the checkout's five "Next" buttons
   (`Checkout/BillingAddress.cshtml:213`, …) and the "Page size" setting on category, manufacturer and vendor edit and reward
   points (`Category/_CreateOrUpdate.Display.cshtml:37`) are skipped as `table`. The handler half also skips buttons whose own
   words aren't on the list: Kavita "Download", "View activity", "Back to {{action}}"; Sonarr "Stay and review changes"
   (`onCancel`), "Hide episodes" (`handleExpandPress`). All visible in `skipped` with their reason.
2. **App shells are never placed.** The root component or layout view (Kavita `App`, Sonarr `Page Layout` and the sidebar
   menu `Groups`, nopCommerce `_Root`, `_AdminLayout`) is rendered by no screen, so the main navigation and header controls sit in
   Not placed. A general rule (a part every screen sits inside) would place them once as a shared part.
3. **Pointer noise from the dialog walk.** Kavita's card menus reach `ActionService`, which opens most edit dialogs and menus,
   so a screen with cards points at ~35 shared parts (Known For lists 35 pointer lines); 16 dialogs and menus appear on the
   same 16 screens.
4. **Titles keep template placeholders** (`{{personName}} Details`, `Edit Reading List: {{name}}`, `{{count}} total items`) and
   take odd heading words (Kavita `-label`, `Kavita ,`; Sonarr screen `/series/:titleSlug` titled "Page not found" from a branch
   heading). These come from step 2's `screen-title` facts and lookup.
5. **Data menu entry names**: Kavita's action-factory entries are keys the app prefixes at runtime; one that reads as words is
   shown as written (`edit`, `merge`, lowercase), others ❓. Help text keys shown as words (`mal-tooltip`).
6. **Not placed parts the graph doesn't link**: nopCommerce `EditorFor` templates and product-page partials rendered from the
   product template's code; Sonarr `Calendar` (its page has 20 own lines, but the `Calendar` component isn't reached).
7. **nopCommerce has no dialogs or placed data menus by design** (Razor views have no walk; its menus are built in C#).
