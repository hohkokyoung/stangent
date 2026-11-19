# Layer 5 inventory, step 2: the label lookup (D327, D333), 2026-11-15

Share of lines named by the words a user sees, per clone, from `stangent flows labels --flows --top 8 --sample 8` run inside each
clone with this branch's build, graphs rebuilt `--full`. A floor check of extraction (D327 item 3), never measured against a planted key.

**Before** is what the code at `fb245ed` could name with the same graph facts: the old lookup (English JSON only, no resource XML;
words as written counted even with no letter in them), computed by `labels-before.py`; fields and screens had no lookup in layer 3 at
all, so their "before" is that old lookup applied to the facts' keys, and screens were named by route only. Click flows "before" are
the names in each clone's committed `specs/flows/index.json` (flows v5), a name equal to the handler's (or "… cshtml form N") counting
as code.

| | Sonarr before → after | Kavita before → after | nopCommerce before → after |
|---|---|---|---|
| Buttons (click/submit events) | 409 → 408 / 505 (81.0 → 80.8%) | 449 → 447 / 606 (74.1 → 73.8%) | 49 → 835 / 991 (4.9 → 84.3%) |
| Fields (client and Razor) | 284 → 284 / 354 (80.2%) | 320 → 317 / 376 (85.1 → 84.3%) | 17 → 1,959 / 3,015 (0.6 → 65.0%) |
| Server-defined fields | 470 → 473 / 473 (99.4 → 100%) | — | 0 → 2,254 / 2,660 (0 → 84.7%) |
| Choices (fields with an enum or list, every choice named) | — → 34 / 76 (44.7%) | — → 3 / 23 (13.0%) | — → 5 / 34 (14.7%) |
| Screens | 0 → 34 / 37 (91.9%) | 0 → 26 / 34 (76.5%) | 0 → 326 / 399 (81.7%) |
| Click flows (layer 3) | 60 → 66 / 116 | 6 → 142 / 223 | 1 → 318 / 336 |
| Screen-opening flows (layer 3) | 0 → 31 / 31 | 0 → 19 / 26 | 0 → 326 / 399 |

Sonarr's buttons drop by one: a "button" whose only text was a symbol no longer counts as words; three gained by a prop's button
(D333) offset most of the four such. Kavita's "before" counted symbol-only text and values pointing at a missing key (`{{common.copy}}`) as words; both are ❓ now.

**After the fresh check (D333 items 13–17):** key references followed (Kavita's 657 `"{{common.…}}"` values now give their words, so its counts barely move but the words are right), placeholder options and options drawn through a template count as made at runtime (choices totals fell: Kavita 36 → 23, nopCommerce 46 → 34, and their shares with them), branch headings take the plain words. These numbers are after those fixes.

## What is left ❓, and why (from the reports' `missing` lists)

- **Words made at runtime** (all three): `{{task.title}}`, `{{label()}}`, a menu's `action.title`, a detail screen's title from the
  record (Kavita's readers and series pages, Sonarr's `/series/:titleSlug`). Code can't know them; ❓ is right.
- **Fields with no label element near them**: table-cell inputs labelled by a column header (Sonarr's import table, Kavita's mapping
  rows), search boxes with a placeholder only, nopCommerce's per-store override checkboxes (`…_OverrideForStore`, a bare box beside
  the setting).
- **nopCommerce plugins' strings** (most of the 406 server fields, 178 buttons and many fields left): plugins add their strings in C#
  install code (`["Plugins.Misc.Polls.Fields.DisplayOrder"] = "Display order"` passed to a resource-adding call), not in a translation
  file. Not read: a rule for it would fit this one app's install pattern (D333).
- **Choices**: TypeScript enums and C# enums whose members carry no label attribute (Kavita shows its enums through pipes at runtime;
  Sonarr's provider enums without `[FieldOption]`); lists built at runtime are not counted.

Files: `labels-<clone>.json` (shares, sources, first ❓ lines, a fixed sample of named lines), `labels-before.py`.
