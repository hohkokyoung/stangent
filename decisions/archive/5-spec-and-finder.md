# Decisions D99–D111: the structured spec, the file finder and closing Phase 3

Full entries, word for word. One line each in [`decisions.md`](../decisions.md).

## D99 · The FSD becomes a structured spec, built from word-for-word quotes; the BA approves each requirement — 2026-11-02 (decided; status corrected 2026-11-06, D147: extraction, requirement links and `ticket match` built, approval, reconcile and the other sources not)
The imported FSD (D52) stays the record, byte for byte. On top of it, an AI step builds a structured spec: features,
requirements (rule, validation, flow, data, permission, non-functional), acceptance criteria, journeys and data
definitions, each with a stable id (D11's original aim). Then requirement ↔ page statement ↔ code are linked.
- **Every item cites word-for-word FSD quotes** (section id + text); code checks each one, as for links (D53).
- **A fresh AI checks each item says no more than its quotes** (D61, like the agree-checker).
- **Ambiguities and clashes in the FSD become questions for the BA**; the AI never resolves them.
- **The BA approves each requirement**, in a spec-only PR (D11). Until then it is `proposed`: shown as context, never
  used to judge "bug or by design". Editing a requirement or its quoted FSD text drops it back to `proposed` (D11's hash).
- **Reference pages stay:** they are the "does" side that requirements ("should") are compared with, and the checked,
  stored reading of the code that linking would otherwise redo each time.
Reopens D52's "Rejected: one merged spec": the FSD isn't rewritten, a checked layer is built on it, and a person owns it.
**Why:** an agent can use a requirement with acceptance criteria; it can't reliably use a section of prose. The BA knows
the business logic, so only their approval makes a requirement the truth; quotes and the checker keep the AI honest
until then.
**Cost:** the BA's time, requirement by requirement (the largest cost); one AI pass per FSD section on import, re-run
when a section's text changes; a new contract beside `source-document` (D20: consumers marked `needs review` when it
lands); designed without a real FSD so far (D56). Doesn't raise right place (Sonarr has no FSD); aims at not-bugs
"proved" as bugs.
- *2026-11-02, owner:* designed and first measured on the planted Sonarr FSD (`benchmark/fsd/sonarr/`, 2b): tidier than
  a real FSD, so a pass there doesn't prove a real one; the real-FSD run (D56) stays owed. Its frozen answer key (30
  planted items) gives a ready bar: requirement-level links should find as many planted items as 2b's section-level
  links (27/30) with fewer false alarms (2b: 31.3%, bar ≤ 30%; 25 of 62 unplanted were agrees disputed because the
  page covered only part of the section, which requirement-level links address).
- *2026-11-02, owner — the spec's shape:*
  - **Items:** features (one per FSD section) holding requirements (id, kind, statement, source quotes, acceptance
    criteria, status, links); journeys (ordered steps, each pointing to requirements); scenarios per journey (happy,
    alternate and error paths); data definitions; and, only when the FSD says something about them: scope / out of
    scope, glossary, roles, states, messages and errors, outside systems (D83). Success criteria only when the FSD
    states them. Left out: background prose, revision history, layout and images (no quote), build hints.
  - **No bloat:** no quote, no item; one fact in one place (items refer to data, glossary and requirements by id;
    journeys and scenarios add no statements); no empty sections; **every FSD sentence is either cited by an item or
    marked `background`** (a code check; the BA reviews what was marked background).
  - **Scenarios combine requirements, never add any:** every step and outcome points to a requirement; an alternate or
    error path exists only if the FSD states it, otherwise it is a question for the BA. Tests carry scenario ids as
    well as requirement ids (D10), so coverage shows which scenarios have a test.
  - **Diagrams are drawn by code** (Mermaid) from journeys and states: a view, not a source, never written by the AI.
  - **Files:** `specs/requirements/<doc>/` with `index.yaml` (one line per item, read first), `glossary.yaml`, one file
    per feature (`F-<id>.yaml`) and `questions.yaml`; agents open only the features they need.
  - **Acceptance criteria may add example values** ("Firefly", "10 selected"), **never an outcome the FSD doesn't
    state**; an unstated outcome becomes a BA question (owner, 2026-11-02). The fresh checker treats an outcome with no
    quote behind it as "says more than its quotes".
  - **Big features (owner, 2026-11-02):** features follow the FSD's own section tree, split by code (a section with
    sub-sections becomes a parent feature listing its children; no size limit to tune). Agents read one item by id
    (a command like `stangent spec req <id>`, returning the item with its quotes, criteria and links), never a whole
    file. A section with many requirements and no sub-headings is flagged to the BA as a question: the BA adds
    headings to the FSD (the next import splits it), or approves AI-proposed sub-groups labelled as the AI's; the AI
    never splits silently. A compound sentence ("a series, a season, or a single episode") becomes several atomic
    requirements citing the same quote.
  - **Ids (owner, 2026-11-02):** plain numbers assigned once and never reused (`R-0042`, `F-0007`, likewise journeys,
    scenarios, data and questions); section and title are fields, never part of the id, so renumbering or renaming
    the FSD changes no id (D11: stable for the requirement's life). On re-import, an item whose quotes are unchanged
    keeps its id and status; a changed quote keeps the id and goes back to `proposed`; a removed quote retires the id.
    People read the title (`R-0042 · Can't add a series twice`).
  - **Reworded quotes in a new FSD version (owner, 2026-11-02):** code first matches by exact quote; for what's left
    (old items with no match, new text with no item) the AI proposes pairs ("R-0042 was reworded as …"); code shows
    the BA old and new text side by side; a confirmed pair keeps the id and goes back to `proposed`; a rejected pair
    retires the old id and the new text gets a new one. Treating every rewording as new was rejected: each typo fix
    would break test and link ids and force a fresh approval.
  - **Several documents and a reconcile command (owner, 2026-11-02):** each document (FSD, change request, addendum) is
    imported on its own with the effective date the document states (`fsd import … --effective <date>`, confirmed
    by the BA) and a status, draft or approved (D15: only an approved document supersedes; a newer draft's
    differences show as proposed changes); the same `--id` with a new file is a new version of that document, the old one stays in the archive. A requirement may cite
    quotes from several documents (current and superseded); a change request touches only what it mentions. After
    any import, `stangent fsd reconcile` (name not final) works out what changed, by code: unchanged items kept,
    removed quotes → ids to retire, new text → items to extract, reworded quotes → pairs for the AI to propose, and
    clashes between documents → the newest approved document proposed as the winner. The AI parts run as the usual prepare → AI → check
    loop (D60). Each result is an entry with an empty `decision` (in a reconcile file beside the spec, plus a Markdown
    view for the PR); **the BA confirms every clash**, reworded pair and retirement; `reconcile apply` then updates
    ids and statuses. Until confirmed, affected items stay `proposed` (context only), never blocking other work.
    **It runs automatically (owner, 2026-11-02):** when an import lands (a change under `specs/fsd/`), CI runs
    reconcile and its AI steps and opens a spec-only PR for the BA, as D55 does for stale pages (`plugin/ci/`).
  - **Jira tickets as sources (owner, 2026-11-02):** a change-request ticket (story or feature request; which issue
    types count is a setting) may be a requirement's source: the requirement quotes the pulled ticket word for word,
    code checks the quote, it is labelled with its key (`source: Jira SONB-41`) and the BA approves it like any other.
    When a later document covers it, reconcile pairs them and the document becomes the main source. **A bug report
    never creates a requirement:** its "expected" is the reporter's view, not an agreement (D96).
  - **No FSD: as-built drafts (owner, 2026-11-02):** with no document, an AI step turns reference page statements into
    draft requirements in the same shape, quoting the statement (`source: code (as built)`). They are context only and
    never judge "bug or by design" (drawn from the code, they always agree with it). The BA adopts or corrects them one
    at a time (no bulk approval for as-built drafts); an adopted one records "agreed by the BA from as-built"; a
    corrected one that the code now disagrees with is a bug.
  - **Testing without a BA (owner, 2026-11-02):** for benchmark runs only, until a testing project with a proper FSD is
    found, approval is skipped: a setting (off by default, never in a user's config) treats `proposed` items as
    approved, and every result made with it says so. **As-built drafts still never judge bugs in that mode** (owner):
    with approval skipped they would mark every reported behaviour "by design", so the judging score would only
    measure that loop. They are still scored for extraction, shape and links.
  - **How the BA approves before the Phase 8 dashboard (owner, 2026-11-02):** the spec-only PR's description lists each
    item as a tick box (title, quotes, acceptance criteria, any clash); the BA ticks what they approve, then approves the
    PR; on merge, code marks ticked items approved and leaves the rest `proposed`; `decided_by` comes from the PR's
    approver via git, never typed (as D58). One PR per feature for big batches. `stangent spec approve <id>` (name not
    final) stays as a backup for developers. Needs the BA to have repo access; otherwise approval waits for Phase 8.
  - **A confirmed bug the spec doesn't cover (owner, 2026-11-02):** refines "a bug report never creates a requirement".
    The report alone still doesn't, and nothing reaches the spec automatically. When a developer confirms a bug
    they also say whether it exposes a gap in the spec, default no: most bugs are code mistakes, and a question for each
    would bloat the spec and waste the BA's time. A yes opens a BA question quoting the ticket. The BA chooses: an
    acceptance criterion on a requirement, a scenario, a new requirement (source: the ticket), or no change, which is
    recorded. The proof's test then carries the new id. **Why:** otherwise each repeat is a person's call again.
    **Cost:** a developer can miss a real gap; the spec is then no worse than before.

- *Placed 2026-11-02 (D109, owner):* built before Phase 4, as phase 2c on the roadmap.
- *Matching a ticket to its requirement (owner, 2026-11-02, "yeah sure"):* code limits the candidates to requirements linked, through
  page statements, to the code at the ticket's starting points; no link means "no requirement covers this". An AI picks one or none,
  quoting both word for word, and code checks the quotes. A fresh AI, without seeing the pick, checks that the requirement says what
  should happen in this situation; if it disagrees there's no match. **A person always confirms:** until then it reads "suggested:
  R-0042", and only then "bug, per R-0042" or "looks intended (R-0042)", for an approved requirement only. Rejected: automatic when
  the checks agree (both AI steps can share a misreading, and there's no data on how often). May loosen later on measured numbers.
- *Scoring the match (owner, 2026-11-02, "lets go"):* only 7 of Sonarr's 50 tickets reach any FSD section, 1 of them a not-bug, and
  reaching one doesn't mean the ticket is about it (SONB-46 reaches only the Library chapter); the 27/30 bar scores requirements and
  links, not this step. So a fresh agent writes a key on the planted FSD before anything is built, frozen: tickets that clearly fit a
  requirement and tickets that clearly don't. **Cost:** it's our own test set, so a real FSD (D56) stays the judge.
- *A requirement no page statement covers (owner, 2026-11-02, "yes, labelled"):* it can still match, second after linked ones. The AI
  sees such requirements' titles and FSD sections; the same checks and a person's confirmation apply; it reads "no code found for
  this rule", so it says the feature may not be built, not that existing code is wrong. **Why:** a feature that was never built is
  what a spec is best at catching ("the episode list can be exported as a CSV file"), and links alone would say nothing covers it.
  **Cost:** more candidates, so more wrong picks; the key tests this case.
- *Bars for the match (owner, 2026-11-02: strict, since an agent acts on the business logic it's given):* on the frozen key of 30 tickets
  (15 a requirement with code covers, 5 one with no code, 10 none): at least 90% of suggestions name the right requirement; at least 8 of
  the 10 say "none"; at least 14 of the 20 find it. If either of the first two fails, matching ships off and requirements stay context; if
  only the third fails, it ships on and the report says so. Agreed before any run; not moved after one.
- *A fix that goes against an approved requirement (owner, 2026-11-02, "stop and ask"):* an agent trusts the spec, so a wrong approved rule
  gets implemented faithfully and every check passes. An approved requirement outranks a ticket: the match says "looks intended", no fix
  starts, and a rule that's wrong changes through the BA first (D11). A diff touching code linked to an approved requirement is flagged;
  code can tell it touched that code, not that it broke the rule, so a person judges. Details with Phase 4. **Cost:** no check can show a BA
  read carefully; D53's telemetry idea (approvals later reopened, time per approval) applies here too, as a follow-up.
- *Recheck triggers (owner, 2026-11-02, "as recommended"):* when a page goes stale (D55), its links are judged again, in 2c; a broken link stops
  saying `agree` and shows as a conflict for a person, and the BA's approval stands, since the FSD's text hasn't changed. A code PR listing the
  requirements it reaches with no test waits for tests that carry ids (Phases 5–6). No trigger after a failed proof: 8 of the 9 saved
  `not-reproduced` only ran out of turns, and a developer already flags a spec gap when confirming a bug. **Cost:** an AI step per merge
  that changes a page's links; small, not yet measured, reported.
- *Adoption replaces D14's audit (owner, 2026-11-03):* no separate audit. `/stangent:adopt`, with a driver script behind it as for tickets and
  pages (names not final), runs the existing steps in order: graph, pages, FSD import, extraction, links, spec-only PRs. Each skips what
  its inputs say is done, so it resumes, and the pages step asks before spending. The difference list appears only if 2c's links pass the noise
  bar and findings go on. After day one the triggers keep it current. **Why:** a separate audit would repeat steps needed anyway, and could
  drift from them. **Cost:** no single report; still the largest AI spend and a large BA backlog on day one.
- *Phase 3's follow-up when 2c lands (owner, 2026-11-03, "as recommended"; D20):* `ticket match` is its own step after the bundle; `ticket-bundle`
  v3 adds the linked requirements and keeps FSD sections; `bug-proof` v2 records the verdict, and `--confirm` also confirms the match; drafts may
  link a requirement, and "looks intended" asks the reporter. The finder doesn't change. Verdict: the requirement agrees with the reporter and a
  test fails → bug; it agrees with the code and a test fails → looks intended, no fix starts; no code found → may not be built; no match → a
  person decides, as today. Skipping the proof when a match says "intended" waits for numbers. **Cost:** saved drafts go stale; two contract
  bumps, with no consumer yet; one more AI step and its fresh check per ticket.
- *What "cited or background" means (found by a fresh check of the first build, 2026-11-03):* a loose check let a rule leave through either exit: `Orders
  over 50 need` covered "Orders over 50 need approval unless the customer is a VIP", and a rule marked background was seen by nobody. So: **cited** means the
  quotes of requirements, criteria and definitions together cover every word of the sentence, whole words only; journey and question quotes don't count, a
  piece under 4 words counts only as a whole sentence, and a table row must be quoted whole, since any cell could be the rule ("Refunds need a sign-off | ✓"). The
  fresh checker also rejects a quote added to an item it has nothing to do with, which code can't tell. **Background** is one sentence exactly as listed; the
  checker sees it and can say it needs an item, and the BA will see it in the approval list (step 4). A table's first row is a sentence, since Word doesn't say
  whether it's a header. **Cost:** more retries, and one more thing for the checker and the BA to read.
- *Ids on a re-run, until reconcile (same check):* items with the same quotes and words keep their id; reworded items keep it only when there's one old and one new
  item on those quotes. With several, a guess could swap two ids and carry links and approvals to the wrong rule, so they get new ids. The title and the words of
  definitions it uses are in the hash, and any answer from the checker but `ok` drops approval too. **Why it isn't finished:** D99 says unchanged quotes keep id and status, and also that editing a requirement
  drops it back, and an AI re-run rewords freely. Step 6 settles it at the root: code carries forward every item whose quotes are still in the text, and only new or
  changed sentences go to the AI (D18). Also step 6: a changed quote keeps its id, a renumbered section keeps its ids, and retiring waits for the BA.
- *Requirement-level links, first measured (2026-11-03, sonnet):* worse than 2b on both bars: 24/30 found (2b 27), false alarms 44%, range 29–59% (2b 31.3%), from a fresh agent's hand check of 20
  (`benchmark/results/2026-11-03-phase2c-sonarr-links-score.json`). All 20 planted gaps and conflicts were found; 6 of 7 undocumented behaviours weren't. 11 of the 12 false alarms are gaps where the code does what the FSD says
  and the summary page left it out, so the hope above, that a finer grain gives fewer false alarms, was wrong: a page saying less doesn't show the code lacks it. **Open, for the owner:** 56 of 109 requirements read "no code found", and only 10 of those
  are really unbuilt; proposed: "the page doesn't mention this", with gaps naming a page at the ticket's code in the main candidate list. Bigger option: check each gap against the code. Findings stay off (D68).
- *A clash between sections (same check, no owner decision yet):* D99 says clashes become BA questions, but one pass per section can't see one: on the planted FSD,
  R-0014 lists 3 things tags link to and R-0001 lists 6, both `confirmed`. Added to step 6 as one AI pass over a document's requirements, proposing questions.

## D100 · Held-out bugs come from bitwarden/server; Sonarr's 40 become the tuning set — 2026-11-02 (decided; set frozen)
Every Phase 3 change so far (the page index, the picker, the bundle, the free checks) was judged on Sonarr's 40 bugs,
so no half of them is unseen and a split of them would still flatter the result.
- **Sonarr's 40 bugs (D78) are the tuning set** from now on: changes may be tried and compared on them.
- **The held-out set is fresh bugs from bitwarden/server** (C#, graph already built, the D47 split check's repo),
  picked by code with D78's rules where they apply (closed GitHub bug reports with one merged server fix, non-test C#,
  at most 15 files), excluding the 5 issues D87 already measured. It is frozen and committed before anything is run on
  it, and measured only to judge a finished change, never to tune one.
- **Fallback:** if bitwarden has too few such bugs for the bars to mean anything, newer Sonarr bugs (after the frozen
  set) instead, reported as same-repo (unseen, but no test of generality).
**Why:** a measure that tuning has seen can't tell a real gain from fitting; a second repo also checks stangent isn't
shaped around Sonarr (the owner wants any C#/TS repo).
- *Frozen 2026-11-02:* `benchmark/tickets/bitwarden/heldout.json`, 24 bugs (`select.py --heldout`): of 281 closed bug
  reports, 62 have a linked merged server PR, 29 fit (non-test C#, ≤ 15 files, merged before the pinned commit), less
  the 5 D87 cases. Fixes merged 2020-06 to 2026. 24 is below Sonarr's 40, so ranges are wider; enough to tell a result
  near the 70% bar from one far from it, so no fallback. No not-bugs (bitwarden's are not selected).
- *Tickets through Jira (owner, 2026-11-02):* the 24 are copied into their own Jira project (`--copy-to-jira`, as
  Sonarr's SONB) and pulled from there, so the held-out run tests the pull too; local files that skip Jira were
  rejected (a benchmark-only path real use never takes). `freeze.py` lays `heldout.json` out as Sonarr's set is
  (text unchanged; authors and dates read from GitHub; answer key = the merge commit's own files, as the agent
  baseline's). Images are not downloaded: the finder reads text only (D101). (The pull drops link and image URLs from
  that text: phases/3-tickets.md, 2026-11-02.)
- *Measured 2026-11-02 (D101, phases/3-tickets.md):* right place 20 of 24 (83%, range 67–96%) ✅, $1.28; the agent
  baseline 23 of 24. Not tuned on: the gap goes to Sonarr first.
- *Owner, 2026-11-02:* no proof run on bitwarden. Only the finder has run there (D101, D102), so the proof's bars stay
  measured on Sonarr alone, the tuning set, and Phase 3's report says so.
**Cost:** bitwarden needs its own setup for what a run uses (tickets in Jira or replay files, reference pages for the
page-based sources); bitwarden's bugs may be fewer and different in kind; the owner decided "your call".

## D101 · A coding agent finds the ticket's files; code checks them; the graph is the checker and the map — 2026-11-02 (decided; built, committed)
Replaces D70's starting points and D87's page picker as the way into the code. The agent baseline (phases/3-tickets.md,
2026-11-02, fresh-checked): a headless coding agent with only the ticket and the code, Read/Grep/Glob, named a fix file
in 36 of 40 Sonarr bugs (ours: 19) and 23 of 24 held-out bitwarden bugs (D100; strict 20 of 21), median 4 files, about
$0.05 a ticket. Pages as the way in cap right place at 65% (D98's stage table), and a nearby page explains uncovered code
in 1 of 12 bugs.
- **The agent searches the repo** for each ticket, read-only, capped (D86's 30 turns / $1 as a start), and names files.
- **Code checks every file it names** (D18). *Settled 2026-11-02 (owner):* for each file the agent returns the path,
  a word-for-word quote from the ticket (the part this file explains) and a word-for-word line from the file (the
  code that connects to it); code checks the file exists at the commit worked on and both quotes match. A failed check
  gets one retry with the reason (D60); a file that still fails is listed as "named, not shown in code", not dropped.
- **The graph stops being a way in.** It checks what the agent named, expands from it (impact, D27), links across
  boundaries (HTTP, events, tables), picks tests (Phase 5) and splits work (Phase 4). Pages explain the named files
  where they cover them; a nearby page is at most labelled context.
- **Removed:** D70's code-only shortcut (a text match skipping the AI) and the page-title / page-index picker as the
  source of starting points. The page index (D87) may stay as a hint; to be decided in the design.
- **Misses** (SONB-1, 18, 21, 32; bitwarden #2506) are grouped by cause; only repeated causes get a fix, tried on Sonarr's
  40 and judged on bitwarden's 24.
- *Design settled 2026-11-02 (owner: "go with the most optimal option"; built, uncommitted):*
  - **Under the cap** (closes the open question after D71's fix): the starting points are never cut; the impact
    answer's item lists go first (counts kept; `stangent graph impact` lists them); then the lowest-ranked pages. A
    cut page that is a starting point keeps its citations as pointers; a cut page that only explains a file doesn't.
  - **Pages explain found files:** the written pages whose statements cite code in a found file, most files first;
    for a file no page cites, the pages whose slice holds it. No nearby page (1 of 12).
  - **Impact starts from the starting points:** the found files' code, symbol points, page points' citations.
  - **Code's signals stay as the fallback,** not a gate: with `tickets.aiPoints` false or `ai.enabled` false they are
    the starting points (no AI); with the finder on they are only the ticket's text the finder reads.
  - **The page index (D87)** is no longer an input to starting points; it stays for the benchmark's measures.
  - **The finder's input is the ticket only** (prose and text attachments), as in the baseline; it searches the repo
    at the commit checked out, read-only, never `.stangent/` or `.git/` (permission rules; tested 2026-11-02 that an
    allow rule on the repo bounds Grep and Glob too). Its cap: `tickets.findBudget` (30 turns, $1 a call).
  - **Contract:** `ticket-bundle` v2 (file points with `line` and `why`; pages' `explains`; `rejected`;
    `cap.impactTrimmed`, `cap.dropped`). D20: Phase 4 consumes it and hasn't started; Phase 3's own readers updated.
  - *After a fresh review (D61), same day:* the finder's answer is kept while the ticket is unchanged and marked
    `stale` when the code moves on (a new search would see the half-done fix); `--refind` searches again. It searches
    a throwaway git worktree of the commit (no `.stangent/`, pages, build output or untracked files), and code checks
    files against git (tracked at HEAD, exact case) and lines against HEAD's text. An import or namespace line is no
    evidence. Files that failed their checks are shown in the bundle, and a ticket with them isn't "only outside the
    code". A retry gets what's left of the cap. Pages that only explain a file are left out (counted) when cut; an
    unwritten page whose slice holds a file no written page explains is listed as missing.
  - **Measured on Sonarr (2026-11-02, phases/3-tickets.md):** right place **36 of 40 (90%) ✅**, was 19; no bundle over
    the cap (was 19 of 50); $2.68 for 50 tickets. D79's fallback "AI picks off by default" no longer applies: the
    finder is on by default (`tickets.aiPoints: true`). A private repo was owed; dropped by the owner 2026-11-02.
  - **Held out (bitwarden, D100, 2026-11-02):** right place **20 of 24 (83%, range 67–96%) ✅**, $1.28; the agent baseline
    23. The finder names fewer files (median 2 vs 4); 3 of its 4 misses the baseline reached lower in a longer list.
**Why:** measured, on a held-out repo: the agent finds what our own search can't, cheaply; building our own search would
need to close 47.5% → 90%.
**Cost:** an AI call on every ticket (about $0.05 on these repos); both repos are public, so the rate may lean on what
the model already knows (no private-repo check: dropped by the owner 2026-11-02); about half the hits come from tickets that name the file; `ticket-bundle` and the points
record change shape (D20: Phase 4 consumes the bundle and hasn't started, so no consumer to mark).

## D102 · A ticket Jira sends as HTML reads as the same Markdown as any other — 2026-11-02 (decided)
The pull asks Jira for Markdown; Jira sends HTML instead when an issue holds what Markdown can't (media, panels). The
pull flattened that HTML to plain text, losing link and image addresses, list numbers, task-list ticks and code marks:
BWB-3's fixing-PR link reached the finder as "PR up". 5 of 51 Sonarr tickets and 11 of 24 bitwarden ones came as HTML
(45 links, 121 code marks).
- **The pull turns HTML into the Markdown Jira would have sent** (owner, 2026-11-02): links `[text](url)` (a bare URL when
  the text is the URL), images `![alt](src)`, numbered lists keep their numbers, task items `- [x]`, inline code in
  backticks, code blocks fenced with their language, quotes `>`, headings `#`, bold/italic/strike, rules, tables as
  `|` rows; Jira's media blocks stay `[attachment: name]`. Our own code (Jira's HTML is a small, known set of tags), no
  new library; tests built from the saved real tickets.
- Rejected: only adding each link's URL after its text (code marks and numbers stay lost).
- *After a fresh review (D61), same day:* it lost no word or URL on the 93 real HTML bodies. Fixed: a list keeps
  children that aren't items (Jira nests task lists directly); tags are read in linear time; an unquoted URL ending in
  "/" isn't a self-closing tag; a tick doesn't eat a line break; tables keep captions and count only their own rows. The
  output is Jira's Markdown shape *without* its backslash escapes (better for code's own signals). Two readers learned
  Markdown marks, which Markdown tickets already needed: the quote check's whole-line rule (D53) ignores a line's
  quote, heading, bullet, number and tick marks; a label may be `_Label:_`. A quoted heading is never a label (a quoted
  error page mustn't end the part it is in). All 93 bodies' Markdown is pinned in `tests/fixtures/tickets/html/`.
- *Measured 2026-11-02 (phases/3-tickets.md):* the 16 HTML tickets re-pulled and searched again ($0.84): right place
  Sonarr 36 of 40 (unchanged), bitwarden 21 of 24 (one flip, run-to-run variation). It fixes lost text, not the rate.
  The quote check accepts a short whole line with or without its marks (a fresh check found the marks-only form
  rejected "1. Save it").
**Why:** the same ticket should read the same whichever format Jira picked; links and code marks are leads for the
finder and for code's own signals (backticked names).
**Cost:** the text of HTML tickets changes, so their ticket hash does: their finder answers are marked changed and are
found again (≈ $1 for the 16). No contract changes shape. Measured on Sonarr's 5 first (D100), then bitwarden's 11.

## D103 · A code quote may be one whole line, even with no space — 2026-11-02 (decided, built)
Code checks every line the file finder (D101) and the page checks (D53) quote from code: at least 12 characters and
two words, so a bare name proves nothing. Real lines are often one token: `model.ValidateEdit();`,
`app.UseForwardedHeaders(options);`. On record, all 5 rejected code lines were one token, and 4 of them were whole
lines of their file (BWB-9 lost `ServiceCollectionExtensions.cs` that way). A fresh check (D61) counted every
rejection on record, retries included: 13, of which 10 were whole lines (SONB-1, 6 ×2, 18, 42's first try; BWB-9 ×2,
11, 18) and 3 were rightly refused (a string fragment, a bare name twice).
- **A quote with no space counts when it is a whole line** (after trimming), as a ticket quote counts when it is 4
  words or a whole line (D53). A piece of a line still needs two words (SONB-42's string fragment stays rejected).
  The 12-character floor stays. Owner: "fix that rule" (2026-11-02).
- The finder's instructions still say "two words" and are left as they are: they are part of its input hash, so
  editing them would drop every saved answer. The retry message says "two words, or a whole line"; the instruction
  text follows at its next real change.
**Why:** a whole line is as specific as two words; the rule existed to stop fragments, not statements.
**Cost:** none measured: it changes only which answers pass the check, so it is seen on the next finder run.

## D104 · The proof's turn cap counts model rounds, what `--max-turns` limits — 2026-11-02 (built; not yet run)
The audit of the 40 proof results (phases/3-tickets.md): `--max-turns` limits model rounds, while Claude Code's `num_turns`
on a normal finish is tool results + 1, so parallel tool calls inflate it. `BugProof` summed `num_turns` and compared it
with the limit before reading the answer, and threw out 6 finished answers unrun (SONB-6, 9, 18, 29, 46, 47: 22-29 real
rounds, under $0.45). It also cut SONB-10's retry to 3 rounds when 12 were left.
- **The driver counts rounds** (one per model message id in the transcript) and adds `rounds` to the result given to
  `check`. Claude Code's own stop, the attempt's rounds and the dollars still cap an attempt (D81); the 30 is unchanged.
- **With no `rounds`** (a result passed by hand), only Claude Code's own stop and the dollars apply, and a retry gets the
  whole limit. `num_turns` isn't a fallback: it would bring the bug back.
- *Observed, not read from Claude Code's source:* the pattern holds on all 46 saved transcripts; a stopped run ended at
  exactly its limit (SONB-28 at 30, SONB-10's retry at 3).
**Why:** a cap that throws out finished work measures our own bookkeeping, not the reproducer; D81's money bound is the dollar cap.
**Cost:** attempts use more of the turns they were given; expect few extra real proofs (of the 4 real bugs, SONB-29 would
probably pass on its fix; SONB-6, 9, 18 probably wouldn't). D86's raise to 30 stands: its deciding case, SONB-5, was a real stop at 15 rounds.
Nothing tests the driver's count in `dotnet test` (no test covers `ticket-run.sh`); a fresh check ran it on all 46 saved transcripts.

## D105 · The on-fix check says what kind of failure it was; D79's bar stays as it is — 2026-11-02 (built; shows-based comparison not yet run on real proofs)
The audit of the 40 proof results (phases/3-tickets.md): `--verify` knew only pass or fail, and a fail meant different
things. SONB-30's reported `ArgumentNullException` is gone, and the test's own setup then throws a `NullReferenceException`;
SONB-14 fails exactly as before, since the test never fires the event the fix relies on; SONB-2 can't compile, since the fix
changed a constructor the test calls.
Decided (owner, "of course lets go"): `verification.result`, by code:
- **`passed`:** the test passes and nothing new fails. **`new-failures`:** it passes, other tests started failing.
- **`same-failure`:** it still fails with the words its proof named (`shows`, D95). **`different-failure`:** it still fails,
  those words gone: the reported failure looks gone and something else fails.
- **`not-compiled`:** it can't build here; `reason` says whether the error is in the test. **`not-run`:** a timeout, no test
  found, a runner error, or the test file isn't on the branch (nothing is run, so a same-named test elsewhere can't pass for it).
- A proof with no `shows` (before D95) compares the whole message without its stack lines.
- **Also:** `testChanged` when the test was edited after the proof (updating it is the normal answer to `not-compiled`, and a
  pass then isn't on the test that was reproduced; recorded, not blocked); the reproduction test is listed in `newFailures`
  only when it passes alone and fails with the rest, since otherwise it's the result itself; a failed build skips the full
  test run; `othersChecked` is false when no full run judged the other tests, as on Playwright, which has none. Optional fields in `bug-proof` v1, as D95's; nothing outside Phase 3 reads it yet.
- **D79's bar isn't moved:** only `passed`, by the proof's own test on the answer key's fix commit, counts (a hand-run
  `--verify` elsewhere or on an edited test doesn't).
  Everything but `same-failure` is reported as a range beside it (`shareIfUnclearReal`). A test that passes while others
  newly fail isn't counted as a pass, which is stricter than the bar's wording; no saved proof has it.
- **Re-scored on the saved run, no AI:** 17 of 22 pass (77%), up to 19 of 22 (86%): same-failure SONB-1, 11, 14;
  different-failure SONB-30; not-compiled SONB-2. The bar stays failed.
Rejected: an AI judging the failure on the fix (nothing could check it, D18); checking the test runs the lines the fix changed
(SONB-26's false pass goes through the fix's own new early return, so it wouldn't catch it, and a proof has no fix to compare
with); counting the unclear ones as real (moves the bar after seeing a result).
**Why:** a person can't act on a bare "failed", and the score shouldn't hide what's behind it.
**Cost:** code describes what happened, not why. SONB-14 reads the same as SONB-1 and 11, and SONB-26 stays a false pass, so
a pass can still be for the wrong reason and a person reads the rest. A `shows` holding a value that changes per run (a time, a
temp path) reads as `different-failure`; a very general one reads as `same-failure`. With bare test names (Sonarr's reports),
two fixtures' tests with the same method name can't be told apart: an already-failing one dropped from the baseline can read as
`new-failures`, and a newly failing one that shares a baseline test's name is missed. No saved baseline has either (all empty).

## D106 · "Not reproduced" stops saying "don't fix" and says why — 2026-11-02 (built; not yet run)
Changes D75. In the saved run, 7 of the 9 not-reproduced tickets are real bugs with real fixes, and 8 of the 9 only hit the
cap (6 of those through D104's bug); only SONB-16 ran a test that passed. So "don't fix yet" mostly held back real bugs, and a
questions draft had to tell the reporter what was tried and ask for more when the tool had only run out. D79's own fallback
applies only when "proof finds bugs" fails, and it passed (73%), so this is the owner's call (owner, "ur call, just go ahead").
- **Why, by code:** `noProof` on the attempt: `ran-out`, `test-passed` or `flaky`. Outcome values are unchanged; a record
  from before reads it from its reason. Optional field, `bug-proof` v1.
- **Phase 3 says "no proof yet, the developer decides".** **Phase 4's rule stands:** an agent waits for a person's go,
  since with no failing test it has nothing to check its own fix against. That's a limit on the agent, not the proof blocking a
  fix, which D79's advice-only forbids: a person's go always clears it. To confirm when Phase 4 is grilled.
- **Drafts:** a questions draft must cover the attempt only if a test ran. For `ran-out`, the writer is told to leave it out and
  code refuses a claim linked to the proof, so an unlinked "we couldn't reproduce this" is left out (D97).
**Why:** a label that holds back real bugs and misleads reporters costs more than the 2 not-bugs it would have caught.
**Cost:** a developer may fix a wrong report with no warning from the proof; the Phase 4 rule is the guard for agents.

## D107 · The finder's reason is a guess: labelled for people, hidden from the proof — 2026-11-02 (built; not yet run)
The owner objected to the finder naming a cause at the start. It only reads code (Read, Grep, Glob) and nothing has run, so its
one-line `why` ("recursing infinitely") is a guess no code checks, yet it went into the bundle and into the reproducer's input.
- **The reproducer's input leaves it out;** the files, quotes and lines stay. **People see it as "The finder's guess, not
  checked:"** in `bundle.md`. `bundle.json` keeps it. No finder re-run: `file-finder.md` is unchanged.
- **Root cause isn't Phase 3's job.** A developer reproduces, then traces. A cause found before anything runs has no checker
  (D18). The evidence comes in Phase 4, when a fix turns the failing test green.
Rejected: a root-cause step at the start (the same guess in more words); dropping `why` entirely (it's useful reading once
labelled); recording what a failing test ran as evidence (left for Phase 4).
**Why:** a test should check what the ticket reports; SONB-1's bad proof tested exactly this kind of guess.
**Cost:** probably small: SONB-1 and 11 tested the wrong thing with no finder at all, so the agent can still guess on its
own. It can't be measured: no finder-era proof ran with the reason visible. Only the input leaves it out: the reproducer may
Read any file, so it could open `bundle.md` (none of 46 saved transcripts read such a file).

## D108 · `ticket post` refuses a draft whose inputs changed — 2026-11-02 (built; no AI needed)
`post` never compared a draft with what it was made from, so a draft saying "not confirmed" could be posted after a developer
confirmed. It now recomputes the draft's input hash: changed (the ticket, the bundle, the proof, the writer's instructions), or
impossible to work out, gives `draft_stale`, unless `--stale`. It's the only step that writes to Jira. The error gives the exact
command to make it again (kind, tone, PR). A relative range such as `HEAD~1..HEAD` moves with a later commit, so pass fixed commits; the error says so when it repeats one.
Rejected: a warning only (the output is JSON, easily missed); a word filter for "don't call it a bug" (stays an instruction).
**Why:** a person reads the draft, but can't see that its facts moved.
**Cost:** posting loads the graph and pages to recompute, and an instructions change makes every saved draft stale.

## D109 · Phase 3 closes on what we have; the proof ships as advice only — 2026-11-02 (owner: "do whatever we have for now, and get ready for p4, ur call, just go ahead")
D79 already says what follows when the proof's bars fail, so they aren't a reason to stay in Phase 3, and more Phase 3 work can't
show whether the bundle helps anyone fix a bug. Only a fix agent can.
- **No proof re-run now:** the report uses the saved numbers and says they predate the finder, D95, D104, D106 and D107.
  The re-run (at most about $45: the $40 cap plus about $4 of drafts; Sonarr only) is owed after 2c, before Phase 4 is built, on the owner's OK.
- **Moved to Phase 4:** the graph gaps (`IExecute<T>` handlers, a hub made by test callers, code dispatched by interface or name,
  framework-run code). No Phase 3 bar depends on them. **After the re-run:** SONB-17 offered only Playwright; SONB-16 passing
  alone; setup problems counted against the AI (SONB-10, 18, 28, 46).
- **D99 comes first, before Phase 4 (owner: "before"), as its own phase, 2c, on the roadmap.** I recommended after Phase 4's
  first experiment; the owner chose before: it's core architecture, and Phase 4's agents should check their work against
  requirements from the start. It's still scored on the planted FSD's key and can't move the proof bars on Sonarr.
- **Dropped:** the finder naming more files (no gain on Sonarr; the 4 misses are 2 choices of where to fix, 1 that can't be hit
  and 1 real one). **Not run:** bitwarden's proof (D100) and a private repo: rates stay public-code only.
**Why:** the phase's job is a checked starting position; what's left either needs Phase 4 or doesn't change a bar.
**Cost:** Phase 3 closes with two bars failed and its proof numbers out of date; a person makes every bug-or-not call until D99,
and Phase 4 starts later.

## D110 · The core stays C#; no port to Python — 2026-11-03 (owner: "Stay on C#")
The owner asked whether the core should run from Python while the analysed code stays C#. Reconfirms D9 and D23.
- **Roslyn stays C# either way** (10 files, 1,884 lines, 6 of the 8 detector files), so a Python core means Python, C# and Node
  on every developer machine, not one language swapped for another.
- **Measured, the reasons to move don't hold:** real commands take about 150 ms (debug build); a self-contained macOS arm64 build
  ran `req sections` with no .NET on the machine (139 MB; building the graph still needs the SDK; Windows untested); the Word
  importer's gaps are elements it doesn't handle, not limits of C#.
- **No failing bar traces to the language.**
- **A new product may still use its natural language through the CLI (D9, D23).** Spec and Tickets are in-process libraries today,
  so moving either out would need a CLI boundary first.
- The untested bash drivers (752 lines) are a real weakness, but not a reason to change language. How to fix them isn't decided here.
Rejected: a full port; Python for everything but the Roslyn parts (three runtimes, and D23's "two copies drift" unless the
boundary is clean).
**Why:** a port costs re-verification, and input hashes are built from C#-generated text (`Bundle.cs`), so saved AI outputs would
need byte-identical text or be paid for again. Done during the overhaul, a score change couldn't be traced to the port or the redesign.
**Cost:** contributing to the core still needs .NET (D23); a non-developer install is a large self-contained build.
Reopen only if a measured failure traces to the language, and then as a port-only step with identical hashes as its bar.

## D111 · Stangent imports PDF and Excel as well as Word — 2026-11-03 (owner: "i want our stangent able to handle pdf and xlsx also"; PDF built 2026-11-04, uncommitted; Excel not built)
Changes "`.docx` only" (D15's deferral, 2b) and "a converter when a real FSD or ticket needs one" (D72).
- **The same rules as Word (D13, D16, D18):** code only, no AI; the same file gives the same text; the original is archived; what can't
  be brought in exactly is listed, never dropped silently.
- **A PDF's sections (owner, 2026-11-03, "okay"):** bookmarks first; otherwise the printed contents page, each entry checked by code
  against the page it points to; otherwise, or when entries fail, the import stops and explains, and `--flat` imports the whole
  document as one labelled section. Headings are never guessed from fonts. Measured with Python tools on `Documentation.pdf`: all 340
  bookmark titles are on their pages; all 163 printed entries with page numbers check out, but the printed list has 167 entries against
  340 bookmarks, so sections from it are coarser. A title on a page doesn't prove it's the heading there.
- **Tables and the library (owner, 2026-11-03, "build it"):** PdfPig reads the PDF; our own table reader builds cells from the drawn
  lines. A code check requires every cell word for word in the page's own text; a table that fails is kept as plain text and listed as
  "not brought in exactly". Implemented by an agent on Opus 5.5 at medium effort (owner).
- **PDF as built (2026-11-04), read directly with no conversion to Word; Stangent still writes its own text (`document.md`), which quotes are matched against:**
  - **Text:** a false sentence is worse than a broken one, and a listed problem is better than either. So it joins across a page only on strong
    evidence, keeps text in other directions apart, removes only text never painted (render mode 3, off-page; an OCR layer stays, listed), and
    keeps white text, listed. Running lines must be identical or follow the page number (Arabic or Roman, odd and even pages apart).
  - **Tables:** only ruled grids become tables. Fill-only rectangles are shading, not rules. A word crossing a cell boundary or sitting in an open cell fails the
    table, which then stays plain text and is listed. Order is checked by position against the content stream.
  - **Errors:** any encryption gives `pdf_locked`, even with no password needed. Also `invalid_pdf`, `invalid_docx`, `no_text`, and `no_structure` for a PDF or a `.docx`
    with no headings (`--flat` overrides); the `.docx` rule is the assistant's call, not asked of the owner. `source-document` stays v1.
  - **Measured on `Documentation.pdf`:** 340 sections, 321 tables, 0 fell back, 99.93% of words kept against `pdfplumber`, 77 problems; 718 tests pass; a Word document
    that already imported gives byte-identical text (Sonarr's FSD, run 2026-11-04). The "encryp" / "t" cause: PdfPig's glyph box is wrong for some letters, so positions come from baselines.
  - **Checked by:** four fresh agents with fixes between. A fifth, on the last four small fixes, was cut off by a usage limit; those rest on the tests and the re-import above.
  - **Known gaps (`docs/spec.md`):** all checks rest on PdfPig's own letters; borderless tables; a table row drawn apart loses its columns, its words stay in order (39 such rows,
    1.6% of the words); two columns drawn row by row; a capitalised sentence over a page; a body-size footnote can join a next page (joins are listed); near-white text; a 100,000-level
    page tree kills the process (hostile file, nothing written); images unread. An earlier check judged all 13 "possible table" listings on this file false: noise, not lost text.
- **To grill before building Excel:** hidden sheets, formulas and merged cells; whether ticket attachments (D72) use the same converters.
- **C# libraries, first measured on `Documentation.pdf` against Python's `pypdf` and `pdfplumber` (2026-11-03, scratch code, not fresh-checked):**
  PdfPig 0.1.14 (Apache-2.0) gave identical bookmarks (340), the same word counts but for one word in 111,971, every sentence of 6+ words on
  table-free pages word for word (2,024, headers and footers stripped), the same 379 images, identical output on two runs, about 3 s; it
  reports no bookmarks and refuses a locked file with its own exception. **Tables aren't solved:** Tabula 1.0.1 drops letters ("tweet"
  became "twet"); a grid built from the drawn lines found 330 tables of 2×2 or more against 329, but only 70% of cells of 4+ words match, a
  page border reads as one cell, and a letter at a line's end can land in the next column ("encryp" / "t"; traced, see below). The
  Python result is a comparison, not proven right.
- **Test files:** the owner's `Documentation.pdf` (D56) for PDF. No real Excel file yet.
**Why:** the owner's documents come in these formats.
**Cost:** a PDF holds no reliable structure, so its import is weaker than Word's and quote checks depend on extraction; at least one
new dependency; two more formats to keep byte-identical across versions.
**Note, 2026-11-04 (D121):** Word and PDF now fill in one set of import steps (`IImportFormat`), which Excel fills in next, and ticket attachments use it. What D72 left open about attachments is settled there.

**Note, 2026-11-04 (D123):** this answers what was left open here about Excel. Sections are sheets, values are what's stored, and hidden, merged, formula and comment cases are settled as D123 and `docs/spec.md` say, by the answers §8 gave and D120 took up. It's been run on hand-built files only: no real Excel FSD has been seen, so D56's bar for real documents still stands.

