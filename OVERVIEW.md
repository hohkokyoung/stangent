# Stangent in plain words

*What the project does, part by part, with an example of each. Written 2026-11-11 from `spec.md`, `roadmap.md` and `decisions.md` on branch `claude/phase2c-spec` (worktree `.claude/worktrees/phase2c-spec`); updated 2026-11-15 from `roadmap.md`, `decisions.md` (to D339), `phases/r4-pages.md` and the handoff note. If this page and those files disagree, they win; fix this page.*

## The one-paragraph version

Stangent is a set of tools that helps an AI do software work **without making things up**. It reads a project's code and writes down, as facts, what the code does: which button calls which server endpoint, what gets saved to the database, what is checked along the way. Later steps (writing specs, linking them to code, handling tickets, and eventually writing code, tests and PRs) work from those facts instead of from an AI's guess. The rule behind everything: **code decides whatever code can decide; an AI only puts things into words, or picks from options code laid out; and code or a person checks every AI output** (D18, D155).

It's a .NET command-line tool (`stangent`) plus a Claude Code plugin that calls it. It works on .NET backends and React or Angular frontends, and it's tested on three real open-source apps: **Sonarr** (a TV-show downloader), **Kavita** (an ebook and comic reader) and **nopCommerce** (an online shop).

## The full pipeline you asked for, and where each piece stands

| # | Piece (from `spec.md`) | In plain words | Status |
|---|---|---|---|
| 2 | Code graph | A map of the code: what calls what, front to back | **Rebuilt and checked** (layer 2) |
| 1 | Requirements / spec | What the app should do, written as checked facts | Flows done (layer 3); developer pages parked (layer 4); checking an FSD is done by an agent reading the code, with code checking its quotes (layer 5) |
| 7 | Tickets | Pull a Jira ticket, hand the developer everything relevant, draft the reply | **Closed (layer 6, D340):** Phase 3's version kept, plus impact told to the developer's session after a test showed it helps name whole fixes |
| 9 | Agents | Split a ticket into small tasks and run AI helpers on them | Not started |
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
**Where it stands:**
- **Gate 1, nothing lost (by code):** every change and stop in a flow is on its page, rebuilt byte for byte. Passes on every area of all three apps.
- **Gate 2, the page beats the flow file:** a fresh reader answers 4 questions on 9 clicks from the pages, graded against the source. 28 of 36, then 33, then **35 of 36: passed** (bar 34; the flow file scored 31).
- **Gate 3, would a developer open it before the code:** you left it to my call, so a fresh agent read three pages. Add New: yes. Kavita Reading Lists: no. Sonarr Series: partly. After eight runs it still didn't pass, and in a task test the page helped an agent no more than the code alone, so it's parked: code kept, switched off.

### Layer 5 — Checking an FSD ✅ settled, parked parts (D337)
**What it does:** an agent reads the client's FSD and the code and lists gaps, conflicts and things the FSD leaves out. Code checks every FSD quote word for word and every `file:line`. Two runs are merged.
**Why this way:** on planted test FSDs the agent alone beat the old page-based linking (26 of 30 found, 8% false alarms, against 24 and 31%). A code-made list of every control (the "PM inventory") was built to three of seven steps and parked: it could only win by 1–2 items on one small test, after about a million more tokens.
**Example:** Kavita's FSD never mentions the "Remove from On Deck" menu entry, so it's reported as left out.

### Layer 6 — Tickets ✅ closed (D340)
**What it does today:** `stangent ticket <KEY>` pulls a Jira ticket and builds a bundle for the developer's Claude Code. The bundle holds where in the code to start, the related spec pages and FSD sections, what might break, and the attachments. For a bug, it tries to reproduce it with a failing test before anyone fixes it. It drafts the Jira comment, and a person posts it.
**What changed (D338):** starting points stay with the agent search, which found the fix file for 36 of 40 bugs (code alone found 19). **Tested (D339):** on 20 Sonarr bugs whose fix touched several files, an agent with `graph impact` named the whole fix 8 times, without it 3 times (a small, fragile gain: better on 5 bugs, worse on 4). So `/stangent:ticket` now tells the developer's session to run it on each file it will change. Comment templates (P7) weren't built: drafts already have every claim checked by code.
**Still owed:** a re-run of the bug proof, and a test of whether the bundle helps an agent actually fix a bug.

### Layer 7 — Cuts ⏳
**What it will do:** remove AI steps that never proved they help (P8). The rule is D138: a part that fails its target gets one fix attempt, and if it fails again it's removed.

## How work gets checked (why things take the time they do)

- **The bar, set before building:** each layer gets a target agreed in advance, and it can't move afterwards. Layer 3's was: no wrong ✅, at least 95% of steps found, every guess explained, rebuilds byte-for-byte identical, nothing skipped silently (D177).
- **The answer key:** a fresh AI agent reads the app's source and writes down what the right answer is. You approve some of it, and stangent's output is scored against that key.
- **Fresh checks:** after building, a separate agent with a clean context looks for mistakes. That way the agent that built something never grades its own work (D61, D158).
- **General fixes only:** a fix that helps only one of the three test apps counts as a fault, because the rules have to work on any project.

## Where things stand today (2026-11-15)

- Work is on the `claude/phase2c-spec` worktree, ahead of `main` (`main` stops around D88); 57 commits aren't pushed.
- So far, side-by-side tests show an agent reading the code does as well as Stangent's prepared material, or better. What has paid off is code checking the AI's answers, and `graph impact`. Layer 6 is built around that.
- Layer 6 closed (D340). Next: layer 7, cutting AI steps that never proved they help (P8).

## Words you'll see

| Word | Means |
|---|---|
| **D123** | Decision number 123. One line each in `decisions.md`, full text in `decisions/` |
| **Layer / Phase** | Layers are the rebuild's order (1–7). Phases (0, 1a–1d, 2a–2c, 3…) are the original build, still running |
| **FSD** | Functional spec document: the client's Word file describing what the app should do |
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
