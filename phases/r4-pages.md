---
phase: r4
name: pages
status: needs review
consumes: { flows: 1, areas: 1 }
provides: { pages: 2 }
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

## Pilot stopped, page shape dropped (2026-11-13, D270)

Bar item 4 failed: the owner read Series › Add New and rejected it as bloat (no flowchart, no scenario steps, no edge cases
a PM can read). Items 1, 2 and 5 passed, item 3 wasn't run. **Layer 4 fails as designed.** The page is being redesigned per
task: a code-made flowchart, a scenario table, edge cases, plumbing folded away, FSD differences from layer 5 later. The new
shape was grilled the same day (D271–D276); this plan's design, bar and build order above are history.

**The new shape.** Per click that changes something (D271): a Mermaid flowchart by code (D273), a scenario table, refusals
in three buckets (D272), edge cases; display-only clicks one line each; no paragraphs (D276). The AI words labels only (D274).

**The new bar (D275), in order, each gate only if the one before passes:** (1) the owner reads three raw-label charts; (2) by
code, every change is in its chart or a named fold, byte-for-byte rebuilds, all 7 pilot areas; (3) a fresh agent reads 30
folded items, at most 2 wrongly folded; (4) a fresh agent reads 60 labels, half diamonds, at least 57 true and at most 1
flipped diamond; (5) the owner reads three finished pages. The size cap is set from a count between gates 1 and 2.

**First count and gate 1 (2026-11-13, D277).** `pages chart` built (241K tokens). Total nodes per chart, median / max: Add New
8 / 15, Series 14 / 155, Indexers 33 / 53, Reading Lists 30 / 201, registration 33 / 62, Checkout 73 / 281, Admin › Catalog
47 / 209. The owner's read: more generic (business logic), cleaner, nuance in the scenarios. So: an overview chart bounded
by its rules, a scenario table, D273's chart folded as the detailed one with a cap; shared framework saves folded.

**Reader changed to a developer (2026-11-13, D278).** The owner had assumed the page knew the FSD; it is code only. So the
page is for a developer: code-made, no AI wording, code's own labels with `file:line`, nothing folded for being technical.
D272, D274, D275 and D277 are archived; the bar above no longer applies and a developer's bar isn't written yet. Second
count (round 2, overview / scenario rows / detailed chart, median and max): Add New 3/8, 4/14, 5/10; Series 4/21, 7/46, 8/81;
Indexers 5/12, 8/10, 10/24; registration 7/9, 10/18, 17/22; Checkout 7/32, 21/77, 33/131; Admin › Catalog 3/24, 6/59,
10/112; Reading Lists 42/43, 87/94, 142/152. Fault found: folding a save passed by 10 or more flows hides real business
saves (Sonarr's "Delete series"); framework saves sit at 15–85% of an app's flows, business saves at 7% or less.

**The developer page's shape and bar (2026-11-13, D279, D280).** Per changing click, nothing folded: chart, scenario table
(holding the field checks), service failures, follow-ons, shared guards, shared-code changes. Bar, in order: (1) by code,
every change and stop of the flow file is on the page, byte-for-byte rebuilds, 7 areas; (2) 9 clicks × 4 set questions, one
fresh agent from pages, one from flow files, a third grading against source: page at least as good, at least 34 of 36, and
the shorter read; (3) the owner reads three pages as a developer. Every box and table row carries its flow-file step
number (D284), so gate 1 matches by step number and a reader jumps from the page to the step. The AI machinery (`pages sections`/`prepare`/`check`, the area writer,
its schema, `spec-run.sh --pages`, the run records) and the surplus overview code are removed in the build round (D285).
**Built and recounted (2026-11-13):** gate 1 passes on every area of the three clones; 23 of 123 pilot charts were over 40
nodes (11 at 41–60, 12 over 60), so the cap's default is 60 (D286). One-line flows list their own stops, shared guards as a count (D287).

**Fresh check 1 (2026-11-13):** 4 defects, 9 risks, 5 nits; fixes to the page (D288) and gate 1 (D289) in one round.

## Known risks

- **The vocabulary gap is still layer 5's.** The document says "the user picks a root folder", the items say
  `SeriesResource.RootFolderPath: NotEmpty`; a shared summary doesn't bridge that.
- **Reading names is unchecked.** "Already in your library" from `SeriesExistsValidator` is the writer reading a
  name; only bar item 3's sample catches a wrong reading.
- **Pages say less about what a screen shows** than the old reference pages (D252); markup-bound data is parked
  in `improvements.md` §1.
- **Big areas stay long** (Series, Settings, Kavita's Customize, nopCommerce's Admin › Catalog), readable only
  through D253's grouping.

## Gate 2 (2026-11-14, D280, D290): failed
9 clicks picked by code (seed 280); three fresh Sonnet agents (page reader ~152K tokens, flow reader ~187K, grader ~141K; ~480K in all).
Page 23/36 right, flow file 28/36 (bar: page at least 34 and at least the flow file); 8 answers split for a person, but even all 8 to the
page gives 31, so they don't change the result. By repo, page/flow: Sonarr 10/10, Kavita 7/12, nopCommerce 6/6. The page is far shorter
(168K characters against 1.22M), but the flow reader searched and read only about 190K.
Why the page lost marks (traced on the flow JSON): Identity store calls the flow file marks `database`, "guessed" (Kavita login's
refresh-token writes, TokenService.cs:71–73, steps 33–35) never reach the page; in-app messages and events (Kavita's
ReadingListUpdatedEvent and CoverUpdateEvent, Sonarr's ProviderStatusChangedEvent handlers), file writes (the cover upload) and client
timers are plain `call` steps in layer 3, so the page has no change to show, while the flow reader read them from the code text; two
conditions read backwards (Kavita's CoverImageLocked, nopCommerce's GenericAttribute save). Grades: `layer4-build/gate2/grades.md` in the
session notes.


## Gate 2 fixes built (2026-11-14, D291–D297, D300–D303)

One build round, no AI. Layer 3: Identity store steps by verb (D291), push steps (D292), file steps (D294), timer links (D295),
handled errors (D300), filter stops (D301), outside-service libraries (D302); one contract bump, `flows` v1 → v2, r4 marked
`needs review`. Layer 2 gained facts in place (`try`, `file-change`, `exceptionTypes`, a package call's `ns`); the three graphs
were rebuilt with `--full`. Page and gate 1: new kinds as changes, handled errors in a list of their own, receivers of every
message (D293), carried changes of repeated calls (D296), positive scenario rows and code lines on boxes that read the same
(D297); gate 1 checks all of them. Own calls in D303. Gate 1 passes on every area of the three clones (63, 28, 142). Charts over
the 60-node cap: Sonarr 14 → 54, Kavita 11 → 27, nopCommerce 29 → 45. Sonarr's series search job: 110 exits → 1 stop and 109
handled. Next: the D298 re-test.

## Gate 2 re-test (2026-11-14, D298): 33/36, one short
After D291–D304 (built ~343K + fix round ~193K; fresh check 2 ~191K, `layer4-build/check2-findings.md`), the same 9 page parts
re-cut (`layer4-build/gate2/retest/`; 317K characters, up from 168K, flow files 1.16M; Kavita login's part, 47K, is now a little
longer than its flow file, 45K). Fresh page reader ~152K tokens, fresh grader ~81K against the first key and D299's rulings.
Page 33/36 (Sonarr 12/12, Kavita 12/12, nopCommerce 9/12), 0 splits; bar 34 and at least the flow file's 31: fails by one. The grader
corrected the first key once: Sonarr's `InvalidSceneMappingException` is caught in `DownloadDecisionMaker` (lines 77, 159), so the
reader was right to list it as handled. Lost: nopCommerce payment info Q2 (Avalara) and edit product Q2 (Brevo), plugin outside
calls D302 left unseen; edit product Q3, where the page row X4 shows the normal end after a save (`ModelState.IsValid` and
`!continueEditing`, redirect to List, `ProductController.cs:1419`) as "Sent elsewhere … passes no change", so the reader took it for a
stop. Fixes: D305.

## Gate 2 run 3 (2026-11-14, after D305): passed, 35/36
D305 built (~219K tokens; plugin rule: a project under a folder named `Plugins`, `Plugin`, `AddOns`, `Add-ons` or `Extensions` is a
plugin; `src/Stangent.Flows/Walker.Plugins.cs`); tests 2308/2308; gate 1 passes on every area (changes/stops: Sonarr 6,365/1,524,
Kavita 2,537/1,371, nopCommerce 17,029/4,315). Same 9 page parts re-cut: 339K characters against the flow files' 1.16M. Fresh page
reader ~178K tokens (read all 340K characters), fresh grader against the first key, D299's rulings and the key correction.
Page 35/36, 0 splits: bar 34 and at least the flow file's 31 met, and the page is the shorter read. Lost: Sonarr search job Q4 named
the RSS sync job as running afterwards; the page's "after Saves PendingReleases, which the job RssSyncCommand reads and works through
later" reads like a follow-on, but nothing in the code queues that job (it runs on its own schedule). Run 2's files are kept as
`retest/*-run2.md`.

## Gate 3 (2026-11-14, delegated by the owner: "give it to ur call"): not passed
A fresh Sonnet agent read three pilot pages as a developer (~99K tokens; report `layer4-build/gate3.md` in the session notes):
Sonarr Series › Add New (25K characters) yes; Kavita Reading Lists (503K) no, except its small clicks; Sonarr Series (2.5M) partly.
Cause: huge flows. Kavita's three selection clicks (`selectItem`, `handleSelection`, `onTouchEnd`, each ~770K of JSON) walk on into
the whole bulk-action menu (saves, downloads, send-to-device email; 167 scenarios each), which runs only on a later click; Sonarr's
RefreshSeries (923 steps) and DiskScan (944 nodes) are too big to read. Also: titles repeat ("Save" x4, three handlers titled alike),
and lines like "saves NamingConfig" repeat. Fixes: D306.


## D306 built (2026-11-14, with D307's calls)
Cause of the selection clicks' size: Kavita's `BulkSelectionService.getActions` (run on every selection by `handleCardSelection`)
hands each bulk handler to `wireBulkCallback`, which stores it as the menu action's `callback` (`action.callback = (a) =>
handler(a)…`); layer 2 read "a function that calls its parameter" as calling it now, so the walk took in every bulk action
(saves, downloads, send-to-device email) that runs only on a later menu click. Now a function stored on a property is kept, the
walk stops there with a `waits` step ("leaves code for later (wireBulkCallback): …"; `flows` v2 → v3), and `selectItem`,
`handleSelection` and `onTouchEnd` (943K, 943K, 930K of JSON each) reach no server call: skipped as screen behaviour, the
reason naming the keepers. Graphs rebuilt with `--full`; three stale tie answers removed from Kavita's `areas.yaml`. Pages: heavy
shared work once in a part of its own (`<!-- shared <id> -->`, `## Shared work: X`), one line "runs X (see its part): N changes,
M stops" in each flow that reaches it, gate 1 checking each part and each line into it; distinct titles; one line per change
made both directly and in shared code. Gate 1 passes on every area (Sonarr 63, Kavita 28, nopCommerce 142). Sizes: Kavita
Reading Lists 503K → 38K characters; Sonarr Series 2.47M → 2.3M, Add New 25K → 28K (the three handlers now titled by class);
all pages Sonarr 9.4M → 10.4M, Kavita 4.9M → 2.9M, nopCommerce 16.8M → 17.4M. Shared parts: Sonarr 152, Kavita 25,
nopCommerce 1,528. Charts over the 60-node cap: Sonarr 54 → 70 (39 flows, 31 shared parts), Kavita 28 → 23 (17, 6),
nopCommerce 50 → 55 (43, 12): the parts show shared code's own stops and outside calls, never on a page before. Tests 2313/2313.
Next: gate 2 again (D298 method), then gate 3 on the same three pages.

## Checked before the gates, D308 and D309 (2026-11-14)
Checked by hand before spending on gate 2 and gate 3. Sonarr's Series page was still 2.3M characters, 1.85M of them scenario
rows (RefreshSeries' table 606K: each of 145 rows listed every step it passed, every step it skipped and every skipped change
in words): D308 makes a long flow's row name only the shorter step list, with ranges, and count skipped changes past 5. And
D306's parts were mostly tiny helpers (nopCommerce 1,528, median 4 steps), and 163 flows fell to one line because all their
saves had moved into parts, gate 2's nopCommerce payment-info screen among them: D309 keeps parts for work over 30 steps and
charts every click with a part box. Built here (no agent), tests 2314/2314, gate 1 passes on every area. Sizes now: Sonarr
Series 1.14M, Add New 28K, Kavita Reading Lists 38K, nopCommerce Admin › Catalog 0.91M; all pages Sonarr 7.1M, Kavita 1.8M,
nopCommerce 14.3M; parts Sonarr 87, Kavita 6, nopCommerce 155. D306's build took ~350K tokens.

## Gate 2 run 4 (2026-11-14, after D306–D309): passed, 34/36
Same 9 clicks re-cut with each click's shared parts appended (`retest/cut.py`): 252K characters against the flow files' 1.16M.
Fresh Sonnet page reader ~197K tokens (read all 252K), fresh Sonnet grader ~89K against the first key, D299's rulings and the
key correction. Page 34/36 (Sonarr 12/12, Kavita 11/12 + 1 split, nopCommerce 11/12): bar 34 and at least 31 met. Lost:
nopCommerce edit product Q3, the grader counted the cyclic "required products" check (`ProductController.cs:1257–1271`) as a
missed refusal; it is not a stop (the save goes on with the old `RequiredProductIds` and a warning), neither the flow file nor
the page has it, and run 3's grader on the same key did not count it: a grader judgement, left for layer 3 (a value put back
with a warning is not a step kind). Split: Kavita reading-list save Q1, the reader said the save at step 32 needs
`!CoverImageLocked`, while the page's row D8 says it passes step 32: a reader slip. Run 3's files are `retest/*-run3.md`.

## Gate 3 run 2 (2026-11-14, after D306–D309): not passed
Fresh Sonnet developer (~121K tokens), the run 1 questions (`layer4-build/gate3-prompt.md`), report `layer4-build/gate3-run2.md`.
Sonarr Series › Add New (28K, read whole) yes; Kavita Reading Lists (38K, read whole) partly, up from no; Sonarr Series (1.14M,
about 40K skimmed) partly, "mostly no as a whole". Named in the way: shared-framework noise ("saves NamingConfig", the 582-step
`RestController.TypedCreated` part with 19 unrelated stops), raw negated chart conditions ("not: …"), scenario rows that read as
contradicting a stop row (Kavita save D6/D7 beside X4), and Series mixing list, detail, search, import and jobs. Traced: the noise
is one layer 3 over-reach, `TypedCreated` calling the virtual `GetResourceById` on `this`, which the walk follows into every
controller's override (NamingConfigController's saves the default naming config). Fixes: D310. Tokens so far this round:
build ~350K, gate 2 ~286K, gate 3 ~121K.

## D310 built (2026-11-14)
Layer 3 part in `phases/r3-flows.md` (`flows` v4; r4 marked `needs review` for it). Pages: a `D` row whose condition's other
side is only a stop whose own `X` row carries the same condition (code at the same line) is dropped, unless a change sits on that
other side (Kavita's save loses D6 and D7 beside X4); diamonds read positively: an else or guard on a condition that isn't a
plain `!x` shows the condition itself with yes/no swapped, and in a stop's diamond with several conditions such a one reads
"(x) is false". "not:" diamonds: Sonarr Series 53 → 0, Kavita Reading Lists 14 → 0. Sizes (bytes): Series › Add New 28,301 →
23,972 (NamingConfig left only as a setup row, real: `GetSeriesFolder` → `GetConfig`), Kavita Reading Lists 38,211 → 37,250
(D rows 16 → 14), Sonarr Series 1,138,626 → 1,153,063: "saves NamingConfig" fell from 106 to 35 of 392 flows, under the 10%
framework share (D281), so each click now shows it as a setup row instead of one framework line. Gate 1 passes every area on all
three. Gates 2 and 3 not rerun here.

## Gate 2 run 5 (2026-11-14, after D310): passed, 36/36
D310 build: one Opus agent ~297K tokens (tests 2320/2320; gate 1 passes 63/28/142). Run 4's files renamed `retest/*-run4.md`,
`retest/cut.py` re-cut the 9 clicks (250K characters). Fresh Sonnet reader ~196K (read all; cut some very long nopCommerce
form-1-123 lines when printing), fresh Sonnet grader ~86K: 36/36 (12/12 each), bar 34 and at least 31 met. The grader checked
source twice: nopCommerce `CustomerEventConsumer` on the language event is a real, non-cache handler (Q4 right), and the cyclic
required-products case still saves, so not a refusal: the opposite of run 4's grader on the same point, so the score is 35 or 36
depending on the grader; both pass. Run 4's Kavita save Q1 split is gone (D310 dropped the D rows the reader misread).

## Gate 3 run 3 (2026-11-14, after D310): not passed
Fresh Sonnet developer (~94K tokens), same questions, report `layer4-build/gate3-run3.md`. Add New (24K, read whole) yes; Kavita
Reading Lists (37K, about half read) partly; Sonarr Series (1.15 MB, about 40K skimmed) no as a front door. Named in the way:
Add New, the 59-entry shared-flow list of utilities; Reading Lists, raw code conditions, nine near-identical
`ArgumentOutOfRangeException` stops, `/api/{*}` unfollowable, create/add/delete not obviously on the page; Series, 78 flat start
flows, 16 too large to draw, a 1,231-step screen flow reaching rename, import and search, one "see its part" line repeated ten
times, setup "saves NamingConfig" read as misleading. Traced: Series is breadth (34 of 233 areas are over 200K; D221 found
nothing to split by though `frontend/src/Series` has eight subfolders), asked of the owner (`questions.md`); the rest is page
noise code can remove: D311. Tokens this round: build ~297K, gate 2 ~282K, gate 3 ~94K.

## D311 built (2026-11-14)
Pages only, no flows contract change. (1) A list line repeated word for word in one part is one line with every step (Series'
`ParseEpisodeCustomFormats (see its part)` lines: 21 list lines with a "see its part" run → 8). (2) "Shared flows" leaves out
shared flows that change, stop and call nothing outside, all the way down, counted in one line; gate 1 checks each reached flow is
linked or quiet and counted. (3) `throw` stops of one exception kind in one class are one `X` row (or sub-bullet) listing every
stop, shared conditions once (Kavita Reading Lists updateFilter X rows 8 → 3). (4) Condition shapes phrased by code, raw code kept
in the row's code column or after the phrase. Shared flows left out: Add New 38 of 59, Reading Lists 26 of 39, Series 401 of 816;
all three clones 6,685 of 19,268. Sizes (bytes): Add New 23,972 → 19,809, Reading Lists 37,250 → 32,949, Series 1,153,063 →
1,055,001. Tests 2347/2347; gate 1 passes 63/28/142. Gates 2 and 3 not rerun here; Series is still breadth (split question open).

## D312 built (2026-11-14)
`areas propose` (fresh or on a confirmed file) splits an area over 40 with no menu children by its frontend folders' subfolders
(`docs/areas.md`, "Splitting a large area by folder"): a part gaining fewer than 3 start flows stays with the parent; a subfolder
whose menu screens sit on an existing part joins it; one part taking all is no split; jobs and handlers the parts share go to
`<Area> › Background` (condition 1), never the first part. `areas.yaml` rewritten in all three clones with a D312 header line.
Sonarr 63 → 66 areas: Series 78 → Series 9 (Edit Save, Move, Delete, MonitoringOptions save, history, search grab, filters),
› Details 7 (series/season/episode Monitor toggles, detail screen), › Index 11 (`/series` screen, bulk select footer, bulk
delete), › Library Import 2 → 6 (`AddSeries/ImportSeries`), › Background 47 (39 handlers, 8 jobs incl. RefreshSeries,
DiskScan, MoveSeries, searches; listed large). Page bytes: Series 140,147, › Details 202,373, › Index 60,429, › Background
669,051 (Series was 1,055,001). Settings 72, Kavita Customize 131, nopCommerce Admin › Catalog 61 and Nop.Web › Infrastructure
48 had nothing to split by (still large); Kavita 28 and nopCommerce 142 areas unchanged. Three Sonarr answers moved from Series
to › Details; no new ties. `areas check` (after `flows build` on Sonarr) and `pages verify` pass 66/28/142. Tests 2349/2349.
Gates 2 and 3 not rerun here: for condition 2, gate 3 reads Series (parent) plus › Details.

## Gate 3 run 4 (2026-11-14, after D311 and D312): not passed
Fresh Sonnet developer (~95K tokens), same questions; under D312's condition 2 the Series page is replaced by the Series parent
(140K) plus Series › Details (202K), read for one task (change how a series is edited, deleted, saved or monitored), yes needed
on both. Report `layer4-build/gate3-run4.md`. Add New (19.8K, read whole) partly, down from yes in runs 2 and 3 on a larger page;
Kavita Reading Lists (33K, 28K read) partly; Series parent partly, Series › Details partly; the pair "partly, leaning toward
being enough to start". Named in the way: no "start here" per flow (route, entry file on both sides, what it writes, events),
the empty layer 4 words comment read as a missing section, huge unrelated flows (interactive-search grab, the 1,231-step
`/series/:titleSlug` screen flow) before the small ones, unexplained 🟡/❓ and step numbers. One reader's verdict swings between
runs (Add New yes, yes, partly). Fixes: D313. Tokens: D311 build ~235K, D312 build ~181K, gate 3 ~95K.

## Gate 2 run 6 (2026-11-14, after D311 and D312): passed, 35/36
`picked.json` repointed for D312 (Sonarr's three picks now in `series-background` and `series-index`; old file
`picked-before-D312.json`), run 5's files `retest/*-run5.md`, re-cut 243K characters. Fresh Sonnet reader ~185K (read all, no
lines cut), fresh Sonnet grader: 35/36 (Sonarr 12/12, Kavita 12/12, nopCommerce 11/12). The one wrong is again nopCommerce edit
product Q3, the cyclic required-products check: run 4's grader counted it wrong, run 5's right, run 6's wrong (a grader
judgement, see run 4). Bar 34 and at least 31 met.

## D313 built (2026-11-14): "start here" lines, small-to-large order, no empty words line, a legend
Code only (`PageChart.StartOf`/`StartLines`, `KindRank`, `Legend`; `AreaPages.Render`), developer page only, no flows contract
change. Each index line gets `start here:` (frontend line or C# method → endpoint → the action it lands on, from the flow file's
own `http` steps) and `changes:` (first three changes, outside services, events); start flows go by kind, small to large by
chart nodes, too large last, in the index, the body and the can't-see list; the empty "layer 4 words go below" line is gone (on
a page off the switch both lines are written only around words); one legend line. Tests 2349 → 2352 (+3, two Areas tests
changed). `areas check` then `pages verify` on Sonarr 66/66, Kavita 28/28, nopCommerce 142/142; a rerun writes nothing; every
flow and shared part is byte-identical to before, only moved. Requests: Sonarr 122 of 128 frontend flows all matched (6 have a
❓), Kavita 154 of 169 (15), nopCommerce 308 of 315 (7 server screens with no route in their name); every matched one names
its action. Pages grow ~1.5–3K: Add New 19.8K → 21.3K, Reading Lists 32.9K → 35.1K, Series 140.1K → 143.3K, › Details
202.4K → 204.9K. Gate 3 not rerun here.

## Gate 3 run 5 (2026-11-14, after D313): not passed, closer
Fresh Sonnet developer (~109K tokens), run 4's prompt, report `layer4-build/gate3-run5.md`. Add New (21K, read whole) yes;
Kavita Reading Lists (35K, 33K read) yes ("mostly"), the first yes there; Sonarr Series parent (143K, 30K read) partly; Series ›
Details (205K, 28K read) yes for monitoring, partly for the rest; the pair partly ("I can find the endpoints and controller
lines for all of edit, delete, season/episode/series monitor, and bulk monitor within a couple of minutes"). Helped most: the
start-here lines. In the way: parent and Details don't point to each other; delete's after-handlers cut with an ellipsis, as
ids; "queues MoveSeriesCommand" on the Monitored toggle without its condition; K1–K8 repeated in every flow; Grab Release and
shared work bulk. Fixes: D314.

## D314 built (2026-11-14): parts linked both ways, every receiver named, conditional changes, field checks once
Code only (`PageChart`: `Parts`/`StartNames`, `Receivers`, `OnlyIf`, `SameChecks`/`Anchor`; `PageGate`: a sub-bullet's step from
the line above it), developer page only, no flows contract change; `Build` takes the layout (areas check, `pages chart`, gate 1).
(1) A split area's page lists its parts with their start flows by readable name; each part says "Part of" its parent, with its
siblings; links relative, all resolving (Sonarr 13,800 `.md` links, Kavita 883, nopCommerce 19,986, none broken; 55 anchors,
none broken). (2) Every receiver by class and method with its id linked; over 12, one sub-bullet each (Sonarr's delete: all 18,
before 18 bare ids). (3) `changes:` says "(only when …, `file:line`)" for a change under a condition (Monitored: "queues
MoveSeriesCommand (only when `queueMove`, `SeriesService.cs:336`)"). (4) A whole K set of two rows or more, the same as an
earlier flow's, is one "same checks as" row: Sonarr 26 flows, Kavita 4, nopCommerce 25. Tests 2352 → 2355. `areas check` then
`pages verify`: Sonarr 66/66, Kavita 28/28, nopCommerce 142/142; a rerun writes nothing. **Flow parts changed** (gate 2 cuts
them): Sonarr 164 of 309, Kavita 20 of 179, nopCommerce 417 of 584, every one only in its receiver lines and/or K rows (Sonarr
138 receivers, 23 both, 3 K only; Kavita 16 receivers, 4 K; nopCommerce 392 receivers, 25 both); nothing else in a part changed.
Pages: Add New 21.3K → 22.2K, Reading Lists 35.1K → 35.2K, Series 143.3K → 161.5K, › Details 204.9K → 246.3K (named, linked
receivers outweigh the K rows saved). Gate 3 not rerun here.


## Gate 3 run 6 (2026-11-14, after D314): not passed; the verdict swings between readers
Fresh Sonnet developer (~100K tokens), run 4's prompt, report `layer4-build/gate3-run6.md`. Add New (22K, read whole) yes;
Kavita Reading Lists (35K, about 17K read) partly, a yes in run 5 on a page that changed by 0.1K; Series parent (161K, 22K
read) partly; Series › Details (246K, 21K read) partly; the pair "partly, leaning yes to start". Named in the way: Kavita's
create, delete and add-to-list are on other areas' pages and the page doesn't say so; removeItem's validators and naming-regex
stop; Series parent half grab-release and filter flows; delete doesn't show whether files go; Details' ~600 lines of shared work.

Verdicts so far (one fresh Sonnet reader per run):

| Run | Add New | Kavita Reading Lists | Sonarr Series |
|---|---|---|---|
| 2 | yes | partly | partly |
| 3 | yes | partly | no |
| 4 | partly | partly | partly (parent + Details) |
| 5 | yes | yes | partly |
| 6 | yes | partly | partly, leaning yes |

One reader's verdict on the same page moves by a step between runs (Add New run 4, Kavita runs 5 → 6), so "yes on all 3 from
one reader" is partly a draw. Asked of the owner (`questions.md`): how gate 3 is judged from here.

## Gate 2 run 7 (2026-11-14, after D314): passed, 35/36
Run 6's files `retest/*-run6.md`; re-cut 273K characters (up from 243K: every after-handler named). Fresh Sonnet reader ~215K
(read all), fresh Sonnet grader: 35/36 (Sonarr 12/12, Kavita 12/12, nopCommerce 11/12). The wrong is again nopCommerce edit
product Q3: the cyclic required-products check, and now also the field-rule refusals, because D314's "same checks as
click-save" points to a flow outside the one-flow cut (on the page the link resolves). If the cut is kept as the test, `cut.py`
should append the flow a "same checks as" line points to, as it already does for shared parts.

## Gate 3 run 7 (2026-11-14, D315: three readers, majority): not passed
Three fresh Sonnet developers (~95–113K tokens each), run 4's prompt, reports `layer4-build/gate3-run7-{a,b,c}.md`.

| Page | A | B | C | Result |
|---|---|---|---|---|
| Sonarr Series › Add New | yes | yes | yes | pass |
| Kavita Reading Lists | partly | partly | partly | fail |
| Sonarr Series (parent + Details) | partly, leaning yes | partly | partly to yes | fail |

All three: Kavita's create, delete and add-to-list are on no page they could find; Series parent half Grab Release; Details'
1,231-step screen flow and shared work. Traced: the screen-open walk takes 12 `useMutation` request declarations as sent on load
(PUT/DELETE series, DELETE episodeFile, POST manualimport …), so the screen flow carries other clicks' work. Fixes: D316.

## D316 built (2026-11-14, pages part)
Each page gains **Elsewhere** (`PageElsewhere.cs`, `docs/pages.md`): the controllers the area's start flows land on themselves,
their other endpoints no start flow of the area calls, each with the start flows of the app that do (by area, readable name,
linked; three, then `+N more`), endpoints with the same callers on one line, uncalled ones on one "no start flow calls:" line,
15 per list then `+N more`. Kavita Reading Lists: `ReadingListController` 18 of 27 not called here; `DELETE /api/ReadingList`
called by Customize "deleteList"; `POST /api/ReadingList/create` and the add-to-list endpoints are called by no start flow (they
run from `ActionService`'s menu callbacks, which the walk leaves for later, D306), so the D316 example "called by a click on
Series Detail" doesn't hold. Kavita removeItem's K rows checked: right, left. removeItem posts `delete-item`
(`reading-list.service.ts:103`), whose action `DeleteListItem(UpdateReadingListPosition dto)` (`ReadingListController.cs:172`)
binds that DTO; the rows are its `[Required]` fields; the name misled readers. Page sizes (kB): Sonarr Series › Add New 22.2K → 23.2K,
Kavita Reading Lists 35.2K → 40.7K, Sonarr Series 161.5K → 165.8K, Series › Details 246.3K → 208.1K. `pages verify`: Sonarr 66,
Kavita 28, nopCommerce 142 areas pass; a second `areas check` writes nothing.

## Gate 2 run 8 (2026-11-14, after D316): passed, 35/36
Run 7's files `retest/*-run7.md`; `picked.json` folders still match; re-cut 275K characters. `cut.py` now also appends the rows
a "same checks as" line points to (run 7's note), one case: nopCommerce edit product, K1–K4 of `click-save`. Fresh Sonnet
reader 214K tokens (read all), fresh Sonnet grader 91K: 35/36 (Sonarr 12/12, Kavita 12/12, nopCommerce 11/12). The field-rule
refusals are now answered. The one wrong is nopCommerce edit product Q3 for the third run: the cyclic required-products check
(`ProductController.cs:1257–1271`). The page doesn't carry it, and it is not a stop: the action puts back the previous
`RequiredProductIds` and saves anyway, so it is a changed save under a condition that the key counts as a refusal. Left as
found (D317). Before the run: `dotnet test` 2359/2359; `flows check` no problems and `pages verify` nothing missing on Sonarr
(66 areas), Kavita (28), nopCommerce (142); no source file newer than the pages.

## Gate 3 run 8 (2026-11-14, after D316): not passed, same two pages
Three fresh Sonnet developers (101K, 99K, 111K tokens), run 7's prompt and pages, reports `layer4-build/gate3-run8-{a,b,c}.md`.

| Page | A | B | C | Result |
|---|---|---|---|---|
| Sonarr Series › Add New | yes | yes | yes | pass |
| Kavita Reading Lists | partly | partly | partly, leaning yes | fail |
| Sonarr Series (parent + Details) | partly | partly | partly, leaning yes | fail |

No reader names wrong content any more, and none names run 7's cause (the 1,231-step screen flow is gone). All three name
the same thing on the pair: the click summaries at the top answer the task (Edit Save and the Monitored toggle both go through
`PUT /api/v5/Series/{id}`, `SeriesController.cs:205`, branches at `SeriesService.cs:237` and `:336`; Delete at `:244`), and they
are 5–15% of about 370K characters, under Grab Release, filters, screen-load detail and shared work; "an index, not a
read-through". On Kavita: generic flow names (`save`, `orderUpdated`), the unresolved `POST /api/{*}` on `updateFilter`, and
D316's "Elsewhere" read as long and mostly about other areas (A, B, C), while create and add-to-list sit only under "no start
flow calls" (B). Each reader's top change is the same: a short "start here" block per user action at the top, the rest
collapsed or linked. **Not fixed by another rule: the owner agreed before this run (2026-11-14) that a failure here stops the
fix-and-rerun loop (D317); the page's shape and gate 3's method are reconsidered as one change (open question Q-L4 in `questions.md`).**

## Gate 3 redefined as a task test (2026-11-14, D318)
The owner: the page is for an agent in the later layers deciding how to change code. Gate 3 is now nine change tickets (three
per repo, clicks picked by code with seed 318 from areas no earlier gate run read), answered from the area's page alone in
three parts (start, change point, reach), marked against a key a fresh agent wrote from source. Bar: at least 25 of 27 right
and every start right. Tickets, method, marking and bar are frozen in `layer4-build/gate3-task/tasks.md`; picks in
`gate3-task/picked.json`. The owner saw the tickets and asked for Opus 5.5 instead of Sonnet for all three agents.

## Gate 3 task test, run 1 (2026-11-14, D318, Opus 5.5): not passed, 17 of 27
Key writer 189K tokens (source only), reader 175K (pages only, about 220K characters read of 3.9M, 2–25% of a page), grader
117K. Files `layer4-build/gate3-task/{key,answers-run1,grades-run1}.md`. Bar: 25 of 27 and every start right.

| Part | Right | Split | Wrong |
|---|---|---|---|
| A. Start (9) | 8 | 1 | 0 |
| B. Change point (9) | 6 | 2 | 1 |
| C. Reach (9) | 3 | 0 | 6 |
| All (27) | 17 | 3 | 7 |

By repo: Sonarr 4 of 9, Kavita 7, nopCommerce 6. The page routes an agent to the start and mostly to the place to change
(14 of 18); it does not say who else depends on that place (3 of 9). Causes, checked on the pages by grep after grading:
1. **Reach from code no UI action starts is on no page** (1C, 2C, 3C; 6C and 9C are the same kind). Sonarr's API v3
   controllers call the same `ProviderFactory.Delete` and `ManualImportService.ReprocessItem`; neither page mentions v3 once.
   The page is built forward from the area's start flows, and "Elsewhere" lists only the other endpoints of the controllers
   those flows land on. "Who else runs this method" is the code graph's impact answer (D88), a different command the reader
   was not given. Kavita's cover reset (same endpoint, empty file name) and nopCommerce's default and shared wishlists (same
   model factory) are other inputs to the same code, which a forward walk from one click doesn't set side by side.
2. **A save the code can't make on that path, stated flatly** (2C, 3B, 3C). The Interactive Import re-check lists "saves
   EpisodeFiles" under Changes; the condition `episodeFile.Id != 0` (`UpdateMediaInfoService.cs:89`) appears only lower
   down, and on this path the file is always new, Id 0 (`LocalEpisode.cs:91–110`), which a walk over code can't know. The
   grader confirmed from source that nothing is written to episode files on re-check. **Ticket 3 was worded from that line
   of the page, so the ticket itself is wrong**: two of the seven wrongs come from a false premise the page gave the ticket
   writer (the assistant). It is counted, not struck, because the page misled a reader the same way on ticket 2.
3. **Generic names for cshtml buttons** (9A, split): "cshtml form 3" doesn't say which wishlist button; the reader found two.
4. Smaller: 1B split (the page gives the shared base lines, the fix belongs in a download-client override); 6B split (the
   page says the catch at `UploadController.cs:553` "returns BadRequest(…)" with the message cut, the reader took it for a
   clear message; it is a generic one); 7C wrong on nopCommerce's entity event that clears the type cache, which D299 ruled
   out of gate 2's key (the key writer didn't have that ruling) and on the skip for disabled types.
One key correction, minor: the re-check can insert a default NamingConfig row once (`NamingConfigService.cs:22–28`); the key's
other disputed points held. Nothing is fixed from this run (D317): the first cause is a question of what the page is for,
asked of the owner (Q-L5 in `questions.md`).

## Checked by code after run 1: the impact answer names the callers the page missed (2026-11-14, free)
`stangent graph impact --symbol <the key's change point>` in each clone, the owner's go-ahead ("Yes, check it"), no AI:

| Wrong | Symbol asked | What the impact answer names | Covers the miss? |
|---|---|---|---|
| 1C | `ProviderFactory.Delete` | 16 endpoints, among them `DELETE /api/v3/DownloadClient/bulk`, both single deletes, and the indexer, import list, metadata and notification deletes that share it; 22 event handlers | yes |
| 2C, 3C | `ManualImportService.ReprocessItem` | `POST /api/v3/ManualImport` beside the v5 one; both controllers by name | yes (v3; 3C's other miss comes from ticket 3's false premise) |
| 9C | `ShoppingCartModelFactory.PrepareWishlistModelAsync` | `ShoppingCartController.Wishlist`, `UpdateWishlist`, `AddItemsToCartFromWishlist` | mostly: the re-render callers yes; default and shared wishlists are the same action with other inputs, not told apart |
| 6C | `UploadController.UploadVolumeCoverImageFromUrl` | its three frontend callers, among them `CoverChooserConfigFactoryService.forVolume` | partly: the other caller is named, not that it is a reset sent with an empty file name |

So the gap is not in layer 2: the graph already holds these callers, and a forward page was never where they'd be. Not
graded (the assistant's reading of four outputs), and each was asked with the right change point, which the page alone gave
on 6 of 9 tickets. 7C (an event's cache consumer, ruled out by D299) was not asked.

## Gate 3 task test, run 2 (2026-11-15, D319, Opus 5.5): not passed, 21 of 27
The page plus `graph impact` on the change point the reader picks; ticket 3 replaced (Settings › Connect, Save, worded by the
key writer from source). Key update 118K tokens, reader 166K (about 143K characters read; 2–6 symbols asked per ticket),
grader ~120K. Files `gate3-task/{key,answers-run2,grades-run2}.md`; run 1's key is `key-run1.md`.

| Part | Run 1, page alone | Run 2, page + impact |
|---|---|---|
| A. Start | 8 right, 1 split | 8 right, 1 split |
| B. Change point | 6 right, 2 split, 1 wrong | 8 right, 1 split |
| C. Reach | 3 right, 6 wrong | 5 right, 1 split, 3 wrong |
| All (27) | 17 | 21 |

Bar 25 of 27 and every start right: not met either way. Ticket 3 differs between the runs, so the two columns compare eight
tickets strictly. By repo, run 2: Sonarr 6, Kavita 8, nopCommerce 7. What is left:
1. **The false save again** (2C): the reader repeats the page's "saves EpisodeFiles"; the page's Changes line states it
   flatly and the walk can't know the file's Id is always 0 there.
2. **Razor buttons** (9A split, 9C split): "cshtml form 3" doesn't say which wishlist button, and the request's `list`
   parameter, which is what makes it a custom wishlist, is on neither tool.
3. **Consequences only the code's meaning gives** (3B split, 3C wrong; 6C wrong): that unticking every trigger is today the
   only way to turn a connection off, that a shared validator would also block Test, that a cover reset is the same
   endpoint with an empty file name. The reader said itself that neither tool shows them. The key writer saw them by
   reading the source.
4. Every ticket, by the reader's own report: the page gives `file:line` and no method name, the impact answer gives names
   and no lines, so joining the two means guessing names (6 of 23 symbols asked were not found).
Key corrections, both small: 8C's post-import log count is not a must; ticket 4 has a second sender the key lacked (the
answer had it).

**What the two runs say together, and its limits.** The key writer is in effect a source-only agent on the same tickets
(with the UI entry given): 189K tokens for nine, and three small corrections from two graders. The page-plus-impact reader
spent 166K and got 21. So on these nine tickets, on repos of this size, the two tools did not beat reading the source, in
right answers or in tokens. Not measured: the key writer was never marked blind like the readers; nothing here says how
either scales on a larger repo. Nothing is fixed from this run (D317, D319: one run). The question it leaves is what a
later layer will really be given, asked of the owner (Q-L6 in `questions.md`).

## Parked (2026-11-15, D323)
Layer 4 is parked by the owner, not cut (D138 set aside): code and tests kept, `components.pages` off, no further work. Gate 3 not passed (task test 17 and 21 of 27). Comes back only on one of D323's four tests: `pages summary` items as D322's checklist; the task test on a much larger repo; the owner on a real ticket; page diffs in layer 6. Layer 5 no longer reads pages (D322) after a source-only agent beat the old page-based linking (D321).
