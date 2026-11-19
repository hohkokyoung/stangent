---
phase: tm
name: truth map
status: done
consumes: { store: 1, cli-conventions: 4, code-graph: 7, flows: 6, source-document: 4, ticket-bundle: 3, bug-proof: 2 }
provides: { rulings: 2, truth-map: 2 }
---

# The truth map

**Designed 2026-11-16 (D361–D364); plan and my calls D365; every sentence the code follows covered too (D366).** **Layers 0 and 1 built and fresh-checked 2026-11-16 (D367, D368); layer 2 2026-11-17 (D369); layer 3 (D370), 2b (D371), 4 (D372) and 5 (D374) built 2026-11-17.** **Measured 2026-11-18 (D406–D408) and closed 2026-11-19 (D409): [end-of-phase report](#end-of-phase-report).** Also reads `fsd-review` v1 and
`inventory` v1, which layer 5 provides with no phase file of its own, so they aren't in `consumes` above.

The judge of a ticket is the truth map, not the FSD alone (D361). The FSD review (D341) finds where the FSD and
the code differ, and where the code does what the FSD says (D366). The PM confirms each section's matches once and
rules on each difference once, up front, by FSD section: **code right** (the FSD gets
updated), **FSD right** (the code has a bug), **restated** in the PM's own words, and for code the FSD never
mentions, **keep** or **remove**. `ticket prove` then says whether a ticket is **backed**, **contradicted** or
**unclear**. It writes a test only when the ticket is backed. Otherwise the questions draft asks in Jira, and the
answer comes back as a ruling.

Example (D361): the FSD says a refund sends an email, the code sends none, and the PM ruled "code right". A ticket
saying "refund sends no email" is contradicted, so no test demands the email, and the draft quotes the ruling.

## What's built where

```
stangent fsd review …             + a fourth state, match: the FSD quote and the line that does it (D366)  AI + code
stangent fsd links <doc>          each review finding's link: flow, else screen, else node, else file;     code
                                  the share linked by fallback printed (D362)
stangent fsd rulings <doc>        drafts or refreshes specs/fsd/<doc>/rulings.yaml from review.json:      code
                                  carries rulings over, blanks what changed, flags what its link changed
stangent fsd rulings-check <doc>  every value known, restated has a statement, who and when from git;    code
                                  counts blank, flagged, not found again, by fallback; then writes
                                  specs/fsd/<doc>/truth-map.json (agents) and truth-map.md (the PM)
stangent ticket bundle <KEY>      + the truth map's entries for the ticket, with their FSD section text     code
stangent ticket prove <KEY>       the reproducer first gives a verdict, citing an entry; code checks it;   AI + code
                                  only "backed" goes on to a test
stangent ticket draft --kind questions   asks the open point, ending "Reply with a line starting Ruling:" AI + code
stangent ticket pull <KEY>        reads Ruling: replies, writes the drafted entry into rulings.yaml        code
                                  (uncommitted); a person commits it
```

## Layers, in order (each with tests, then a narrow fresh check before the next, D158)

### Layer 0 · Matches in the FSD review (D366; `fsd-review-output`, `fsd-review` v1 → v2)

- The reviewer's instructions and output schema gain `match`: a sentence the code does as the FSD says, with
  `section`, `fsdQuote` and `evidence` (`file:line` where the code does it). It is checked like a gap or conflict: the
  quote word for word in its section, every line real. Merge joins matches by section and quote; `review.md` lists
  them per section after the differences.
- A sentence can be one finding only: a quote that is both a match and a gap or conflict in one run is an error that
  gets the retry.
- Tested with fakes. The saved Kavita runs have no matches, so a live run is needed to see real ones (paid, asked
  first, after 2026-11-18).

### Layer 1 · Links (D362 (1)–(4))

- A linker in `src/Stangent.Flows` (it needs flows, the inventory and the graph): per finding, each evidence line
  `path:line` gets the most specific thing that fits:
  1. **flow**: the line is a flow step, or inside a method a flow walks (as `flow-anchor/count.py` counts);
  2. **screen or shared part** from the inventory: the line is an inventory line, or its file holds inventory lines
     of one screen or part (read-only use of `inventory` v1's groups; not D337's checklist);
  3. **graph node**: the smallest non-file node whose span holds the line;
  4. **file**.
  The finding's link is its best line's link; ties go to the first evidence line. All lines' links are kept too
  (for matching and flagging).
- **App frame (D362 (3))**: the inventory gets one group, "App frame (on every screen)", for what the app's root
  component draws or opens, found by a general rule: Angular's bootstrap component (`bootstrapApplication(X` or
  `bootstrap: [X]`), React's root render (`createRoot(...).render(<X` / `ReactDOM.render(<X`), the Razor layout
  (`_Layout.cshtml` and `Layout =`). Its lines are the root component's template and what it uses, minus routed
  pages. They move out of "Not placed" or the shared parts into the new group. This changes `inventory` v1 → v2. No
  built consumer reads it (layer 5's link step isn't built), so nothing goes `needs review`.
- **The fallback share (D362 (4))**: printed by `fsd links` and `fsd rulings-check`, e.g. "51 findings: flow 12,
  screen 34, node 5, file 0 (fallback 10%)".
- Free check: Kavita's 51 saved findings should come out at about 12 / 34 / 5 / 0 (D361's part 3), and the App frame
  group should hold the 6 groups and 86 lines D362 counts.

### Layer 2 · `rulings.yaml` (D363)

**Built (D369):** as below, with D369's calls: a section counts as changed by its own text, and an entry whose quote is gone
from its section's text drops too; a match may be confirmed one by one as well as per section; blank entries no run
finds again are dropped and listed.

- `specs/fsd/<doc>/rulings.yaml`, contract `rulings` v1 (schema `rulings.v1.json`), grouped by FSD section. Each
  section has `matches: confirmed` (blank until the PM sets it; a match added later shows as new until it is
  confirmed again) and an entry per difference, then the undocumented batch. A match the PM rejects gets
  `ruling: not-a-match` and counts as unclear (D366). Each match gets a **confidence** by code (D366 (4)): high
  (every run found it, and its line is on a flow step or a screen's control), medium (one of those), low (neither).
  `rulings-check` and `truth-map.md` list medium and low matches first, for the PM to verify at setup:
  ```yaml
  - key: { section: "sec-0007", quote: "A refund sends the customer an email." }   # gap / conflict
    state: conflict
    section: "4.2 Refunds"
    link: { kind: flow, id: "refund-submit", hash: "…" }
    evidence: ["Api/RefundService.cs:88"]
    note: "The code sends no email."           # the review's words, for the PM
    ruling: code-right                         # blank until the PM fills it
    statement:                                 # required for restated; optional otherwise
    pmNote:
  - key: { section: null, link: "screen:/settings/preferences" }   # undocumented
    ...
  ```
- **Keys (D363):** a gap or conflict by the section's permanent id and its quote (normalized as the review's quote
  check does); undocumented by its D362 link (with its section when it has one). On a new review run the same key
  keeps its ruling; a new finding gets a blank entry; an old entry no run finds again stays, marked
  `notFound: <review commit>`, and is listed; a section whose text changed drops its rulings to blank, unless the
  new text holds the PM's `restated` statement, which closes the entry as `inFsd: true`.
- **Flags (D362 (2)):** each entry keeps its link's `hash` (the flow's JSON, the screen's lines, the node's code,
  the file). When `fsd rulings` finds a different hash on a ruled entry it writes
  `flagged: "link changed since ruled"` and the new hash. The PM deletes the `flagged` line once they have looked;
  git shows who did.
- **Checks (`fsd rulings-check`):** every value is one of code-right, fsd-right, restated (gaps and conflicts) or
  keep, remove, restated (undocumented); restated needs a `statement`; nothing hand-added without a key. Who ruled
  and when come from `git blame` on the `ruling` line, never from the file. Output: counts per state (blank, ruled,
  flagged, not found again), by section, and the fallback share.
- **`truth-map.json` and `truth-map.md` (D366, contract `truth-map` v1)**, written by code from `review.json`,
  `rulings.yaml`, the links and the flows, byte for byte the same on a rebuild. Per section: the FSD text; each
  sentence's state (match, gap, conflict) and code link; the linked flow's actor, steps, conditions and scenarios,
  with its 🟡/❓ count; the ruling, its statement and who made it (git); and how sure the entry is (found by every
  run or one; linked by flow or by fallback). Then the undocumented batch. The JSON is what agents read; the md is
  the same, for the PM.

### Layer 2b · The PM rules by telling Claude (D371)

**Built and fresh-checked (D371).** `--map <id>` refuses numbers from a rebuilt map; every answer is checked against `rulings-check`'s checks before it is written. `PmAnswers.cs`, `plugin/commands/rule.md`; Kavita: 42 numbered items, answers write
only their lines.

- `truth-map.md`: every open item numbered, a plain question with plain answers, low and medium matches first; no YAML.
  `truth-map.json` records each item's `n` (and a section's `matches.n`); `fsd rule` reads a number back to its key there.
- `stangent fsd rule <doc> <n> <answer> [--words "…"] [--note "…"]`: `yes`/`no` (a match, a section's matches),
  `code`/`fsd` (a difference), `keep`/`remove` (undocumented code), `--words` for restated. Writes `rulings.yaml`
  keeping every other line, prints the item back in plain words, never commits, doesn't rebuild the map (numbers hold
  until `rulings-check`); refuses an answer that doesn't fit the item and an item a refresh removed or changed in kind.
- `plugin/commands/rule.md` (`/stangent:rule`): Claude turns the PM's words into `fsd rule` calls, asks back on
  anything that could mean two things, reads each back, offers to commit.
- `rulings.yaml`'s header: ask Claude or run `fsd rule`; hand edits still read.
- No contract bump: `truth-map` v1 gains `n` (additive, nothing reads it yet); `rulings` v1 unchanged.

### Layer 3 · Email templates in the graph (D362 (5); `code-graph` v6 → v7)

**Built and fresh-checked (D370):** the `template-files` detector; a whole string literal in the class's members counts as a
name too (Kavita's `GetEmailBody("base")`); only in a template folder (two or more names match there, or the pattern names
it); Kavita 22 lines, 1 blind spot; Sonarr and nopCommerce 0.

- A string constant whose value names a template file in the project (Kavita: `EmailConfirmTemplate =
  "EmailConfirm"` with `TemplatePath = "{0}.html"` → `EmailTemplates/EmailConfirm.html`) gets a link
  `sends-template` from the constant to a new `template-file` node; 🟡 when the name is built at runtime. A
  matching rule, not Kavita's names: a constant's value equals a file's base name under a folder that the same class
  formats a path into.
- Level 1 (a finding on a template file links by file) already works after layer 1. This adds the code that sends it,
  so a ticket's starting point in `EmailService` reaches the template's rulings.
- D20: bump `code-graph` to v7, mark its consumers `needs review` in `roadmap.md`, `phases check` clean.

### Layer 4 · The bundle, the verdict, the question (D361, D364)

**Built and fresh-checked (D372).** Behind `truth-verdict` (off by default). `TruthList.cs`; `ticket-bundle` v3,
`bug-proof` v2, `bug-reproducer-output` v2 (v1 stays what the reproducer reads with it off); the PM's question is
appended by code, so `ticket-update` stays v1; `fsd.pm` isn't built (the question names no one).

- **The bundle:** a new `truth` list, read from `truth-map.json`. These are the entries that apply to the ticket: an entry applies when one of its
  evidence files is a ticket file (a starting point or a found file), or its screen is one of the ticket's pages.
  Each comes with its ruling, statement and state, and the full text of its FSD section. `TicketBundle.Markdown`
  prints the section text, not only titles (D358's finding). The old `sections` from the frozen `trace.yaml` stay
  as they are (Sonarr only). `ticket-bundle` v2 → v3; Phase 4's `fix` reads the bundle, so it goes `needs review`.
- **The verdict, in the reproducer's same call:** `bug-reproducer-output` gets `verdict`: `backed`,
  `contradicted` or `unclear`, with `entry` (a key from the bundle's `truth` list, or a section id) and `quote` (word
  for word from that entry's statement, the section's text or the review's note). Code checks:
  - the entry or section is in the input and the quote is in it;
  - code makes it **unclear** whatever the AI said when the cited entry is blank, flagged, not found again, or when
    nothing is cited, or when it is an unconfirmed match of medium or low confidence (D366 (4)); an unconfirmed
    high one is allowed, and the verdict says the PM hasn't confirmed it;
  - **contradicted** writes no test; **backed** must come with the test as today.
  `bug-proof.json` records a stop as `noProof: contradicted | unclear` with the entry and quote (D106: no proof says
  why). This is `bug-proof` v1 → v2 and `bug-reproducer-output` v1 → v2.
- **The question:** `ticket draft --kind questions` sees the stop. It asks the open point, quoting the truth map and
  the ticket's own words, and ends "Reply with a line starting `Ruling:`" (D364). For an unconfirmed medium or low
  match it asks the PM to verify it (`fsd.pm`, the PM's Jira account, mentioned when set; named in plain text otherwise). It stays checked as today (D98).
  It records which entry key the question is about (or "new", with the ticket's link).

### Layer 5 · `Ruling:` replies (D364)

**Built and fresh-checked (D374; 5 defects and 7 risks, fixed or listed there).** `RulingReplies.cs` (tickets), `JiraRulings.cs` (spec); behind `truth-verdict`; no contract bump
(`rulings` v1 already had `jira`). My calls: the entry is the latest attempt's `verdict.entry`, trusted only while the
posted question is still the one that verdict writes; a drafted ruling backs no ticket until committed; "not covered" (or
words on a match) drafts an undocumented entry keyed `ticket:<KEY>` on the ticket's file and symbol points, which a
refresh keeps and never marks not found; one person's later reply replaces their earlier one, two people who disagree are
listed; `the code is right` on undocumented code means keep; `ticket pull` doesn't run `ticket prove` (paid): its output
says to commit, `rulings-check`, then prove again.

- `ticket pull` reads comments that come after the questions draft was posted and have a line starting `Ruling:`.
  `Ruling: code right`, `fsd right`, `keep` or `remove` gives that ruling; any other text after `Ruling:` becomes
  `restated` with the text as its statement. A reply that doesn't parse is listed, not guessed. The drafted entry is
  written into `rulings.yaml` uncommitted, with `jira: { key, comment, author, created }`. Pull's output says so, and
  that a person confirms it by committing. When the git committer and the Jira author differ, `rulings-check` shows
  both.
- Then `ticket prove` runs again against the new ruling.

## After the layers: D358's measurement, redone

`benchmark/tickets/kavita-fsd-rules/plan.md` is revised first, and frozen before any planting. A fresh planter plants
rules on Kavita and writes tickets that agree with a ruling or go against it. Code scores whether `ticket prove`
calls each one right, and whether a wrong ticket stops before any fix. Kavita needs its FSD imported, a merged
review, and rulings filled in. The rulings come from the planter's key, written by the planter, never by this session.
Paid; the cost is said and asked first (the planter is about $3–6, plus a `ticket prove` run per ticket).

## Cost

Building is free (no AI). Six narrow fresh checks on Sonnet, about 100–150K tokens each (0.6–0.9M in all); one also counts false matches on a sample, said
before each one. The measurement is paid and asked first.

## End-of-phase report

Written 2026-11-19, on `claude/fsd-source` at `8c8ccb9` plus this report's docs (not pushed; 362 commits ahead of `main`).

### 1. What was built

A PM rules once on what the FSD review found, and `ticket prove` judges each ticket against those rulings before it
writes any test. Example from the run: KAVR-1 asked for something the PM had ruled the code right on; `ticket prove`
called it contradicted, quoted the ruling, wrote no test, and `fix start` refused it.

| Layer | What it built | Commit | Fresh check (D158) |
|---|---|---|---|
| 0 · Matches in the review (D366, D367; `fsd-review` v2) | a fourth finding state, match, with its quote and line | `a54f5c9` | done, findings fixed (D367) |
| 1 · Links (D362, D368; `inventory` v2) | `fsd links`: each finding → flow, else screen, else node, else file; the App frame group | `167413f` | done (D368) |
| 2 · Rulings and the map (D363, D369; `rulings` v1, `truth-map` v1) | `fsd rulings`, `fsd rulings-check`, `rulings.yaml`, `truth-map.json` and `.md` | `f087a05` | done (D369) |
| 3 · Email templates (D370; `code-graph` v7) | `template-file` points and `sends-template` lines | `b19783f` | done (D370) |
| 2b · The PM rules through Claude (D371) | numbered plain questions, `fsd rule`, `/stangent:rule` | `a0a6990` | done (D371) |
| 4 · Bundle, verdict, question (D372; `ticket-bundle` v3, `bug-proof` v2) | the bundle's `truth`, the reproducer's verdict checked by code, the PM's question | `896dd8f` | done (D372) |
| 5 · `Ruling:` replies (D374) | `ticket pull` drafts replies into `rulings.yaml`, uncommitted; `rulings-check` shows the Jira author beside the committer | `59782c4` | done: 5 defects and 7 risks, fixed or listed (D374) |
| The verdict before the test check (D408) | with no test target, `ticket prove` still asks for a verdict; only backed then needs a test | `8c8ccb9` | ~95K tokens, no bugs |
| Entries through flows and screens (D410) | an entry also reaches a ticket through the flow its method is on, or that flow's screen (`via`, `reach`) | uncommitted | ~78K tokens: 3 defects, 2 minor, fixed |
| A kept feature works as the PM's words say (D411) | backed on a "keep" entry quoting four or more of its PM words isn't refused | uncommitted | ~75K tokens: 1 defect (too wide) and a short-quote gap, narrowed |

Phase fs then changed this phase's own contracts (`rulings` v2, `truth-map` v2, D394), and layer 5 fed it (the review
on D323's prompt, the left-out list as its own call, matches from that list: D401, D404, D405, `fsd-review` v3).
Tests: 1,982, all passing (`dotnet test`, run 2026-11-19 on `8c8ccb9`).

### 2. Benchmark results

D358's question, measured as D375 planned and D406 adapted: on Kavita's live truth map (D404's review, 117 entries:
gap 11, conflict 9, match 26, undocumented 71, and 118 after KAVR-3's reply added a `ticket:KAVR-3` entry; matches ruled by Claude blind, D405; the rest by a blind Opus planter),
does `ticket prove` judge nine planted tickets right, and does a wrong ticket stop before any fix? Scored by code
against the sealed key (`benchmark/results/2026-11-18-truth-map-tickets/`).

| | first run, as built (D407) | after D408 | bar |
|---|---|---|---|
| verdict right | 5 of 9 | **8 of 9** | 8 of 9, or every one with its entry in the bundle |
| wrong tickets that got a test | 0 of 4 | **0 of 4** (`fix start` refused all 4) | 0 |
| backed tickets reproduced | 2 of 3 | **2 of 3** | 2 of 3 |
| replies leading to the right verdict | 1 of 2 | **2 of 2** | 2 of 2 |
| file-finder misses (reported apart) | 1 (KAVR-8) | 1 (KAVR-8) | — |
| bar | doesn't | **works** | |
| cost | $1.36 | $0.29 more ($1.65 in all) | $20 cap |

**Caution:** D408 was built after seeing the first result and aimed at the failure it showed (three tickets got no
verdict because `ticket prove` looked for a test project first), so the second run is not blind to it. It is also
nine tickets on one repo. Both results stand side by side. No earlier report measured this: Phase 3's numbers (D109)
are about the proof, not the truth map.

### 3. Decisions made or changed

D361–D364 (the design), D365 (the plan), D366 (matches), D367–D374 (one per layer), D375 (the measurement plan), D406
(adapted to the live review), D407 (first result), D408 (verdict first), D409 (closing), D410 (KAVR-8's fix), D411 (a kept feature works as the PM's words say). Changed by others: D394 (phase
fs: `inFsd` gone, `writtenFrom` added), D401–D405 (layer 5: the review's prompt, the left-out list, matches back).

### 4. Known gaps

- **KAVR-8, the file finder's miss, is general (D409 (3)).** The finder named `Kavita.Database/Repositories/GenreRepository.cs`, which is right for the bug; the ruling
  that backs the ticket cites only the Browse Genres screen's template (`UI/Web/src/app/browse/browse-genres/browse-genres.component.html`).
  The bundle takes an entry only when one of its evidence files is a ticket file (D372 left screens out). On Kavita 115
  of 118 entries cite only frontend files, so any ticket whose finder lands on backend code misses nearly all of them.
  The screen's own flow (`screen-opens-browse-genres`) walks into `GetBrowseableGenre`, so the link is already there.
  **Fixed by D410 (2026-11-19):** an entry also reaches a ticket through the flow the ticket's own method is on, or the
  screen that flow opens; free check: the key's entry is in the bundle for 9 of 9 tickets. KAVR-8 re-proved ($0.38): its entry reaches
  the verdict, still unclear: the planter ruled it `keep` with PM words that say what the ticket expects, and `keep` is
  read as "the code is right as it is". **D411** reads "keep" with PM words as keeping the feature working as the words say; re-proved, KAVR-8
  is backed and reproduced: **9 of 9 verdicts right, 3 of 3 backed reproduced** (result.md's fourth section; both fixes
  built after seeing KAVR-8 fail). **The other eight re-proved under D410 and D411 (2026-11-19, $0.62): every verdict
  unchanged, 9 of 9**; the three backed ones reproduced on byte-identical tests the earlier runs left in the clone, so the
  verdicts are fresh and the reproductions only show those tests still fail (result.md's fifth section).
- **`tickets.app` isn't set for Kavita,** so a backed frontend bug can't get a Playwright test (D408's fix 2); set it
  when a ticket needs it.
- **A written ruling (`writtenFrom`, phase fs's `fsd write`) has never reached `fsd rule`, a bundle or a draft:** no
  live `fsd write` has run, and Kavita's map has none (every entry's `writtenFrom` is null).
- One planted ruling set, one repo, nine tickets; the matches were ruled by Claude standing in for the PM (D405).
- Both replies were replayed from files (`reply.py`, `tickets.replay`), not posted to a real Jira.

### 5. Impact on other phases (D20)

| Contract | Version | Consumers |
|---|---|---|
| `rulings` | 2 (tm's own, bumped by fs) | tm itself, now current |
| `truth-map` | 2 (tm's own, bumped by fs) | tm; Phase 3's bundle reads `truth-map.json` |
| `ticket-bundle` | 3 (D372) | Phase 4 (`needs review`) |
| `bug-proof` | 2 (D372) | 2c (cut), Phase 4 (`needs review`) |

This phase now consumes `cli-conventions` v4 and `source-document` v4: the run used the verdict with no switch (D381)
and read every FSD section from the migrated clone's committed chapters (its `document.json` has no `sections`).
Phase 3's two items the run exercised are cleared on the roadmap (D409 (2)); its written-ruling item stays. The run
also exercised Phase 4's item ("the fix loop takes a stopped proof as no proof": `fix start` refused all four wrong
tickets); that is left for Phase 4's own review. `phases check` shows tm current after this report.

### 6. Follow-ups

- Re-prove the other eight tickets under D410 and D411 (paid, about $1), or better, a blind second measurement on another repo.
- `tickets.app` for Kavita when a backed frontend bug needs a Playwright test.
- The first live `fsd write`, then `fsd rule` on a written entry and a bundle and draft that carry it (tm, Phase 3).
- A second measurement on another repo, blind to D408, before trusting 8 of 9 widely.
