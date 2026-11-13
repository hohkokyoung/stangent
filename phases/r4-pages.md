---
phase: r4
name: pages
status: in progress
consumes: { flows: 1, areas: 1 }
provides: { pages: 1 }
---

# Rebuild layer 4: pages in plain words (D156 P1, D251–D258)

Grilled with the owner on 2026-11-12, one question at a time. Steps 2–4 built 2026-11-12 (D259–D263), sections shrunk (D264, D265), below; the pilot not run.

## Bottom line

Each area's `page.md` (D230, D233) gets a few plain paragraphs a PM can read, written into the empty place
between its two layer 4 marker lines. **Code decides what the page says. An AI only words it.** Code cuts
every flow into a short list of numbered items, and the writer turns those items into sentences, each one
naming the items it came from. Code then rejects any sentence that names no item, states a 🟡/❓ as certain,
drops a condition, or leaves an item out. The linking judge (layer 5) reads the same item lists, never the
AI's words, so a bad page sentence can't make a bad link. Layer 4 is the first layer since the old pipeline
that uses AI again.

## The design, by decision

| | Decision |
|---|---|
| Reader | people (a PM, a BA, a developer new to the app), not the linking judge; one code-made summary per flow is read by both the page writer and the judge (D251) |
| Items | per flow: who, start (button label, screen address, job schedule), saves, reads, messages, outside calls, screens opened, early exits, checks grouped by field, what it sets off later; each with an id, file and line, ✅/🟡/❓, and every condition on the way to it; calls ending nowhere (package code, hubs, caches) dropped, a save never (D251) |
| Shows | only the texts a flow already holds, with their conditions, a translation call turned into its English text (D228's reader); component names never read for meaning (D252) |
| Shape | "In short", then one section per screen (its opening flow and the clicks on it), then "On its own" (handlers grouped by the message they answer, jobs together), then "Can't tell from the code"; a contents list; one page per area however big (D251, D253) |
| Who | always shown; common kinds by code's template ("anyone signed in", "anyone, signed in or not", "Sonarr itself, on its own"); named access rules worded by the AI; once in "In short" when the whole area shares it; only what the server allows (D251) |
| Left to the links | inputs, scenarios, build settings (D251) |
| AI step | one call per section: `stangent pages prepare <area>` → headless Claude Code through the batch driver → `stangent pages check`, one retry, else the section keeps the code-made list; "In short" last, from the checked sections; a section rewritten only when its items' hash changes (D254) |
| Model and cost | Claude Sonnet 5.5, medium effort, pinned in `stangent.yaml` (D161); pilot `--max-cost 25` (D126, D256, D266: ~$7 a run after D264/D265) |
| Pilot | Sonarr: Series, Series › Add New, Settings › Indexers; Kavita: Reading Lists, registration; nopCommerce: Nop.Web › Checkout, Admin › Catalog (D257) |
| Old pages | `specs/reference/` retires one reader at a time: people at layer 4, old linking at layer 5, tickets at layer 6, the files and writer in the cuts at layer 7 (D258) |

## Worked example (Sonarr's Series › Add New)

The Add click's flow is 137 steps. Code cuts it to about a dozen items:

```
A1  calls an outside service, GET (SkyHookProxy.cs:54)   ❓ where: URL built at run time   when: Add pressed
A3  saves NamingConfig (NamingConfigService.cs:30)        when: Add pressed · no path given · `config == null`
A4  saves Series (SeriesService.cs:84)                    when: Add pressed
A5  sends SeriesAddedEvent → 3 handlers 🟡                when: Add pressed
X1  refuses when the lookup answers 404 (SkyHookProxy.cs:60)
K4  TvdbId: GreaterThan(0), SeriesExistsValidator
```

The writer answers with sentences that name their items (`{"text": "…", "items": ["A1","A4"]}`). Code rejects
"Three other parts are told a show was added" (cites the 🟡 A5 without "probably") and "Sonarr looks the
show up online" (cites the ❓ A1 as if the destination were known), and flags any item no sentence covers.
After the one retry the PM reads:

> **Adding the show.** Press **Add** and Sonarr fetches the show's details from an outside service (which one
> is set at run time), then saves the show. If you didn't give a folder, it makes one from your naming
> settings, creating default settings the first time. Three other parts of Sonarr are probably told a show
> was added. *[details]*
> **It refuses if** the show can't be found, the folder isn't valid or isn't one of your root folders, the
> quality profile doesn't exist, or the show is already in your library.

## The bar, frozen before building (D255)

On the 7 pilot areas:

1. **Code's checks pass on every pilot page** after the one retry: every sentence cites an item; every sentence
   on a 🟡/❓ item uses a word from a fixed hedge list; every condition of every cited item is marked "said" or
   "always true here"; no save, message, outside call, 🟡/❓, early exit or check group uncited; at most 5% of
   sections fall back to the code-made list.
2. **Repeatable:** the same pilot sections written three times; the item sets each cites match by code in at
   least 90% of sections. Words may differ.
3. **True:** code draws 60 sentences across the pilot pages; one fresh agent (D61, D158) reads each against its
   items and quoted code lines; at least 57 true and adding nothing, reported as a rate with its range (57 of
   60 still allows about 1 in 10 wrong).
4. **Readable:** the owner reads 3 pilot pages chosen by code (biggest, middle, smallest): "could a PM tell what
   this part does?" and "when it refuses?", 3 of 3 yes on both.
5. **Nothing else changes:** `areas check` passes; every page's code-made parts and every flow byte for byte
   unchanged.

**Not shown by this bar, said plainly:** whether pages help anyone do their job (the only reader so far is the
owner); how a repo Stangent was never tuned on fares (R.2).

## Build order

1. **Before the pilot, the owner:** confirms Kavita's and nopCommerce's `areas.yaml` (32 and 9 open ties,
   answered by group as D231 did for Sonarr). **Done 2026-11-13 (D267)**; on the confirmed files the pilot areas
   measure as above (Checkout 19 start flows, not 20).
2. **The summary, by code, no AI:** `stangent pages summary <area>` cuts each start flow into D251's items, with
   D252's shown texts and D253's section layout; byte-for-byte rebuilds; tests on fixtures in the three stacks.
   Free.
3. **The AI step around it (D60, D162):** `pages prepare` writes one bundle per section, the batch driver runs
   it, `pages check` runs D251's checks, one retry, writes between D233's marker lines; hashes per section
   (D254); `--max-cost` (D126); model and effort pinned (D161). Behind a switch, `pages`, off by default (D120).
   Tested with fakes only. Free.
4. **Docs:** `docs/pages.md`; the `pages` v1 contract; a layer 4 note on the old `specs/reference/` (D258).
5. **Set up 2026-11-13 (free):** each clone's `stangent.yaml` has `ai.steps.stangent-area-writer: {model: claude-sonnet-5-5,
   effort: medium}` and `components.pages: true`; `pages sections` lists 124 new sections on the 7 areas, all on Sonnet 5.5
   medium; `spec-run.sh --estimate` stops before any call. Run per area: `plugin/scripts/spec-run.sh --jobs 4 --max-cost 25
   --pages "<area>"` from its clone.
   **The pilot run, paid, asked first:** the 7 areas once, then twice more for bar item 2; about $20 under the
   $25 cap (D266). Before it, the first real Claude Code run also settles layer 1's owed check (D161 on real Claude
   Code).
6. **Score:** items 1, 2 and 5 by code; item 3 by one fresh agent (~80–120K tokens, asked first); item 4 by the
   owner.
7. **Pass:** the old page writer stops being run (D258), `pages` turns on, OVERVIEW and roadmap updated.
   **Fail:** one fix attempt (D138); for item 3 that can be Opus 5.5 in place of Sonnet; a second failure
   removes the step or leaves its pages marked unconfirmed until a person reads them.

## Built (2026-11-12): steps 2–4

Free, no AI call made. `stangent pages summary` (`src/Stangent.Flows/PageSummary.cs`), `pages sections`/`prepare`/`check`
(`src/Stangent.Spec/Words/AreaWords.cs`), the `pages` switch (off), the writer agent `stangent-area-writer` with its
instructions and schema, `spec-run.sh --pages <area>`, tests on hand-made flows in React, Angular and Razor shapes and the AI
step with fake answers (`tests/Stangent.Core.Tests/PageSummaryTests.cs`), `docs/pages.md` (contract `pages` v1), a D258 note
in `docs/spec.md`. Settled while building, confirmed by the owner 2026-11-13: D259 (summaries rebuilt, words in
`specs/pages-runs.json`), D260 (how flows become items; the shared-save rule), D261 (sections), D262 (checks), D263
(`--proposed`).

**`pages summary` on the 7 pilot areas** (Kavita and nopCommerce by `--proposed`, their ties still open; byte for byte the same
on two runs; "chars" = the item lines the writer would read, before the rules):

| Area | Sections | Start flows | Items | Must cite | 🟡 | ❓ | Chars | Biggest section |
|---|---|---|---|---|---|---|---|---|
| Sonarr Series | 20 | 78 | 2,088 | 1,481 | 109 | 95 | 1,858K | 935K (jobs, 712 items) |
| Sonarr Series › Add New | 3 | 5 | 114 | 55 | 4 | 7 | 42K | 37K (/add/new, 101 items) |
| Sonarr Settings › Indexers | 2 | 8 | 510 | 435 | 5 | 31 | 174K | 174K (the screen, 509 items) |
| Kavita Reading Lists | 3 | 12 | 854 | 436 | 14 | 3 | 893K | 584K (/lists, 503 items) |
| Kavita registration | 5 | 8 | 246 | 158 | 62 | 4 | 169K | 57K (clicks, 103 items) |
| nopCommerce Nop.Web › Checkout | 12 | 20 | 1,443 | 667 | 50 | 7 | 2,093K | 1,388K (clicks, 829 items) |
| nopCommerce Admin › Catalog | 23 | 61 | 1,998 | 979 | 5 | 10 | 1,539K | 859K (clicks, 1,061 items) |

**Against the worked example (Series › Add New):** the Add click's items are there as planned: the lookup's outside call ❓
("URL built at runtime"), NamingConfig saved only if `config == null`, Series saved, SeriesAddedEvent sent to 3 handlers 🟡,
"throws SeriesNotFoundException" when the lookup answers 404, and `SeriesResource.TvdbId: GreaterThan(0),
SetValidator(seriesExistsValidator)`. It is not "about a dozen": the click plus the screen that shows it give 101 items in one
section (17 check groups, 19 exits, 43 shown texts, 11 reads), and D255 has every exit and check cited with every condition
marked. **This breaks D256's estimate** (~2–3K tokens in a call): the largest sections are 150–350K tokens. Early exits are
most of it (Series: 918 of 2,088 items). Waiting on the owner before step 5 (`questions.md`).

## Shrunk (2026-11-12, D264, D265)

Owner: "Yes, shrink first". Exits keep their own conditions only and merge by what they throw; a section over 40,000
characters splits by flow (D264); reads and shown texts merge by table or wording, keeping shared conditions (D265, the
assistant's call, confirmed 2026-11-13). Re-measured (same runs as above):

| Area | Sections | Items | Must cite | Chars | Biggest section | Sections over 40K |
|---|---|---|---|---|---|---|
| Sonarr Series | 31 | 2,026 | 1,591 | 853K | 100K (235 items) | 9 |
| Sonarr Series › Add New | 3 | 99 | 49 | 29K | 25K (87 items) | 0 |
| Sonarr Settings › Indexers | 8 | 1,543 | 1,475 | 329K | 114K (427 items) | 5 |
| Kavita Reading Lists | 8 | 539 | 404 | 308K | 104K (142 items) | 3 |
| Kavita registration | 5 | 211 | 140 | 119K | 34K (45 items) | 0 |
| nopCommerce Nop.Web › Checkout | 21 | 1,497 | 860 | 1,629K | 388K (286 items) | 15 |
| nopCommerce Admin › Catalog | 49 | 2,658 | 1,552 | 1,949K | 169K (148 items) | 11 |

Sections over 40K are single flows the split can't cut. Rough cost: ~1.5M tokens in, ~0.35M out, about $7 a run and ~$20
for bar item 2's three runs, over D256's $10 cap. What's left: long path conditions on saves and shown texts inside one flow,
and items repeated in each part of a split section. The owner raised the pilot's cap to $25 (D266): all 7 areas, three runs.

## Pilot run 1, smoke test (2026-11-13, paid)

Sonarr's Series › Add New, 3 sections on Sonnet 5.5 medium: all 3 checked (one after its retry), none fell back, **$0.21**
(screen section 25K characters of items: 15.7K input tokens, 3.3K out, $0.096; the handler section $0.058 with its retry;
"In short" $0.060). The page reads as the worked example planned: who, searching, Add, "It refuses if", what happens on its
own, every 🟡 hedged. Found and fixed (free): the driver's "In short" step stopped on an unset variable; every AI call's
telemetry event was dropped because it carried two attributes telemetry-event v2 doesn't allow (layer 1's owed real-run
check, D161), so `cost estimate` had learned nothing; "Can't tell from the code" listed one ❓ twice. Also: Claude Code's sign-in
had expired (the owner signed in again), and the driver needs `--model`/`--effort` since the clones pin only the writer.

**Cost, re-estimated from real calls:** about $0.03 a call plus $0.0026 per 1,000 characters of items, so a full run of the 7
areas (124 sections, 5.2M characters) is about **$17–20**, and bar item 2's three runs about $55–60: well over D266's $25. Claude
Code also warned the plan's seven-day window was 68% used. The owner chose (D268): run 1 on all 7 areas (budget $22), the
repeats for bar item 2 only on the four small areas, $30 in all.

## Pilot run 1 (2026-11-13, paid, D268)

Stopped twice before finishing: the $22 run budget ran out at Admin › Catalog's "In short", then Claude Code's five-hour
usage limit stopped Checkout. **116 of 124 sections checked, 0 fell back to the code's list; $19.96 spent.**

| Area | Sections | Checked | Fell back | Not written | Retried | Cost |
|---|---|---|---|---|---|---|
| Sonarr Series | 31 | 31 | 0 | 0 | 7 | $5.51 |
| Sonarr Series › Add New | 3 | 3 | 0 | 0 | 1 | $0.21 |
| Sonarr Settings › Indexers | 8 | 8 | 0 | 0 | 2 | $2.61 |
| Kavita Reading Lists | 8 | 8 | 0 | 0 | 1 | $1.41 |
| Kavita registration | 5 | 5 | 0 | 0 | 4 | $0.95 |
| nopCommerce Nop.Web › Checkout | 20 | 13 | 0 | 7 | 1 | $3.00 |
| nopCommerce Admin › Catalog | 49 | 48 | 0 | 1 | 1 | $6.26 |

Retries came from single slips (one guessed item stated without a hedge word, one must-cite item left out), each fixed by the
retry. Left to finish run 1: Checkout's 7 sections (458K characters, ~$3.40) and Admin › Catalog's "In short" (~$0.15).

**Finished the same day:** all 124 sections written and checked, none fell back; run 1 cost **$21.64** in all. Two driver
fixes on the way: `--pages` now lists only sections not written for their current items (its estimate had counted all 49 of
Admin › Catalog's and refused the cap for one call), and "In short" runs when it's all that's left. One "cap reached" on Admin
› Catalog's "In short" with $3.06 free wasn't reproduced; the same call ran on the next try.

## Bar items 1, 2 and 5 (2026-11-13, by code)

1. **Code's checks: pass.** Every one of the 124 pilot sections passed D255 item 1's checks after at most one retry; 0 fell back
   (the bar allows 5%). 17 sections used their retry.
2. **Repeatable: pass, on D269's three areas.** Each of the 16 sections' first writer call was sent twice more on the identical
   input (`spec-run.sh --repeat 2`, $3.18); **15 of 16** cite the same must-cite items in all three answers (94%, bar 90%). The
   miss: Kavita registration's login screen, one answer leaving out A5. The three "In short" sections have no must-cite items
   of their own, so they match trivially: without them 12 of 13 (92%). The full cited sets agree 91–100% outside "In short"
   (76% on the miss); "In short" cites differently each time (33–50%), as expected for a summary. Wording differs, as allowed.
5. **Nothing else changes: pass.** `flows check` passes on all three clones (every flow byte for byte); `areas check` passes
   and rewrote no page on Kavita or nopCommerce. On Sonarr it rewrote 37 pages, stale since layer 3 step 6 rebuilt its flows
   (2026-11-12 13:03) after its last `areas check` (2026-11-11); the three pilot pages kept their words.

**Pilot spend: $24.82** of D269's $30 (run 1 $21.64, repeats $3.18). Left: item 3 (one fresh agent, ~80–120K tokens, asked
first) and item 4 (the owner reads 3 pages chosen by code).

## Known risks

- **The vocabulary gap is still layer 5's.** The document says "the user picks a root folder", the items say
  `SeriesResource.RootFolderPath: NotEmpty`; a shared summary doesn't bridge that.
- **Reading names is unchecked.** "Already in your library" from `SeriesExistsValidator` is the writer reading a
  name; only bar item 3's sample catches a wrong reading.
- **Pages say less about what a screen shows** than the old reference pages (D252); markup-bound data is parked
  in `improvements.md` §1.
- **Big areas stay long** (Series, Settings, Kavita's Customize, nopCommerce's Admin › Catalog), readable only
  through D253's grouping.
