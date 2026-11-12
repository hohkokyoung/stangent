---
phase: r4
name: pages
status: planned
consumes: { flows: 1, areas: 1 }
provides: { pages: 1 }
---

# Rebuild layer 4: pages in plain words (D156 P1, D251–D258)

Grilled with the owner on 2026-11-12, one question at a time. Nothing built yet.

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
| Model and cost | Claude Sonnet 5.5, medium effort, pinned in `stangent.yaml` (D161); ~$0.03 a section call; pilot `--max-cost 10` (D126, D256) |
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
   answered by group as D231 did for Sonarr). No building waits on this; only step 5 does.
2. **The summary, by code, no AI:** `stangent pages summary <area>` cuts each start flow into D251's items, with
   D252's shown texts and D253's section layout; byte-for-byte rebuilds; tests on fixtures in the three stacks.
   Free.
3. **The AI step around it (D60, D162):** `pages prepare` writes one bundle per section, the batch driver runs
   it, `pages check` runs D251's checks, one retry, writes between D233's marker lines; hashes per section
   (D254); `--max-cost` (D126); model and effort pinned (D161). Behind a switch, `pages`, off by default (D120).
   Tested with fakes only. Free.
4. **Docs:** `docs/pages.md`; the `pages` v1 contract; a layer 4 note on the old `specs/reference/` (D258).
5. **The pilot run, paid, asked first:** the 7 areas once, then twice more for bar item 2; about $6 under the
   $10 cap. Before it, the first real Claude Code run also settles layer 1's owed check (D161 on real Claude
   Code).
6. **Score:** items 1, 2 and 5 by code; item 3 by one fresh agent (~80–120K tokens, asked first); item 4 by the
   owner.
7. **Pass:** the old page writer stops being run (D258), `pages` turns on, OVERVIEW and roadmap updated.
   **Fail:** one fix attempt (D138); for item 3 that can be Opus 5.5 in place of Sonnet; a second failure
   removes the step or leaves its pages marked unconfirmed until a person reads them.

## Known risks

- **The vocabulary gap is still layer 5's.** The document says "the user picks a root folder", the items say
  `SeriesResource.RootFolderPath: NotEmpty`; a shared summary doesn't bridge that.
- **Reading names is unchecked.** "Already in your library" from `SeriesExistsValidator` is the writer reading a
  name; only bar item 3's sample catches a wrong reading.
- **Pages say less about what a screen shows** than the old reference pages (D252); markup-bound data is parked
  in `improvements.md` §1.
- **Big areas stay long** (Series, Settings, Kavita's Customize, nopCommerce's Admin › Catalog), readable only
  through D253's grouping.
