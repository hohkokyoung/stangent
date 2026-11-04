# Improvements

The owner's list, kept as given, with a status against each item. Change a status in the same turn as the decision or the work behind it.

| Mark | Meaning |
|---|---|
| ✅ | In Stangent now |
| 🟡 | Partly in Stangent; the note says what's missing |
| 🔨 | Being built now |
| ☑️ | Decided, not built |
| ⬜ | Not decided yet; the note gives a recommendation |
| ❌ | Decided against |

Here's the full list, grouped by area. Each section ends with a line on why it's ahead of Stangent, based on what the review says about Stangent.

## 1. Explaining how the app works today

- ✅ Every fact has proof. Each statement gets a permanent id and a code citation (`repo/path:lines`).  
  *Built in 2.3 (D118). A ledger per page holds the ids, and `path:lines` and the commit come from the graph by code, beside the page. A dirty tree writes none, and the page says why. A page from before gets them from `stangent spec cite`, with no AI.*
- ✅ Ids are never renumbered or reused, so links from findings, answers and the FSD stay valid.  
  *Built in 2.3 (D118): the same words, or a rephrase with the same symbols alone in its section, keep the id. A retired number never returns, and a link citing one drops back, reported.*
- ⬜ Pages follow how people use the app. Journeys are cut by step ("what happens at step 4" lives in one place), alongside list, report and admin pages. The team switched to this after a pilot.  
  *Pages are per screen and endpoint today (D54, D63). Recommend: decide after results.*
- 🟡 Shared behaviour is written once (lifecycles, shared steps) and linked. Feature pages only describe what's different.  
  *Shared components and endpoints have their own pages. Lifecycles and shared steps don't. Recommend: decide with the page shape.*
- 🟡 Three kinds of statement are kept apart: what the app does today, the required behaviour for a new app, and findings or open questions (`X` and `Q` ids). Opinions can't pass as facts.  
  *Reference, FSD and findings are separate (D52), and questions exist. No separate finding or question ids of this form, and "a new app" isn't Stangent's job. Recommend: adapt.*
- ⬜ Findings get a severity from one shared scale, graded by the effect on users on the path they actually take.  
  *Nothing has a severity. Recommend: do.*
- ⬜ A second agent tries to disprove every finding. It assumes each claim is wrong until the code proves it, and checks whether the code path is actually reached. Separate "gap finders" look for what the first pass missed.  
  *D62 rejected this; only "agree" verdicts get a second check. Recommend: do, reversing D62, and re-score.*
- 🟡 The call graph handles real C#. It resolves interface calls and DI registrations, and shows every entry point (endpoint, page, job, handler) that reaches a method.  
  *Roslyn graph with DI and impact answers, scored on real repos. Known gaps: code dispatched by interface or name, and framework-run code (D109). Recommend: close them in Phase 4 as decided.*
- 🟡 Adapters for the actual stack: Playwright tests, Svelte screens, Razor pages, .NET code and SQL seed data.  
  *.NET, Angular and React are in. Playwright is used for bug proof (D86), and existing specs now link to screens in the graph as evidence of strength `read` (1.2, D115). Razor is a blind spot (D65). Svelte is out until a repo needs it (D31). No SQL seed data. Recommend: the rest when a repo needs them.*
- 🟡 Scripts generate navigation and numbering, so agents can't break links or page order.  
  *Code numbers statements and renders pages (`PageRender.cs`). No generated navigation. Recommend: do, small.*
- 🟡 Script checks on every output: coverage (`doc-check`), broken links (`docs-lint`), plan order (`plan-check`), and that no client data leaks into the tool (`prompt-lint`).  
  *`spec check`, `trace check`, `req check`, `phases check` and `data check` (client data, D113) exist. Coverage, broken-link and plan-order checks of this kind are per product.*
- 🟡 The client's code is read-only, enforced by a hook (`guard-app.js`), not just by instructions. Agents can't edit, build, run or use git in the app repo.  
  *Headless runs are limited by permission rules (D60, D86), with no hook. Bug proof has to build and run the app (D75). Recommend: not as written; read-only for analysis steps only.*
- ⬜ Runs are reproducible, and that was measured. A fresh session with no access to the first run found 9 of 10 key findings again. Runs are compared by the code lines they cite and their wording, not by id.  
  *"Repeatable" measures cache reuse only (D57, D79). Recommend: do.*

Ahead of Stangent because Stangent's reference pages are summaries, and its discrepancy check failed its quality bar. These pages carry evidence and are independently checked.  
*Partly fair: Stangent's statements do carry checked citations and a fresh verifier (D62). The failed bar is real (D68, D99).*

## 2. Keeping that knowledge current

- 🟡 Updates from a release diff. `reconcile` maps each changed file to the pages that cite it (only overlapping lines count) and updates just those pages.  
  *`spec stale` finds pages per PR and regenerates them on merge (D55), but per whole page, not by overlapping lines. Recommend: do.*
- ✅ A second agent re-checks the updated pages against the new code.  
  *The fresh verifier runs on regenerated pages (D62). A page fixed after its findings isn't verified again.*
- ⬜ Changed code that no page covers is listed as new work, so it isn't silently missed.  
  *`spec stale` lists new and removed pages only. Recommend: do, small.*
- 🟡 Every reference is stamped with the exact code it describes: commit and branch, per repo for apps split over several repos (`BUILT_FROM.json`).  
  *Each page records its commit and input hash. One repo only (D4). Recommend: reopen D4 only if a real target is split across repos.*

Ahead of Stangent because stale-page detection is only planned there (Phase 7).  
*Wrong: `spec stale` and `plugin/ci/spec.yml` have been built since Phase 2a.*

## 3. Bringing the Word FSD in

- ✅ Exact conversion with no AI guessing. The Word XML decides the structure and pandoc renders the text. Nothing is made up.  
  *Code only, with our own renderer and no pandoc (D52). Tracked changes now stop the import rather than resolve silently (D116).*
- ✅ Fails loudly instead of quietly degrading. It checks for tracked changes, merged or nested tables and missing heading styles before converting, and never guesses headings from bold text.  
  *Missing headings and unreadable files stop the import, for PDF and Word (D111). Tracked changes stop a Word import unless `--accept-changes` is given; nested and merged tables are listed; a code check compares every word (D116).*
- 🟡 Handles messy real-world Word files:
   - ✅ six heading levels, and tracked changes including moves  
     *Tracked changes stop the import; `--accept-changes` imports the accepted view, and moved text comes in once (D116).*
   - 🟡 merged table cells expanded  
     *A cell merged down rows repeats its value and is listed (D116). A cell merged across columns still leaves blanks, not its value.*
   - ⬜ embedded Excel saved as one CSV per sheet  
     *Listed by kind and section, not read (D116). Excel import is decided (D111), but not for embedded files.*
   - 🟡 PDFs and PowerPoint slides converted to text  
     *PDF is built, read directly (D111). PowerPoint isn't decided.*
   - ⬜ EMF pictures converted to PNG  
     *Saved as `.bin` today. Recommend: do if a real document needs it.*
   - ⬜ draw.io diagram sources pulled out of images  
     *Nothing reads diagrams. Recommend: do if a real document needs it.*
   - ⬜ Word cross-references turned into working links  
     *Plain text today. Recommend: later.*
- ✅ Lists everything it couldn't bring in exactly, so nothing is lost silently.  
  *Done for PDF, with stated gaps (D111), and for Word, where a code check compares every word per section on each import (D116). Not yet measured on a file saved by Word itself.*
- 🟡 Open Word review comments become questions for the BA.  
  *Each comment is listed with its author, section and open or resolved state, in a fixed shape (D116). Becoming questions is still to do, with 2.5.*
- ✅ Encrypted or protected files stop the run with instructions. It never works around the protection.  
  *`pdf_locked` for any protected PDF; a Word file that won't open gives `invalid_docx` (D111).*
- ✅ Fast on a real FSD: about 360 sections and 150 tables in about 18 seconds in the pilot.  
  *A 717-page Word file imported in 1.9 seconds.*

Ahead of Stangent because Stangent's AI extraction produced 109 requirements but acceptance criteria for only 12, and the review worried about the spec becoming "another source of hallucinations". This step can't hallucinate because no AI is involved.  
*Compares different steps: Stangent's conversion uses no AI either. The 12 is partly by design (D99 forbids inventing outcomes).*

## 4. Stable section ids across FSD versions

- ✅ Section ids survive edits, moves, renames and splits.  
  *Built in 2.2 (D117): a permanent id beside the number. A split or merge is a person's call, since only exact signals match on their own.*
- ✅ A new Word version comes in as a candidate, with a section-by-section list of what changed (`SECTION_DIFF.md`).  
  *Built in 2.2 (D117), for `.docx` and `.pdf` alike. The current version serves until a person promotes.*
- ✅ Doubtful matches are never guessed. They're listed for a person to decide, and promoting the candidate is refused until they're settled.  
  *Built for sections in 2.2 (D117), by code with no AI. Requirements stay with 2c step 6 (D99).*
- ✅ Removed ids are kept and marked removed, never reused.  
  *Built for section ids in 2.2 (D117), on the ledger. Requirement ids come with 2.3.*
- ⬜ Proven on a real version change: 294 sections unchanged, 27 modified, 25 added, 14 moved, with no open decisions. Matching took half a second.  
  *No real version change yet. It needs a document with two versions (D56). A planted edit of Sonarr's FSD found all it planted: a check of the rules, not this.*
- 🟡 Mistakes were fixed and locked in. The matcher's three first-run errors each got a design fix and a test, and the reviewed result is pinned as a regression check.  
  *A reviewed pair is pinned (2.2). There's been no real first run to learn from yet.*

Ahead of Stangent because stable requirement ids are still in progress in Stangent's Phase 2c. These are built and tested on a real FSD.  
*Fair.*

## 5. BAs changing the FSD, with approval

- ⬜ BAs describe changes in plain words. Claude edits through the tool, and direct file edits are blocked so ids and numbering stay correct.  
  *Conflicts with D52 (the FSD is written outside Stangent). Recommend: decide after results, and first settle whether BAs have Claude Code.*
- ⬜ BAs never touch git. Branches, commits and merge requests happen behind the scenes.  
  *D99 has BAs ticking boxes in a PR, and D12 has a dashboard in Phase 8. Decide with the BA workflow.*
- ⬜ Each BA has their own branch, and every edit is a commit, so nothing is lost.  
  *Decide with the BA workflow.*
- ⬜ Done is one step. It opens the merge request.  
  *Decide with the BA workflow.*
- 🟡 The lead BA sees each change marked up and approves or sends it back with a note.  
  *D99 decided approval by tick boxes in a spec-only PR, not built. No send-back flow.*
- 🟡 Approval safety rules:
   - ⬜ you can't approve your own change (a one-BA app is the recorded exception)  
     *Not in Stangent. Decide with the BA workflow.*
   - 🟡 an approval never overwrites a section that changed after it was reviewed  
     *A decision drops back when its quote changes (D58). Nothing blocks a merge.*
   - ⬜ approving always asks for confirmation  
     *Decide with the BA workflow.*
- 🟡 Every approved change is a commit on `main` recording who asked, who approved and which sections changed: a full audit trail.  
  *`decided_by` comes from git, never typed (D58). "Who asked" isn't recorded.*
- ⬜ Clashes between BAs are handled: keep mine, take theirs, or combine.  
  *Decide with the BA workflow.*
- ⬜ One command tells each person what's next. A note at session start says what's waiting on them.  
  *`/stangent:status` shows the store only. Recommend: do, with the BA workflow.*
- ⬜ One-line installer for PMs, with the whole team pinned to one version and offered updates.  
  *"Installable for others" isn't placed (D1). A self-contained build ran with no .NET. Recommend: do when BAs become users.*

Ahead of Stangent because Stangent has designed approval states but not a working edit-and-approve workflow for BAs.  
*Fair.*

## 6. Checking the FSD against the app

- ⬜ Two ways to check: against the code, or against the reference for BAs who don't have the code.  
  *Pages only today. Recommend: test against the code first (D99's "bigger option").*
- ⬜ Whoever finds a difference never verifies it. A separate agent re-checks it in a fresh context before the BA sees it.  
  *Same item as disproving findings in section 1 (D62). Recommend: do.*
- ✅ Only today's behaviour counts as evidence. The reference's opinions and its own findings can never back up a claim.  
  *Links quote the FSD and page statements only (D53).*
- ✅ A script checks every citation: the id must really be on the cited page, and in the right part of it.  
  *Quotes and ids are checked word for word (D53, `trace check`).*
- 🟡 Every finding is stamped and weighed. It records which code commit it was checked against, and how strong the evidence is: backed by a test, by code only, or weak.  
  *The contract is built: `evidence` v1 has a commit and a strength (`ran`, `stated`, `read`, `guess`, D113). No finding uses it yet, so this stays partly done until 2.4 moves findings onto it.*
- 🟡 Findings stay marked "unverified" until code confirms them. Nothing is overstated.  
  *Unverified until a person approves (D53); findings are off by default (D68). No code confirmation step.*
- 🟡 The BA decides each finding, and can say "the reference is wrong", which sends it back to the reference's owner.  
  *Decisions exist (agree, code-wrong, fsd-wrong, defer). "The page is wrong" doesn't. Recommend: do, small.*
- 🟡 Only what changed is re-checked. A new import re-checks just the units whose pages changed. Decided findings go back to "recheck" if their evidence changed, or "gone" if it disappeared.  
  *Unchanged inputs are reused (D18), and a changed quote drops its decision (D58). Re-judging a stale page's links is decided, not built (D99).*
- ⬜ Reference gaps are kept out of the BA's queue. "The reference doesn't cover this" goes to a developer, not the BA.  
  *Most of Stangent's false alarms are this case (D99). Recommend: do.*
- ⬜ A per-chapter report of findings by result and severity, plus a count of FSD rules with no test, for QA.  
  *Bulk approval works per chapter, but there's no report. Tests carrying ids are Phase 5. Recommend: do after severity.*
- ⬜ An alias map records names that differ between the FSD and the screens.  
  *Not in Stangent. Recommend: do if real documents need it.*
- ⬜ Trial result: every finding built on a correct reference page was right (12 of 12).  
  *A result, not a feature. Stangent's own: 44% false alarms (D99). The two aren't comparable.*

Ahead of Stangent because Stangent's discrepancy check failed its bar, with about 44% false alarms on requirement linking. The review also says Stangent is missing a proof trail, which this builds into every finding.  
*The 44% is real, measured on a planted FSD.*

## 7. Engineering discipline

- ✅ Scripts handle everything exact; agents only judge. Agents never decide state.  
  *D18, D60: `check` records results, never the agent.*
- ✅ All state changes go through one module that rejects illegal transitions and keeps a history.  
  *`src/Stangent.Core/State/`, eleven lifecycles (D114). What one command returned stays a plain field. A test scans the source, so a new write can't slip past it.*
- ✅ Stuck locks are never broken automatically. A person clears them after being asked.  
  *Stangent's locks are OS file locks, released when a process ends, so none get stuck. A hung process still blocks until its timeout.*
- ⬜ Every requirement is traced to a test. The build fails if a scenario has no test, or a test names no scenario.  
  *Tests carrying ids in target projects are decided for Phase 5 (D10). Not done for Stangent's own code. Recommend: decide with Phase 5.*
- 🟡 The test suite is strong: a 90% coverage floor, tests against real tools (git, pandoc, Office files) and end-to-end ingest tests.  
  *Real git and Office files are used. Coverage is measured now (`scripts/coverage.sh`: 69.7% of lines, 2026-11-04) and recorded in `phases/0-foundation.md`. No floor yet: the owner's call (D28).*
- ✅ No client data goes into the tool's repo, and that is checked.  
  *`stangent data check` runs in CI (D113): tracked documents need a reason in `.stangent-data-allow`; client terms stay outside the repo. It reads the current tree, not git history, and CI has no terms file, so CI checks documents only.*
- ✅ Honest pilot notes record misses and what was changed because of them.  
  *End-of-phase reports and `decisions.md`.*
- ⬜ Hooks never block Claude, and that is tested.  
  *The plugin has no hooks. Applies only if we add some.*

Ahead of Stangent because the review's main ask, "evidence provenance" (a proof trail for every finding), is already how this works.  
*Partly: Stangent checks citations but records no evidence strength or commit per finding.*

## Two caveats

- Most of the accuracy evidence comes from one app and small samples (one chapter, 22 findings). Stangent is tested on more codebases.
- The handoff between the reference and the FSD tool has no version check, so the strongest part of the list (section 6) depends on the reference's page format staying the same.  
  *Stangent has this covered: versioned contracts and `phases check` (D20).*
