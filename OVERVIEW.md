# Stangent in plain words

*What the project does, part by part, with an example of each. Written 2026-11-11 from `spec.md`, `roadmap.md` and `decisions.md` on branch `claude/phase2c-spec` (worktree `.claude/worktrees/phase2c-spec`); updated 2026-11-16 from `roadmap.md`, `decisions.md` (to D359), `phases/r4-pages.md` and the handoff note; updated 2026-11-18 from `roadmap.md`, `decisions.md` (to D397) and `phases/fs-fsd-source.md` on branch `claude/fsd-source` (worktree `.claude/worktrees/admiring-edison-d9620d`). If this page and those files disagree, they win; fix this page.*

## The one-paragraph version

Stangent is a set of tools that helps an AI do software work **without making things up**. It reads a project's code and writes down, as facts, what the code does: which button calls which server endpoint, what gets saved to the database, what is checked along the way. Later steps (writing specs, linking them to code, handling tickets, and eventually writing code, tests and PRs) work from those facts instead of from an AI's guess. The rule behind everything: **code decides whatever code can decide; an AI only puts things into words, or picks from options code laid out; and code or a person checks every AI output** (D18, D155).

It's a .NET command-line tool (`stangent`) plus a Claude Code plugin that calls it. It works on .NET backends and React or Angular frontends, and it's tested on three real open-source apps: **Sonarr** (a TV-show downloader), **Kavita** (an ebook and comic reader) and **nopCommerce** (an online shop).

## The full pipeline you asked for, and where each piece stands

| # | Piece (from `spec.md`) | In plain words | Status |
|---|---|---|---|
| 2 | Code graph | A map of the code: what calls what, front to back | **Rebuilt and checked** (layer 2) |
| 1 | Requirements / spec | What the app should do, written as checked facts | Flows done (layer 3); developer pages parked (layer 4); an FSD is checked by `stangent fsd review`: an agent reads the code, code checks its quotes (layer 5, built, live run owed); the old route and the requirement steps cut (layer 7); **the FSD now lives in the repo as Markdown chapters the PM edits, and is the source of truth (phase fs, done 2026-11-18)** |
| 7 | Tickets | Pull a Jira ticket, hand the developer everything relevant, draft the reply | **Closed (layer 6, D340):** Phase 3's version kept, plus impact told to the developer's session after a test showed it helps name whole fixes |
| 9 | Agents | Split a ticket into small tasks and run AI helpers on them | **Minimal fix loop built and run 2026-11-16 (D343–D357):** one fix agent per bug (`fix-run.sh <KEY>`, `/stangent:fix`), from the bug proof's failing test, every check run by code, on its own branch, never pushed. First real run (D345): it fixed all 12 Sonarr unit-test bugs with just the ticket; the bundle didn't help and cost 27% more tokens, so the fixer now gets the ticket, the test and the finder's file list; splitting waits for a measured need |
| 4 | Testing | Tests picked from the map and tagged with the requirement they prove | Not started |
| 5 | PR | A review package that's quick for a senior to read and comment on | Not started |
| 6 | CI/CD | Deploy on GitHub Actions, easy to watch and retry | Not started |
| 8 | Dashboard | One place to see spec ↔ code ↔ tests ↔ deploys | Not started |
| 3 | Demo | Quick clickable prototypes before real code | Not started |

## Why there's a rebuild

On 2026-11-07 the spec-to-code links failed their targets: they found 24 of 30 known links with 44% false alarms. Most of the failures came from **an AI being asked to find or decide too much**. So you approved D155 ("deterministic first") and D156: rebuild from the bottom up, in layers, and **don't touch a layer until the one under it passes its checks**. The old versions keep running until each rebuilt layer replaces them.

## The layers, one by one

### Layer 1 — Foundation ✅ built and checked
**What it does:** the plumbing. Every run records which model, effort and settings it used. A repeatability check runs an AI step three times on the same input and compares the results by code.
**Example:** if a step gives three different answers to the same question, the check flags it as unreliable.
**Caveat:** so far it's tested only with fake AI runs. A check with real Claude Code is still owed.

### Layer 2 — Code graph ✅ built and checked
**What it does:** reads the C# (with Roslyn) and the TypeScript (with its own extractor), and stores every function and every connection between them in a database (`.stangent/graph.sqlite`). It also stores facts: button labels, permission checks, validation rules, database writes, outside calls, background jobs, and the condition each call sits under.
**Example:** in Sonarr, it can follow the "Add series" button → the React click handler → the app's state store → `POST /api/v3/series` → the C# controller method → the database write.
**Answers it gives:** "If I change this method, what else might break?" (`stangent impact`).

### Layer 3 — Flows ✅ built and checked
**What it does:** turns the graph into **flows**, by code alone, no AI. A flow is one thing a person or the system does, step by step: who starts it, what's checked, each step and the condition around it, where each branch ends, and what it leads to. Every step points to the line of code that proves it, marked ✅ proved, 🟡 a labelled guess, or ❓ can't see.
**Example flow:** Kavita's "Want to Read": the button → the frontend call → the endpoint → the user check → the save to the user's list.
**Where it stands:** finds 95–96% of what hand-checked answer keys hold, with no wrong ✅. Flows are grouped into **areas**, the app's parts as its menu names them (Sonarr 63, Kavita 28, nopCommerce 142), which you confirmed. Gate 2 of layer 4 then showed real effects layer 3 didn't mark, so it learned them (D291–D305): browser pushes, file writes, timers, plugin calls to outside services, and errors that are caught further up ("handled", not stops). Its file format went to `flows` v2.

### Layer 4 — Developer pages ⏸ parked (D323)
**What it does:** writes one `page.md` per area for a **developer**, by code, no AI (D278, D285). For each click that changes something: a chart with the code's own labels and step numbers, a table of every way it can go (refused, outside service failing, skipped), what runs afterwards on its own, and what happens in shared code.
**Example:** Sonarr's Series › Add New page, click "Add {title}": "calls SkyHookProxy, outside (step 37)", "saves Series (step 133)", and afterwards the three handlers of `SeriesAddedEvent`.
**Where it stands:** it passed gates 1 and 2 (nothing lost; a fresh reader got 35 of 36 answers right from the page). Gate 3, "would a developer open it before the code", didn't pass after eight runs, and in a task test the page helped an agent no more than the code alone, so it's parked: code kept, no further work (it's written with everything else now that switches are gone, D381).

### Layer 5 — Checking an FSD ✅ built (D341), 🔶 first live runs 2026-11-18: gaps and conflicts good, undocumented weak
**What it does:** `stangent fsd review <doc>` (or `plugin/scripts/fsd-review-run.sh --max-cost 8 <doc>`). An agent reads the FSD and the code, read-only, and lists gaps, conflicts and screens the FSD leaves out. Code checks every FSD quote word for word and every `file:line` at the reviewed commit, gives one retry, and drops what still fails. Two runs are merged into `specs/fsd/<doc>/review.md`.
**Why this way:** on planted test FSDs the agent beat the old linking on every number (26–29 of 30 found, 0–8% false alarms, against 24 and 31%). On the saved Kavita runs the merged command found 29 of 30.
**Example:** Kavita's FSD never mentions the "Remove from On Deck" menu entry, so it's reported as a screen action the FSD leaves out, with the code line that proves it.
**Live (2026-11-18, D399–D401):** the first live runs found only 2–17 of 30, because the built prompt had drifted from the measured one (the FSD pasted in, matches asked for, a structured answer). Back on the measured prompt, with the FSD and the answer as files, each run finds 17–18 of 30 with almost no false alarms: 18 of 20 gaps and conflicts, but only 2 of 10 things the FSD leaves out (9 when measured). Matches for the truth map are paused until they're measured as a step of their own.
**Caveat:** one app, an AI-planted FSD; a full run that records and merges through the fixed write rule is still owed.

### The FSD in the repo, and the PM's rulings ✅ built (phase fs, D376–D397; truth map, D361–D374)
**What it does:** the client's Word, PDF or Excel file is converted once; after that, the FSD is Markdown files in the repo, one per chapter (`specs/fsd/<doc>/chapters/`), each heading carrying a hidden permanent id (`<!-- S-0012 -->`) so a rename never loses its rulings. Steps read only what's **committed**: a commit is the approval (D380). The PM rules on each finding of the review ("the code is right", "the FSD is right", or their own words) by number, through Claude (`/stangent:rule`), and `stangent fsd write` turns those rulings into the FSD's own sentences, a "code is right" one drafted by AI and checked by code against the real code line. `stangent fsd check` runs before every commit and refuses anything that could put a ruling on the wrong section. A newer client file is never imported over the PM's edits: it's compared three ways, section by section, and `stangent fsd take` copies in only what the PM picks (with its attached files).
**Example:** Kavita's FSD says "Deleting a series deletes its folder"; the code asks first. The PM answers "12 code", `fsd write` drafts "Deleting a series asks whether to delete its folder", `fsd check` passes, the PM reads the diff and commits; a ticket "folder not deleted" is then judged against that sentence.
**Also in this phase:** every on/off switch was removed (D381): a feature is on once built and checked, and one that looks useless is raised with you first. The Word export was cut (D397); `stangent fsd export --whole` gives one Markdown file.
**Caveat:** tested only on test files and the benchmark apps' planted FSDs; no real client's two versions yet, and the AI drafting step hasn't had a paid live run. The truth map's own measurement (D375) is still owed.

### Layer 6 — Tickets ✅ closed (D340)
**What it does today:** `stangent ticket <KEY>` pulls a Jira ticket and builds a bundle for the developer's Claude Code. The bundle holds where in the code to start, the related spec pages and FSD sections, what might break, and the attachments. For a bug, it tries to reproduce it with a failing test before anyone fixes it. It drafts the Jira comment, and a person posts it.
**What changed (D338):** starting points stay with the agent search, which found the fix file for 36 of 40 bugs (code alone found 19). **Tested (D339):** on 20 Sonarr bugs whose fix touched several files, an agent with `graph impact` named the whole fix 8 times, without it 3 times (a small, fragile gain: better on 5 bugs, worse on 4). So `/stangent:ticket` now tells the developer's session to run it on each file it will change. Comment templates (P7) weren't built: drafts already have every claim checked by code.
**Still owed:** a re-run of the bug proof, and a test of whether the bundle helps an agent actually fix a bug.

### Layer 7 — Cuts ✅ built and committed (D342)
**What it did:** removed what layer 5 replaced and what never proved it helps (P8): the old link route behind `/stangent:fsd-link` (candidate finder, link judge, agree checker, disprover), requirement steps and the ticket matcher, claims, workflow files, test journeys, `spec ask` and the security flag. About 15,500 lines; build clean, 1861 of 1861 tests, fresh check found no breaks.
**Example:** `stangent fsd prepare` is gone; `stangent fsd status` now says the next step is `fsd-review-run.sh`.
**Caveat:** half the cut. The old reference pages and `specs/trace.yaml` stay, frozen, because ticket starting points and bundles still read them. They go once a ticket run without them shows the starting points hold.

### Phase 4 — Fixing a bug ✅ built and first run (D343–D357)
**What it does:** takes a bug whose failing test the bug proof already wrote, and has one AI agent fix it in a copy of the repo on its own branch (`stangent/fix-<KEY>`). The agent says what it changed, where and why (a quote from the ticket, say). Code then checks five things in order: the plan's places and quotes are real, it only edited what the plan reaches, it builds, the failing test now passes with nothing else broken, and the test really runs the changed code. One retry; then a person. Nothing is ever pushed.
**Example (made up: nothing real has run yet):** `fix-run.sh --max-cost 3 SONB-14` → branch `stangent/fix-SONB-14` and `report.md`: "fixed · plan: `CommandResource.cs` · why: ticket 'the server crashes on a nested body' · 5 checks ✅ · $0.62 of $3".
**First real run (D345, 2026-11-16, $2.20):** on 12 Sonarr bugs, the agent fixed every one with only the ticket and the failing test, and the same 12 with the whole bundle for 27% more tokens. So the bundle doesn't earn its cost on bugs like these; the fixer now gets the ticket, the test and the file finder's list of files. Caveats: these bugs were easy enough that nothing extra was needed; on 6 of them the developer changed 2–3 files and the agent changed one, and a fresh judge found 3 of those green fixes left part of the bug (the same flaw in a second place), on both sides (D359); the 5 browser-test bugs can't be checked yet.

## How work gets checked (why things take the time they do)

- **The bar, set before building:** each layer gets a target agreed in advance, and it can't move afterwards. Layer 3's was: no wrong ✅, at least 95% of steps found, every guess explained, rebuilds byte-for-byte identical, nothing skipped silently (D177).
- **The answer key:** a fresh AI agent reads the app's source and writes down what the right answer is. You approve some of it, and stangent's output is scored against that key.
- **Fresh checks:** after building, a separate agent with a clean context looks for mistakes. That way the agent that built something never grades its own work (D61, D158).
- **General fixes only:** a fix that helps only one of the three test apps counts as a fault, because the rules have to work on any project.

## Where things stand today (2026-11-18)

- Work is on `claude/fsd-source` (worktree `admiring-edison-d9620d`), 358 commits ahead of `main` and not pushed.
- Phase fs is done: layers 0–5 built, each fresh-checked and committed; the Word export cut. 1,970 tests passed at the last full run (D396).
- Next, paid and each asked first: the live FSD review, the truth map's measurement (D375), the live `fsd write` run, a ticket run without the old pages, the bug-proof re-run.
- Recent calls left to me, worth re-reading: D389 (how phase fs was cut into layers, `fsd migrate`, no git hook), D390 (`ai.enabled` kept; an old setting is ignored with a warning, not refused), D393–D396 (what `fsd check` refuses, what `fsd write` lists instead of guessing, `take` never over a PM's edit).

## Words you'll see

| Word | Means |
|---|---|
| **D123** | Decision number 123. One line each in `decisions.md`, full text in `decisions/` |
| **Layer / Phase** | Layers are the rebuild's order (1–7). Phases (0, 1a–1d, 2a–2c, 3…) are the original build, still running |
| **FSD** | Functional spec document: what the app should do. It arrives as the client's Word, PDF or Excel file; after import it's the chapter files in the repo |
| **Chapter file** | One Markdown file per FSD chapter, under `specs/fsd/<doc>/chapters/`, the copy the PM edits |
| **Ruling** | The PM's answer to one review finding, kept in `rulings.yaml` until it's written into the FSD's words |
| **Point** | A function or method in the graph |
| **Flow** | One user or system action, laid out step by step by code |
| **Area** | A group of flows (one part of the app, as its users name it), confirmed by a person once |
| **Tie** | A flow two or more areas could hold; code lists the choices and a person picks one |
| **✅ / 🟡 / ❓** | Proved by code / a labelled guess with its reason / code can't see it, with why |
| **Bar** | A layer's pass/fail target, frozen before it's built |
| **Key** | The hand-checked right answers a layer is scored against |
| **Fresh check** | A review by a new agent with no memory of the build |
| **Contract** | The agreed file format one layer hands to the next (e.g. `flows` v2); changing one flags the layers that read it |
| **Gate** | One of layer 4's three pass/fail checks, run in order (D280) |
| **Handled error** | An error some code higher up catches, so the flow doesn't stop there |
| **Shared guard** | A refusal many flows share (a login or role check); the page counts it instead of repeating it |
| **Bundle** | Everything a developer's AI needs for one ticket, put together by code |

## Keeping this page current

Update this page when a layer changes status, or when a decision changes what a part does. One line per change is enough. If it grows past two screens, cut it down rather than adding to it.
