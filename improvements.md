# Improvements

**Ideas not decided yet.** The file an item sits in is its status (D134): not decided here, decided and unbuilt on [`roadmap.md`](roadmap.md), built or rejected in [`improvements-done.md`](improvements-done.md). When its state changes, the item moves, in the same turn as the decision or the work behind it.

Sections keep their numbers in both files, so "§6" means the same section in either. Each note says what's there today and what's recommended. Nothing here is a commitment.

## 1. Explaining how the app works today

- Pages follow how people use the app. Journeys are cut by step ("what happens at step 4" lives in one place), alongside list, report and admin pages. The team switched to this after a pilot.  
  *Pages are per screen and endpoint today (D54, D63). Recommend: decide after results. J.1 adds the order without changing the pages.*
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
