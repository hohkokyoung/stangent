# Improvements

**Ideas not decided yet.** The file an item sits in is its status (D134): not decided here, decided and unbuilt on [`roadmap.md`](roadmap.md), built or rejected in [`improvements-done.md`](improvements-done.md). When its state changes, the item moves, in the same turn as the decision or the work behind it.

Sections keep their numbers in both files, so "§6" means the same section in either. Each note says what's there today and what's recommended. Nothing here is a commitment.

## 1. Explaining how the app works today

- Pages follow how people use the app. Journeys are cut by step ("what happens at step 4" lives in one place), alongside list, report and admin pages. The team switched to this after a pilot.  
  *Pages are per screen and endpoint today (D54, D63). Decided by D144: pages stay, with workflow files on top. J.1 adds the order without changing the pages.*
- Shared behaviour is written once (lifecycles, shared steps) and linked. Feature pages only describe what's different.  
  *Shared components and endpoints have their own pages. Lifecycles and shared steps don't. Recommend: decide with the page shape.*
- Adapters for the actual stack: Playwright tests, Svelte screens, Razor pages, .NET code and SQL seed data.  
  *.NET, Angular and React are in. Playwright is used for bug proof (D86), and existing specs now link to screens in the graph as evidence of strength `read` (1.2, D115). Razor is a blind spot (D65). Svelte is out until a repo needs it (D31). No SQL seed data. Recommend: the rest when a repo needs them.*
- Runs are reproducible, and that was measured. A fresh session with no access to the first run found 9 of 10 key findings again. Runs are compared by the code lines they cite and their wording, not by id.  
  *"Repeatable" measures cache reuse only (D57, D79). Recommend: do.*

## 2. Keeping that knowledge current

- Changed code that no page covers is listed as new work, so it isn't silently missed.  
  *`spec stale` lists new and removed pages only. Recommend: do, small.*
- Every reference is stamped with the exact code it describes: commit and branch, per repo for apps split over several repos (`BUILT_FROM.json`).  
  *Each page records its commit and input hash. One repo only (D4). Recommend: reopen D4 only if a real target is split across repos.*

## 3. Bringing the Word FSD in

- Handles messy real-world Word files:
   - merged table cells expanded  
     *A cell merged down rows repeats its value and is listed (D116). A cell merged across columns still leaves blanks, not its value.*
- Open Word review comments become questions for the BA.  
  *Each comment is listed with its author, section and open or resolved state, in a fixed shape (D116). **Not done:** becoming questions. 2.5 didn't build it.*

## 4. Stable section ids across FSD versions

- Proven on a real version change: 294 sections unchanged, 27 modified, 25 added, 14 moved, with no open decisions. Matching took half a second.  
  *No real version change yet. It needs a document with two versions (D56). A planted edit of Sonarr's FSD found all it planted: a check of the rules, not this.*
- Mistakes were fixed and locked in. The matcher's three first-run errors each got a design fix and a test, and the reviewed result is pinned as a regression check.  
  *A reviewed pair is pinned (2.2). There's been no real first run to learn from yet.*

## 5. BAs changing the FSD, with approval

- BAs describe changes in plain words. Claude edits through the tool, and direct file edits are blocked so ids and numbering stay correct.  
  *Conflicts with D52 (the FSD is written outside Stangent). Recommend: decide after results, and first settle whether BAs have Claude Code.*
- BAs never touch git. Branches, commits and merge requests happen behind the scenes.  
  *D99 has BAs ticking boxes in a PR, and D12 has a dashboard in Phase 8. Decide with the BA workflow.*
- Each BA has their own branch, and every edit is a commit, so nothing is lost.  
  *Decide with the BA workflow.*
- Done is one step. It opens the merge request.  
  *Decide with the BA workflow.*
- The lead BA sees each change marked up and approves or sends it back with a note.  
  *D99 decided approval by tick boxes in a spec-only PR, not built. No send-back flow.*
- Approval safety rules:
   - you can't approve your own change (a one-BA app is the recorded exception)  
     *Not in Stangent. Decide with the BA workflow.*
   - an approval never overwrites a section that changed after it was reviewed  
     *A decision drops back when its quote changes (D58). Nothing blocks a merge.*
   - approving always asks for confirmation  
     *Decide with the BA workflow.*
- Every approved change is a commit on `main` recording who asked, who approved and which sections changed: a full audit trail.  
  *`decided_by` comes from git, never typed (D58). "Who asked" isn't recorded.*
- Clashes between BAs are handled: keep mine, take theirs, or combine.  
  *Decide with the BA workflow.*
- One command tells each person what's next. A note at session start says what's waiting on them.  
  *`/stangent:status` shows the store only. Recommend: do, with the BA workflow.*
- One-line installer for PMs, with the whole team pinned to one version and offered updates.  
  *"Installable for others" isn't placed (D1). A self-contained build ran with no .NET. Recommend: do when BAs become users.*

## 6. Checking the FSD against the app

- Two ways to check: against the code, or against the reference for BAs who don't have the code.  
  *Pages only today. Recommend: test against the code first (D99's "bigger option").*
- A per-chapter report of findings by result and severity, plus a count of FSD rules with no test, for QA. Still to do: `trace check` lists ids and severity, with no per-chapter view.  
  *Bulk approval works per chapter, but there's no report. Tests carrying ids are Phase 5. **Not done:** 2.5 didn't build it. Recommend: do after severity.*
- Trial result: every finding built on a correct reference page was right (12 of 12).  
  *A result, not a feature. Stangent's own: 44% false alarms (D99). The two aren't comparable.*

## 7. Engineering discipline

- Every requirement is traced to a test. The build fails if a scenario has no test, or a test names no scenario.  
  *Tests carrying ids in target projects are decided for Phase 5 (D10). Not done for Stangent's own code. Recommend: decide with Phase 5.*
- The test suite is strong: a 90% coverage floor, tests against real tools (git, pandoc, Office files) and end-to-end ingest tests.  
  *Real git and Office files are used. Coverage is measured now (`scripts/coverage.sh`: 69.7% of lines, 2026-11-04) and recorded in `phases/0-foundation.md`. No floor yet: the owner's call (D28).*
- Hooks never block Claude, and that is tested.  
  *The plugin has no hooks. Applies only if we add some.*
  *Since: T.8 added `read-only-hook.sh` (D126).*

## 10. From comparing a step-by-step journey workflow (2026-11-05, D129)

- J.3 · The page verifier also reads the tests.  
  *Today it reads the bundle only, and tests aren't a separate source. Recommend: decide after J.1, which has to read those tests anyway.*

## 11. From the critical review (2026-11-11)

*An outside-view review of the whole project, asked for by the owner. Nothing here is decided. Each item says what was found, then what's recommended. In the order recommended.*

**Bottom line of the review:** after 24 days (362 commits, 228 decisions, about 37K lines, 2,211 tests) one of the brief's twelve items passes its bars (the code graph). Nothing Stangent produces has yet been shown to help anyone fix a bug or write a spec.

- R.1 · Run "does the bundle help a fix?" before layer 4 is built.  
  *Open since 2026-11-02 (`questions.md`). `phases/3-tickets.md` records that a plain coding agent's own search passed the 70% right-place bar "by a wide margin", held-out set included, which is evidence against the graph's main job for tickets. Recommend: the same bugs fixed three ways (ticket alone; plus the failing test; plus the bundle), as `questions.md` already describes. Paid, on the owner's OK. If the bundle doesn't help, layers 4–6 change shape.*
- R.2 · Run flows on one repo Stangent was never tuned on, with no fixes, and report that number.  
  *Layer 3's item 2 went from 66% to 95.3 / 96.0 / 95.6% (bar 95%) through about 15 fix rounds and rulings R1–R11 on the same 15 flows; D185 and D211 changed the scoring after results were seen. Git history (D43) and bugs (D100) had a held-out repo; flows have none. Recommend: eShop or bitwarden/server (already cloned), a fresh key, one score, no fixing first. Also count how many new rules that repo would need: that is the real cost of adding a project.*
- R.3 · Test linking with flows by hand, now, on a few planted-FSD sections.  
  *The rebuild exists because links failed (24 of 30, 44% false alarms). `questions.md` traced that to a missing page, a judge that skips sentences and bundles missing backend code. The rebuild restarted at the graph, two layers below linking, so nothing shows flows fix it until layer 5. Recommend: hand-feed flows for 3–5 sections to the judge and compare with the saved runs. If the numbers don't move, layer 4 pages aren't worth building. About the cost of one small paid run.*
- R.4 · Cut the off switches now, not at layer 7.  
  *About 30 switches are built, off and "not shown to help" (`roadmap.md`). `Stangent.Spec` is the largest project (15.4K lines), mostly on the path that failed. D138's "one fix attempt, then removed" wasn't applied to the links (requirement level, disprove and `page-gap-strict` all missed). Recommend: delete what D156 P8 already names, keep what the pipeline can't run without.*
- R.5 · Build one thin slice end to end before perfecting any layer further.  
  *Agents, testing, PR, CI/CD, dashboard and demo aren't started, and they're where the brief's value is. Recommend: one ticket → fix → test → PR on one repo with what exists, to learn which lower parts matter.*
- R.6 · Find one real project with a real document, or narrow what Stangent claims.  
  *D1 says the owner's own use first, but the one real project is retired (D137) and the FSD is planted. Every score comes from three open-source apps the rules were shaped around.*
- R.7 · Shrink the brief.  
  *Demo, dashboard and CI/CD are things existing tools already do. The graph, flows and spec links are the parts that could be Stangent's own. Recommend: drop or defer the rest until R.1 and R.5 say the core works.*
- R.8 · Say what a bar can and can't show.  
  *"No wrong ✅" can't be proved by a sample: D217's 0 of 36 still allows about 1 in 12. The key was written by an AI and the owner approved 3 of its 15 files. Recommend: report item 1 as a rate with its range; treat a ruling or scorer change after scoring as a bar change, recorded as one.*
- R.9 · Label name-based rules as guesses everywhere.  
  *Several "deterministic" rules are guesses by name: a queue table is one named queue or pending (D205), a cache is a name containing "cach" (D215), a checking filter is named Validate, Verify, Check or Captcha (D207), any three links are a menu (D221, D226). Code making the same guess every time is repeatable, not right. Recommend: every such rule gives 🟡, and R.2's run counts how often each is wrong.*
- R.10 · Slim the paperwork.  
  *More docs commits (146) than feature commits (110). `decisions.md` was trimmed to one line each on 2026-11-07 (D157) and D202–D228 are paragraphs again. The r2 and r3 roadmap cells are long changelogs read every session. "What's next" on `roadmap.md` is the pre-rebuild order, never confirmed. `CLAUDE.md` rules 7 and 8 still say "build first, test later", which D158 ended. Recommend: changelogs live in phase files only; one line per decision in the index; fix the two rules; rewrite "What's next".*
- R.11 · Put the owner back in the challenger's seat.  
  *The assistant makes about ten decisions a day and the owner answers "Yes, that rule" or "ur call"; D217 and D226–D228 are the assistant's calls, the last three unconfirmed. Recommend: fewer, larger decisions put to the owner, each with what would prove it wrong; rule-level detail stays in the phase file without a D number.*
- R.12 · Push after every working session.  
  *Until 2026-11-11 the rebuild's 261 commits sat on one disk and CI had never run on them. Pushed that day (`origin/claude/phase2c-spec`). Recommend: push at the end of each session; check CI on the branch.*

**What the review says to keep:** bars set before building, the cost recorded on every decision, failures reported plainly, fresh checks (they found real faults every round).
