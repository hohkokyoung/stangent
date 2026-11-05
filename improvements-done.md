# Improvements: done and rejected

**Built or rejected items, kept word for word for what was built and why (D134).** Not a list of work: what's decided and unbuilt is on [`roadmap.md`](roadmap.md), and what's undecided is in [`improvements.md`](improvements.md). An item whose note says "Rest on `roadmap.md`" has its unbuilt part there.

Sections keep their numbers in both files. The owner's list, kept as given, with a status against each item. Here's the full list, grouped by area. Each section ends with a line on why it's ahead of Stangent, based on what the review says about Stangent.

## 1. Explaining how the app works today

- Every fact has proof. Each statement gets a permanent id and a code citation (`repo/path:lines`).  
  *Built in 2.3 (D118). A ledger per page holds the ids, and `path:lines` and the commit come from the graph by code, beside the page. A dirty tree writes none, and the page says why. A page from before gets them from `stangent spec cite`, with no AI.*
- Ids are never renumbered or reused, so links from findings, answers and the FSD stay valid.  
  *Built in 2.3 (D118): the same words, or a rephrase with the same symbols alone in its section, keep the id. A retired number never returns, and a link citing one drops back, reported.*
- Three kinds of statement are kept apart: what the app does today, the required behaviour for a new app, and findings or open questions (`X` and `Q` ids). Opinions can't pass as facts.  
  *Built in 2.4 (D118): findings have permanent `X-0001` ids, questions already had `Q-0001`, and `trace check` rejects evidence citing a finding. "A new app" isn't Stangent's job, so that part is the FSD.*
- Findings get a severity from one shared scale, graded by the effect on users on the path they actually take.  
  *Built in 2.4 (D118): `blocker`, `major`, `minor`, `cosmetic`, starting `ungraded`, set by a person through the decision path. Since 2.5 the disprove step can suggest a grade, shown beside it; a person still sets it.*
- A second agent tries to disprove every finding. It assumes each claim is wrong until the code proves it, and checks whether the code path is actually reached. Separate "gap finders" look for what the first pass missed.  
  *Built in 2.5, reversing D62 (D119): off by default, and not shown to help until the owner's paid re-score runs. Not tested: `undocumented` screens found by code, which have no FSD quote. Since the fresh check, a refuted or unreachable finding leaves the BA's queue only for a developer to confirm or send back, because a quote can't show it's relevant. **Not done:** gap finders; the step only removes findings, so it can't raise what's found.*
  *Rest on `roadmap.md`: X.1 and X.16.*
- The call graph handles real C#. It resolves interface calls and DI registrations, and shows every entry point (endpoint, page, job, handler) that reaches a method.  
  *Roslyn graph with DI and impact answers, scored on real repos. Known gaps: code dispatched by interface or name, and framework-run code (D109). Recommend: close them in Phase 4 as decided.*
  *Rest on `roadmap.md`: the graph's blind spots, Phase 4.*
- Scripts generate navigation and numbering, so agents can't break links or page order.  
  *Code numbers statements and renders pages (`PageRender.cs`). No generated navigation. Recommend: do, small.*
  *Since built as T.7 (D131).*
- Script checks on every output: coverage (`doc-check`), broken links (`docs-lint`), plan order (`plan-check`), and that no client data leaks into the tool (`prompt-lint`).  
  *`spec check`, `trace check`, `req check`, `phases check` and `data check` (client data, D113) exist. Coverage, broken-link and plan-order checks of this kind are per product.*
  *Since: broken-link and duplicate-id checks built as T.6 (D126).*
- The client's code is read-only, enforced by a hook (`guard-app.js`), not just by instructions. Agents can't edit, build, run or use git in the app repo.  
  *Headless runs are limited by permission rules (D60, D86), with no hook. Bug proof has to build and run the app (D75). Recommend: not as written; read-only for analysis steps only.*
  *Since built as T.8, for analysis steps only (D126).*

Ahead of Stangent because Stangent's reference pages are summaries, and its discrepancy check failed its quality bar. These pages carry evidence and are independently checked.  
*Partly fair: Stangent's statements do carry checked citations and a fresh verifier (D62). The failed bar is real (D68, D99).*

## 2. Keeping that knowledge current

- Updates from a release diff. `reconcile` maps each changed file to the pages that cite it (only overlapping lines count) and updates just those pages.  
  *`spec stale` finds pages per PR and regenerates them on merge (D55), but per whole page, not by overlapping lines. Recommend: do.*
  *Rest on `roadmap.md`: I.2, built and off.*
- A second agent re-checks the updated pages against the new code.  
  *The fresh verifier runs on regenerated pages (D62). A page fixed after its findings isn't verified again.*

Ahead of Stangent because stale-page detection is only planned there (Phase 7).  
*Wrong: `spec stale` and `plugin/ci/spec.yml` have been built since Phase 2a.*

## 3. Bringing the Word FSD in

- Exact conversion with no AI guessing. The Word XML decides the structure and pandoc renders the text. Nothing is made up.  
  *Code only, with our own renderer and no pandoc (D52). Tracked changes now stop the import rather than resolve silently (D116).*
- Fails loudly instead of quietly degrading. It checks for tracked changes, merged or nested tables and missing heading styles before converting, and never guesses headings from bold text.  
  *Missing headings and unreadable files stop the import, for PDF and Word (D111). Tracked changes stop a Word import unless `--accept-changes` is given; nested and merged tables are listed; a code check compares every word (D116).*
- Handles messy real-world Word files:
   - six heading levels, and tracked changes including moves  
     *Tracked changes stop the import; `--accept-changes` imports the accepted view, and moved text comes in once (D116).*
   - embedded Excel saved as one CSV per sheet  
     *Saved under `attachments/<A-id>/`, one CSV per sheet, linked from `document.json`, with `document.md` unchanged (D123). Only on hand-built files.*
- Lists everything it couldn't bring in exactly, so nothing is lost silently.  
  *Done for PDF, with stated gaps (D111), and for Word, where a code check compares every word per section on each import (D116). Each import now has a verdict and a report, grouped by kind (D122). Not yet measured on a file saved by Word itself.*
- Encrypted or protected files stop the run with instructions. It never works around the protection.  
  *`pdf_locked` for any protected PDF (D111); `docx_locked` for a Word file with a password, saying how to get a copy that can be read (D122). A sensitivity label stops it too, with `--allow-label` (D122).*
- Fast on a real FSD: about 360 sections and 150 tables in about 18 seconds in the pilot.  
  *A 717-page Word file imported in 1.9 seconds.*

Ahead of Stangent because Stangent's AI extraction produced 109 requirements but acceptance criteria for only 12, and the review worried about the spec becoming "another source of hallucinations". This step can't hallucinate because no AI is involved.  
*Compares different steps: Stangent's conversion uses no AI either. The 12 is partly by design (D99 forbids inventing outcomes).*

## 4. Stable section ids across FSD versions

- Section ids survive edits, moves, renames and splits.  
  *Built in 2.2 (D117): a permanent id beside the number. A split or merge is a person's call, since only exact signals match on their own.*
- A new Word version comes in as a candidate, with a section-by-section list of what changed (`SECTION_DIFF.md`).  
  *Built in 2.2 (D117), for `.docx` and `.pdf` alike. The current version serves until a person promotes.*
- Doubtful matches are never guessed. They're listed for a person to decide, and promoting the candidate is refused until they're settled.  
  *Built for sections in 2.2 (D117), by code with no AI. Requirements stay with 2c step 6 (D99).*
- Removed ids are kept and marked removed, never reused.  
  *Built for section ids in 2.2 (D117), on the ledger. Requirement ids come with 2.3.*

Ahead of Stangent because stable requirement ids are still in progress in Stangent's Phase 2c. These are built and tested on a real FSD.  
*Fair.*

## 5. BAs changing the FSD, with approval

- Approval safety rules:

Ahead of Stangent because Stangent has designed approval states but not a working edit-and-approve workflow for BAs.  
*Fair.*

## 6. Checking the FSD against the app

- Whoever finds a difference never verifies it. A separate agent re-checks it in a fresh context before the BA sees it.  
  *Built with the disprove step (D119), off by default and not yet proven; the re-score stopped at 11 of 62 calls.*
  *Rest on `roadmap.md`: X.1.*
- Only today's behaviour counts as evidence. The reference's opinions and its own findings can never back up a claim.  
  *Links quote the FSD and page statements only (D53), and since 2.4 `trace check` rejects evidence citing a finding (D118).*
- A script checks every citation: the id must really be on the cited page, and in the right part of it.  
  *Quotes and ids are checked word for word (D53, `trace check`).*
- Every finding is stamped and weighed. It records which code commit it was checked against, and how strong the evidence is: backed by a test, by code only, or weak.  
  *Built in 2.4 (D118): each finding carries `evidence` records with the commit, the strength (`stated`, `read`; `ran` only with a real run) and who checked it, filled by code and validated by `trace check`.*
- Findings stay marked "unverified" until code confirms them. Nothing is overstated.  
  *Unverified until a person approves (D53); findings are off by default (D68). Since 2.5 a verdict can mark one as the AI's reading, `read`, never `ran`, and a refuted or unreachable one leaves the BA's queue only for a developer to confirm; off by default and unscored (D119).*
  *Rest on `roadmap.md`: X.1.*
- The BA decides each finding, and can say "the reference is wrong", which sends it back to the reference's owner.  
  *Built in 2.5 (D119): `page-wrong`, with `decided_by` from git. The page goes stale for its owner, is listed in `spec stale`, and writing it again sends the finding back to the BA.*
- Only what changed is re-checked. A new import re-checks just the units whose pages changed. Decided findings go back to "recheck" if their evidence changed, or "gone" if it disappeared.  
  *Unchanged inputs are reused (D18), and a changed quote drops its decision (D58), now marked `recheck` with the old decision visible, or `gone` with its id kept (D122). 2.6 stopped a finder's re-run re-paying the judge for the same pages. Re-judging a stale page's links is decided, not built (D99).*
  *Rest on `roadmap.md`: a stale page's links are judged again (D99).*
- Reference gaps are kept out of the BA's queue. "The reference doesn't cover this" goes to a developer, not the BA.  
  *Built in 2.5 (D119): a `page-gap` verdict goes to developers, listed in its own group. Off by default and unscored until the re-score runs.*
  *Rest on `roadmap.md`: X.1.*
- An alias map records names that differ between the FSD and the screens.  
  *Built, off, not shown to help: `aliases` in `specs/fsd/<doc>/table-map.yaml`, for comparing tables with the code (C.2, D124). Names on screens aren't compared: the link judge still reads those itself.*

Ahead of Stangent because Stangent's discrepancy check failed its bar, with about 44% false alarms on requirement linking. The review also says Stangent is missing a proof trail, which this builds into every finding.  
*The 44% is real, measured on a planted FSD.*

## 7. Engineering discipline

- Scripts handle everything exact; agents only judge. Agents never decide state.  
  *D18, D60: `check` records results, never the agent.*
- All state changes go through one module that rejects illegal transitions and keeps a history.  
  *`src/Stangent.Core/State/`, eleven lifecycles (D114). What one command returned stays a plain field. A test scans the source, so a new write can't slip past it.*
- Stuck locks are never broken automatically. A person clears them after being asked.  
  *Stangent's locks are OS file locks, released when a process ends, so none get stuck. A hung process still blocks until its timeout.*
- No client data goes into the tool's repo, and that is checked.  
  *`stangent data check` runs in CI (D113): tracked documents need a reason in `.stangent-data-allow`; client terms stay outside the repo. It reads the current tree, not git history, and CI has no terms file, so CI checks documents only.*
- Honest pilot notes record misses and what was changed because of them.  
  *End-of-phase reports and `decisions.md`.*

Ahead of Stangent because the review's main ask, "evidence provenance" (a proof trail for every finding), is already how this works.  
*Partly: Stangent checks citations but records no evidence strength or commit per finding.*

## 8. Proposed from comparing a fuller workflow (2026-11-04)

All proposed, none decided. Stangent's own file layout stays; only the ideas count. Letters give a suggested order: seams first (S), then A, C and E. *All built since (D120 to D124). What stays off until a test is on `roadmap.md`.*

**Seams, first**

- F.1 · Every component has a switch, and a run says which were on (D120).  
  *So the test phase can run the flow with each one off. Some exist: `history.enabled`, `fsd.findings`, `fsd.disprove`, `detectors`. Recommend: do first.*
- S.0 · A test of the whole flow with fake AI answers: import, links, promote, disprove, impact, as one run.  
  *`WholeFlowTests.cs`; what it found is in `phases/0-foundation.md`. Five things wait on the owner, as skipped facts.*
- S.1 · Links store a section's permanent id as well as its number.  
  *Links cite numbers, carried across on promote (D117). Keep the number for old hashes. Recommend: do.*
- S.2 · Import problems become structured: kind, section, detail.  
  *`problems` are plain lines, and comments use a fixed text shape no product code reads back. `source-document` v3. Recommend: do before the report and questions.*
- S.3 · One set of import steps every format fills in, and ticket attachments use the same path.  
  *Comments and tracked changes are Word only; `Pull.cs` converts `.docx` but not PDF, which D111 left open. Recommend: do.*
- S.4 · Connect or plainly label what Phase 1 and 0 built and nothing uses.  
  *Only graph code reads `opens-screen` lines; `ran` evidence has no source; Tickets don't use the evidence shape. Recommend: label now, connect when a step needs it.*

**Bringing a document in**

- A.1 · Re-read the same file with a newer build, through the candidate flow.  
  *`fsd reread <doc>`, from the archive's original, comparing text (D122).*
- A.2 · One readable import report with a verdict: OK, OK with warnings, or failed.  
  *`verdict` on the result and `import-report.md`, from structured problems (D122).*
- A.3 · Sensitivity labels stop the import, and a locked Word file gets its own stop with plain instructions.  
  *`document_labelled`, `--allow-label`, `docx_locked` (D122).*
- A.4 · When a Word file has no headings, list the bold lines that look like headings.  
  *In the `no_structure` error's details, up to 20, by paragraph. It still never guesses (D122).*
- A.5 · Excel follows the same steps: each sheet a section, merged cells expanded, hidden sheets and comments listed, values shown with the formula noted, very large workbooks flagged.  
  *`.xlsx` and `.xlsm`, by code (D123). No real Excel FSD has been seen: hand-built files only.*
- A.6 · Ids for tables, pictures and attachments, kept in `document.json`.  
  *`T-`, `F-` (D122), and `A-`, each with its section (D123).*
- A.8 · A section's fingerprint includes its attachments.  
  *`contentHash` beside `textHash`. The same words with other files read `modified`, naming the file; an older document falls back on the text (D123).*
- A.9 · A person approves the first import, in a spec-only PR.  
  *A switch, off by default: `fsd approve` (D122). Stangent opens no PR; a spec-only PR is the team's own.*
- A.10 · `fsd outline` and `fsd show <section>`.  
  *By code, by permanent id or number (D122).*
- A.11 · `fsd export`: one file per top-level heading, long chapters cut one level down, generated, never read back.  
  *Into `.stangent/fsd/<doc>/export/`, which git ignores; `document.json` stays the record (D122). The limit is a stated setting, 1500 lines.*

**Checking the document against the app**

- C.1 · Every section is linked or marked "not in code" by a person, with its attachments listed.  
  *`coverage` in `trace check`, and `fsd not-in-code`, by code; it reports and doesn't fail (D122).*
- C.3 · A decided finding becomes `recheck` or `gone`.  
  *`standing`, beside the drop-back, which is unchanged. A decided finding that goes away keeps its `X-` id; one nobody decided doesn't (D122).*
- C.5 · BA decisions `accepted` and `not a difference`.  
  *`accepted` and `not-a-difference`, in `trace-links` v4 (D122).*

**Everyday use**

- E.1 · A status for a document: where it stands, what's waiting, who it waits on.  
  *`fsd status` and `/stangent:fsd-status`, by code, with the next command (D122).*
- E.3 · Run one chapter and see its cost before the whole document.  
  *`spec-run.sh --chapter <n>` and `fsd disprove estimate --chapter <n>` (D122). The estimate covers the disprove stage only; nothing estimates the link judge.*

## 9. The owner's bulk decisions (2026-11-04, D125)

Marked down, not built. A ☑️ with a test note is taken in and stays off until that test. *What was still unbuilt or off on 2026-11-05 is on `roadmap.md`; the rest was built (D126, D131).*

**Improve what we have**

- I.1 · Pages generated only when a section or ticket needs them, as the normal path.  
  *Built: `spec needed`, `spec-run.sh --for-fsd` and `--for-ticket`; `--stale` no longer includes pages never written, `--new` does (D131). 113 of 718 ran on Sonarr.*
- I.5 · The diff shows `renamed` and `split` as their own outcomes.  
  *`renamed` already was. `split` shows apart, and stays a person's call (D126).*
- I.6 · Similarity ranks suggestions for doubtful matches and never decides.  
  *Built: `suggestions` in `fsd diff`, by shared words, no cut-off (D131).*
- I.7 · Embedded originals go to the archive, not the repo.  
  *Built: at any depth; the record says where (D126).*
- I.9 · Check prompt caching on headless calls.  
  *Looked: one-turn calls write 7.62M and read 0.72M, so caching mostly costs there; multi-turn pays. Not changed (D126, `phases/2c-spec.md`).*
- I.10 · Refused driver steps exit nonzero.  
  *Built: exit 1, the rest goes on (D126).*
- I.11 · Confirm a Word table of contents is removed, with a test.  
  *It wasn't. Fixed and tested; Sonarr byte-identical (D126).*

**Take: cheap, no AI**

- T.2 · A hard stop: an estimate and `--max-cost` before every paid step.  
  *Built: a cap or `none` is required; limits noticed; what code can't see is in `docs/foundation.md` (D126).*
- T.4 · A warning when a page is behind the code, where it's read.  
  *Built: `spec read`, `spec ask` (D126).*
- T.5 · A check that a run didn't change the app's files.  
  *Built: `run-check`, in the drivers (D126).*
- T.6 · Broken-link and duplicate-id checks.  
  *Built: `spec content-check`, in CI (D126).*
- T.7 · Generated navigation and footers.  
  *Built: links and a footer on each page, `specs/reference/index.md` (D131).*
- T.8 · Client code read-only by hook, for analysis steps only.  
  *Built: `read-only-hook.sh`; never on the reproducer (D126).*

**Not taking**

- Other tools' file layout and names.  
  *Owner.*
- Windows and GitLab-specific setup.  
  *D24 keeps these behind adapters.*
- Claude merging clashing edits without asking.  
  *Nothing can check it (D18).*
- Similarity cut-offs deciding matches.  
  *Tuned numbers; can flip meaning silently.*
- A repo survey agent.  
  *The graph does this job.*

## 10. From comparing a step-by-step journey workflow (2026-11-05, D129)

- J.2 · The FSD's journeys and scenarios stay.  
  *Extraction built; Sonarr gave 0 journeys from 109 requirements, and no real document has produced one yet.*

## Two caveats

- Most of the accuracy evidence comes from one app and small samples (one chapter, 22 findings). Stangent is tested on more codebases.
- The handoff between the reference and the FSD tool has no version check, so the strongest part of the list (section 6) depends on the reference's page format staying the same.  
  *Stangent has this covered: versioned contracts and `phases check` (D20).*

## From `roadmap.md`, frozen on 2026-11-05

History, not status: the marks below are how they stood. ✅ in Stangent, 🟡 partly, 🔨 being built, ☑️ decided and not built, ⬜ not decided, ❌ decided against.

### The overhaul: order of work

The one place for what comes next (D112). From the foundation up, applying `improvements.md`. Stops before Phase 4. ✅ when landed.

| # | Slice | What | Decisions | Needs the owner? |
|---|---|---|---|---|
| 0.1 ✅ | Evidence shape | One `evidence` contract, a Core type and validator: permanent id, citation, commit, strength, who or what checked it. Consumers move in 2.x | D18, D53, D62 | no |
| 0.2 ✅ | State changes | One Core module: a table of legal moves per kind, rejects the rest, keeps a history. Real lifecycles move onto it; result fields don't | D18, D58, D60 | no |
| 0.3 ✅ | Client-data check | `stangent data check`: terms kept outside the repo, tracked documents, run in CI | D56, D16 | no |
| 0.4 ✅ | Coverage measured | A script and a recorded number, no floor (D28) | D28 | a floor, later |
| 0.5 ✅ | Driver tests | The bash drivers tested from xUnit with fake `claude` and `stangent` on the path | D110 | no |
| 1.1 ✅ | Line citations | The graph answers symbol → `path:lines` at a commit, for 2.3 | D17, D25 | no |
| 1.2 ✅ | Playwright as evidence | Specs that open a screen become labelled links in the graph; `code-graph` bump if the shape changes | D45, D76, D86 | no |
| 2.1 ✅ | Word fails loudly | Tracked changes, moves, hidden text, comments, footnotes, embedded files, nested tables and vertical merges each stop the import or are listed | D52, D111 | no |
| 2.2 ✅ | Stable section ids | Ids that survive edits and moves; a new version comes in as a candidate with a section diff; doubtful matches go to a person; removed ids kept | D15, D99 | no |
| 2.3 ✅ | Stable statement ids | Ids never renumbered or reused; `path:lines` citations; commit stamped | D62, D54 | no |
| 2.4 ✅ | Findings carry evidence | Findings use 0.1 and 0.2; severity from one scale | D53, D68 | no |
| 2.5 ✅ | Disprove step | A second agent tries to refute every finding, reversing D62's "rejected"; page gaps go to developers; a "page is wrong" decision; an AI suggestion for a finding's severity (D118) | D62, D99, D58 | built and off by default; **the re-score is paid: prepared, not run** (estimate and command in `phases/2c-spec.md`) |
| 2.6 ✅ | 2c's leftovers | Only if they still apply after 2.5: `LinkRun.cs` review items, "no code found" on 56 of 109 | D99 | no paid run needed: three defects fixed; "no code found" is the failed links, not a bug |

Stop there. Phase 4 needs the owner: the order after 2.x, and the questions under "Open questions" in `decisions.md`.

**Follow-ups from 1.1 and 1.2 (D20, D115).** Done 2026-11-04: `code-graph` went to v4, and 1b, 1c, 1d, 2a, 2b, 2c and 3 were each checked by grep, tests re-run, and moved to `code-graph: 4`. Their code wasn't touched.

### Proposed, not decided

Every ⬜ and 🟡 item in `improvements.md`, with what it touches. The owner hasn't confirmed the list, so nothing here is a commitment until it moves to the order above or to "Decided, not built". Marks are as they stood before the overhaul; `improvements.md` holds the current ones. "Slice" points at the order of work. "Owner" means it waits on a question only the owner can settle.

| § | Item | Mark at start | Touches | Slice |
|---|---|---|---|---|
| 1 | Permanent id and `path:lines` per statement | ✅ | 2a, D62, D118 | done (2.3) |
| 1 | Ids never renumbered or reused | ✅ | 2a, D62, D118 | done (2.3) |
| 1 | Pages follow journeys | ⬜ | 2a, D54, D63 | Owner |
| 1 | Shared behaviour written once | 🟡 | 2a, D54 | Owner, with page shape |
| 1 | Today, required and findings kept apart, with ids | ✅ | 2a, 2b, D52, D118 | done (2.4); "a new app" isn't Stangent's job |
| 1 | Severity from one scale | ✅ | 2b, D68, D118 | done (2.4), set by a person; the AI's suggestion built (2.5), shown only |
| 1 | Second agent disproves findings; gap finders | 🟡 | 2b, D62, D119 | disprove built, off, not shown to help until the paid re-score (2.5); gap finders still to do |
| 1 | Call graph: every entry point on real C# | 🟡 | 1a, 4, D109 | Phase 4 (decided) |
| 1 | Adapters: Playwright, Razor, Svelte, SQL seed | 🟡 | 1b, D31, D65, D86 | 1.2 for Playwright; rest when a repo needs them |
| 1 | Generated navigation and numbering | 🟡 | 2a | after 2.3 |
| 1 | Script checks, including client data | 🟡 | 0, D18 | 0.3 |
| 1 | Client code read-only by hook | ✅ | 3, D60, D75, D86 | built, D126; analysis steps only |
| 1 | Runs reproducible, measured by a fresh session | ⬜ | 2a, D57, D79 | after 2.3; paid |
| 2 | Updates by overlapping lines | 🟡 | 2a, D55 | after 2.3 |
| 2 | Changed code no page covers is listed | ⬜ | 2a, D55 | after 2.3 |
| 2 | Stamp per repo for split apps | 🟡 | D4 | only if a real target is split |
| 3 | Fails loudly | ✅ | 2b, D111, D116 | done (2.1) |
| 3 | Messy Word files (parent item) | 🟡 | 2b, D111, D116 | 2.1 done; embedded Excel done (D123); EMF, draw.io, cross-references remain |
| 3 | Tracked changes including moves | ✅ | 2b, D116 | done (2.1) |
| 3 | Vertical merges expanded | ✅ | 2b, D116 | done (2.1); merges across columns stay blank |
| 3 | Embedded Excel per sheet | ✅ | 2b, D111, D123 | A.7 |
| 3 | PDF and PowerPoint | 🟡 | D111 | PowerPoint not decided |
| 3 | EMF to PNG | ⬜ | 2b | if a real document needs it |
| 3 | draw.io sources | ⬜ | 2b | if a real document needs it |
| 3 | Word cross-references as links | ⬜ | 2b | later |
| 3 | Lists everything not brought in exactly | ✅ | 2b, D111, D116 | done (2.1); not yet measured on a file saved by Word |
| 3 | Word comments become BA questions | 🟡 | 2b, D58, D116 | listed since 2.1; becoming questions still to do |
| 4 | Section ids survive edits | ✅ | 2b, 2c, D15, D99, D117 | done (2.2); a split or merge is a person's call |
| 4 | Candidate versions with a section diff | ✅ | 2b, D15, D117 | done (2.2) |
| 4 | Proven on a real version change | ⬜ | D56 | needs a document with two versions |
| 4 | First-run errors pinned as tests | 🟡 | 2b, D117 | a reviewed pair is pinned (2.2); no real first run yet |
| 5 | BAs change the FSD through Claude | ⬜ | D52, D12 | Owner |
| 5 | BAs never touch git | ⬜ | D99, D12 | Owner |
| 5 | A branch per BA, every edit a commit | ⬜ | D12 | Owner |
| 5 | Done is one step | ⬜ | D12 | Owner |
| 5 | Lead BA approves or sends back | 🟡 | D99 | Owner |
| 5 | Approval safety rules (parent item) | 🟡 | D99, D58 | Owner |
| 5 | Can't approve your own change | ⬜ | D99 | Owner |
| 5 | Approval never overwrites a later change | 🟡 | D58 | Owner |
| 5 | Approving asks for confirmation | ⬜ | D99 | Owner |
| 5 | Audit trail: who asked, who approved | 🟡 | D58 | Owner |
| 5 | Clashes between BAs | ⬜ | D12 | Owner |
| 5 | One command for what's next | ⬜ | plugin | Owner |
| 5 | One-line installer, pinned version | ⬜ | D1 | when BAs become users |
| 6 | Check against code or against the reference | 🟡 | 2b, D99, D119 | built, off (2.5): code first, unscored |
| 6 | Finder never verifies; fresh re-check | 🟡 | D62, D119 | built, off (2.5), unscored |
| 6 | Stamped with commit and evidence strength | ✅ | D53, 0, D118 | done (2.4) |
| 6 | Unverified until code confirms | 🟡 | D53, D68, D119 | built, off (2.5), unscored |
| 6 | "The reference is wrong" decision | ✅ | D58, D119 | done (2.5) |
| 6 | Only what changed is re-checked | 🟡 | D18, D58, D99 | 2.6 stopped one re-paid judge; a stale page's links still aren't judged again (2c step 6) |
| 6 | Reference gaps go to a developer | 🟡 | D99, D119 | built, off (2.5): sorted by the unscored step |
| 6 | Per-chapter report | ⬜ | 2b, D118 | still to do: 2.4 shows ids and severity in `trace check` only |
| 6 | Alias map for names | ⬜ | 2b | if real documents need it |
| 6 | Trial result (12 of 12) | ⬜ | D99 | a result, not a feature; the owner's denominator is open |
| 7 | All state changes through one module | ⬜ | 0, 2, 3, D18 | 0.2 |
| 7 | Every requirement traced to a test | ⬜ | 5, D10 | Phase 5 |
| 7 | Coverage floor, real tools | 🟡 | 0, D28 | 0.4 for measuring |
| 7 | No client data in the tool's repo | ⬜ | 0 | 0.3 |
| 7 | Hooks never block, tested | ⬜ | plugin | only if hooks are added |
| 8 | A test of the whole flow with fake AI answers | ✅ | 0, 2 | S.0; `WholeFlowTests.cs` |
| 8 | Every component has a switch, and a run says which were on | ✅ | all, D120, D121 | F.1 |
| 8 | Links store a section's permanent id as well as its number | ✅ | 2b, D117 | S.1 |
| 8 | Import problems become structured | ✅ | 2b, D111, D116 | S.2 |
| 8 | One set of import steps every format fills in, and ticket attachments use the same path | ✅ | 2b, 3, D72, D111 | S.3 |
| 8 | Connect or plainly label what Phase 1 and 0 built and nothing uses | ✅ | 0, 1, 3, D115 | S.4 |
| 8 | Re-read the same file with a newer build, through the candidate flow | ✅ | 2b, D117 | A.1 |
| 8 | One readable import report with a verdict | ✅ | 2b, D111 | A.2 |
| 8 | Sensitivity labels stop the import, and a locked Word file gets its own stop with plain in | ✅ | 2b, D111 | A.3 |
| 8 | When a Word file has no headings, list the bold lines that look like headings | ✅ | 2b, D111 | A.4 |
| 8 | Excel follows the same steps | ✅ | D111, D123 | A.5; hand-built files only |
| 8 | Ids for tables, pictures and attachments, kept in `document.json` | ✅ | 2b, D113, D123 | A.6 |
| 8 | Embedded files converted, linked from their section, and their text checked with it. A loc | ✅ | 2b, 2c, D53, D111, D123 | A.7; the check seeing them is off by default |
| 8 | A section's fingerprint includes its attachments | ✅ | 2b, D117, D123 | A.8 |
| 8 | A person approves the first import, in a spec-only PR | ✅ | 2b, D99 | A.9 |
| 8 | `fsd outline` and `fsd show <section>` | ✅ | 2b | A.10 |
| 8 | `fsd export` | ✅ | 2b | A.11 |
| 8 | Every section is linked or marked "not in code" by a person, with its attachments listed | ✅ | 2b, 2c, D53 | C.1 |
| 8 | Compare table facts by code | ✅ built, off, not shown to help (D124) | 1, 2b, D68, D56 | C.2 |
| 8 | A decided finding becomes `recheck` or `gone` | ✅ | 2b, D58, D118 | C.3 |
| 8 | The link judge can say `partial` and `unclear` | ✅ built, off, not shown to help (D124) | 2b, 2c, D53 | C.4 |
| 8 | BA decisions `accepted` and `not a difference` | ✅ | 2b, D58 | C.5 |
| 8 | Group small neighbouring sections into one unit when checking | ✅ built, off, not shown to help (D124) | 2b, 2c | C.6 |
| 8 | A status for a document | ✅ | plugin, 2b | E.1 |
| 8 | Ask "how does the app do X today?", answered from pages with citations | ✅ built, off, not shown to help (D124) | plugin, 2a | E.2 |
| 8 | Run one chapter and see its cost before the whole document | ✅ | plugin, 2b | E.3 |

**Order for §8 (D120, build first, test later):** F.1 and S.1 to S.4; then A.1 to A.4, A.6, A.9 to A.11, C.1, C.3, C.5, E.1, E.3; then A.5, A.7, A.8 (done, D123); then C.2, C.4, C.6, E.2, built switched off (done, D124). **Earlier suggestion:** S.0, then S.1 and S.2, then S.3, then Excel and attachments (A.5 to A.8), then C.1, then the C.2 pilot once its decision is made. The re-score comes before C.4 and C.6. Other A and E items fit in where they're small.

### Rows and cells replaced on the roadmap

Replaced by the items themselves, or shortened with the numbers kept in the phase report.

| What | Decisions | Phase |
|---|---|---|
| Journeys read from end-to-end tests by code, each step linked to its reference page; off by default, after the use-case runs, eShop first | D129 | 2a |
| Improve, still to build: pandoc as a second reader (I.12, held). I.1 to I.11 are built (D126, D131) | D125, D120 | when a document needs it |
| Take, still to build: Playwright settings for another repo; EMF, draw.io, cross-references and PowerPoint when a real document needs them (T.9, T.10, held). T.1 to T.8 are built (D126, D131) | D125, D120, D4 | next build |
| Take, then test (X.1 to X.19): off until its test, listed in `improvements.md` §9 | D125, D120 | test phase |

Phase 3's row:

| Phase | Delivers | Consumes contracts from | Provides contracts | Status |
|---|---|---|---|---|
| **3. Tickets** ([plan](phases/3-tickets.md)) | Jira ticket (through the Atlassian MCP) → context bundle for the developer's Claude Code: starting points, reference pages, FSD sections, impact, attachments; bugs reproduced by a failing unit or Playwright test before a fix; comments drafted by AI, posted by a person; proven on 50 Sonarr issues copied into Jira (D69–D81) | 0, 1a, 2 | `ticket-bundle` (v2), `ticket-update`, `bug-proof` | done, with two bars failed (D109) ([report](phases/3-tickets.md#end-of-phase-report)): right place Sonarr 90%, bitwarden held-out 88% ✅, beats keywords ✅, grounded ✅, repeatable ✅, Jira ✅, proof finds bugs 73% ✅; proof real 77% ❌, wrong reports 7/10 ❌, so the proof ships as advice only. Proof numbers predate the finder, D95, D104; re-run owed. Merged as [#2](https://github.com/hohkokyoung/stangent/pull/2), CI green |
