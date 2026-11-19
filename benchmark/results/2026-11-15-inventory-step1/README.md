# Layer 5 inventory, step 1: the D330 match after the layer 2 facts (D329, D332)

Free, no AI. Graphs rebuilt with `--full` on 2026-11-15 from this branch's `stangent.dll`, run inside each clone in
`~/stangent-bench` (Sonarr `0bffefe`, Kavita `2d22536`, nopCommerce as cloned). The scanner hits are the pre-build ones
(`../2026-11-15-inventory-scan/*.json`; re-running `scan.py` gives the same hits, checked on nopCommerce: 0 differences).

```bash
python3 -I benchmark/inventory/match.py benchmark/results/2026-11-15-inventory-scan/<clone>.json ~/stangent-bench/<clone>/.stangent/graph.sqlite <out>.json
```

`match.py` counts a hit found when a fact of its kind sits in the same file on a line its element's opening tag covers
(nopCommerce's 2,660 display-name attributes are the labels of `asp-for` fields, so D330 doesn't count them; server fields: the attribute's line; data actions: the handler's line, or an entry starting up to 8 lines before). Hits the
scanner flags `likely_skip` (close, cancel, sort, paging, …) are left out of the main numbers, as in D330.

| Clone | Fields | Actions (buttons) | Server fields | Data actions | All lines |
|---|---|---|---|---|---|
| Sonarr | 359/366 (98.1%) | 390/393 (99.2%) | 469/469 (100%) | — | **1,218/1,228 (99.2%)** |
| Kavita | 333/333 (100%) | 446/451 (98.9%) | — | 88/95 (92.6%) | **867/879 (98.6%)** |
| nopCommerce | 2,881/2,899 (99.4%) | 685/735 (93.2%) | — | — | **3,566/3,634 (98.1%)** |

Before step 1 (D330's pre-build match): fields 1 (Sonarr), 48 (Kavita), 12 (nopCommerce); server fields and data actions 0.

What is still unmatched, by cause:
- **Sonarr fields (7):** `frontend/src/login.html`, a static HTML page outside the TypeScript app; no extractor reads plain `.html` pages.
- **Kavita data actions (7):** three "Send To" entries with `title: ''` (named at runtime); one built by `.push({…})` rather than in a list;
  one is the `ActionItem` interface's `callback:` declaration (a scanner false hit); two are lists holding a single action entry
  (side-nav "reorder", "cbl-manager"), left out on purpose: a list of one isn't a menu (D332).
- **nopCommerce actions (50):** `<a onclick>` links (21) and non-submit buttons written inside script strings (23) aren't `ui-event`
  facts (only submit buttons a script writes are, W1); `onClick` inside vendored JS strings (6). Buttons, not this step's work: for step 4.
- **nopCommerce fields:** `purify.js` (a vendored library's strings) and a few inputs in script strings.

Fact counts after the rebuild: Kavita 384 `form-field` (320 with a label key or words), 88 action `menu-item`; Sonarr 864 `form-field`
(476 server-defined, 766 labelled), 79 `field-choice`; nopCommerce 5,675 `form-field` (2,660 server-defined: display-name attributes on
models), 10 action `menu-item`.
