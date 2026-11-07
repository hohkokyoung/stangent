# Answered questions

Moved word for word from "Open questions" in `decisions.md` on 2026-11-05 (D136). Kept for their reasons and what was tried. What's still open is in [`questions.md`](../questions.md).

## Answered

### `page-gap` answers that can't settle it: D151, built off
- **`page-gap` answers that say the code can't settle it (found 2026-11-07, Sonarr re-score):** all 7 real findings the disprover took out of the BA's queue (5 planted, 2 hand-checked) were `page-gap`, each with a reason saying the code shown can't prove it. With the hand check, the queue is now 23% false alarms, so the noise bar holds at about 30% even if a few false ones come back. Recommended: code keeps a `page-gap` in the BA's queue unless its cited lines show the behaviour, the same check `stands` and `refuted` already pass; and the instructions say "can't see it" is `cant-tell`. Switched, off, then the same 62 re-asked (about $7). **Cost:** fewer findings leave the queue, so the 21% rises some. **Answered 2026-11-07 (owner: "Yes, build it"): D151, built off; routing to `developer-confirm` instead of the BA's queue, the assistant's call there.**

### The spec in its own git repo: kept in the app's repo for now
- **The spec in its own git repo? (owner asked, 2026-11-05, so a PM or BA sees an easier diff; owner: "okay we can keep it for now"):** the graph is settled, it isn't committed anywhere (D25). For the spec, recommended: stay in the app's repo (D7, D12) and fix the diff itself, with spec-only PRs and the generated files collapsed. Kept in the app's repo for now. Move when a real project needs what only a split gives: BAs without access to the code, client text kept away from developers, or one spec over several code repos. **A split would cost:** one PR no longer holds both spec and code (D7's reason), each link has to record two commits, a code branch and a spec branch can disagree, and paths are written in about 23 C# files.

### Editing the parsed FSD directly: D128, with the warning built in D131
- **Editing the parsed FSD directly (owner asked, 2026-11-05):** not supported, by D52: the FSD is the file a person changed, converted by code. Tried on a scratch copy: a hand edit to `document.md` changes nothing the steps read (`fsd show` still gives the original, since sections come from `document.json`), and `fsd status`, `spec content-check` and `trace check` say nothing; only `fsd reread` notices, by starting a candidate. Editing `document.json` wasn't tried. Recommended: a warning where the document is read, small, added to batch 2 because a test showed the need. Direct editing is now D128.

### Which project the pilot runs on: D127, then the public projects on `roadmap.md`
- **Which project the pilot runs on (2026-11-04):** the owner has no project of their own to use ("honestly i dont have my own project sooo") and asked for a reputable public one that has what we need. Sonarr is the tuning set and bitwarden the held-out set (D100), so neither fits. Recommended, not yet confirmed: **umbraco/Umbraco-CMS** (MIT, C# with a TypeScript frontend in one repo, .NET 10, Playwright with its own helper package, about 1,700 closed issues with a linked fix). Runner-up: Squidex/squidex, whose Angular frontend is closer to D4 but which has about 12 such issues. Differs from D4: its frontend is neither Angular nor React. **What a public repo can't give:** no real FSD, so the gap in the handoff stays; Umbraco's user docs are a separate repo and aren't an agreed spec. A public repo may be known to the model, so results may look better than on a private one.
- **A public repo with a real FSD (searched 2026-11-04):** none found that's reputable, an application, and C# or TypeScript. About 300 repos have a file named like a functional spec; those in our languages are mostly libraries, student projects or files written for AI agents. GitHub's search can't read Word or PDF files or outside wikis, where real FSDs usually live, so this doesn't show there are none. Nearest: Apache Fineract (Java, 2.6K stars, functional specs on Apache's wiki, its 1.0 page kept for historical reasons); openIMIS (functional specs on its wiki, its own project team says; the page couldn't be read). HydroServer's README names `specs/functional`, which isn't in its public tree.
  **The owner then found RealWorld (2026-11-04):** `realworld-apps/aspnetcore-realworld-example-app` (C#, 2.1K stars, MIT, 106 `.cs` files) implements the written spec in `realworld-apps/realworld` (84K stars), which also holds about 160 Bruno API tests, 13 Hurl files and Playwright tests. **What it gives:** a real spec nobody wrote for Stangent, and an answer key made by code: run the spec's tests on commits from before the repo's spec fixes (for example #134, "adapt to current realworld spec", and `wrong email format as 422`), and each failure is a real gap between spec and code. The same spec has Go, Angular and Vue implementations, so it can also test other stacks later. **What it can't give:** it isn't an FSD in shape (short API and template notes, no business rules or numbered chapters); it's Markdown, which `fsd import` doesn't take, so it needs converting and tests links, not Word import; the app is small, so impact isn't stressed; it has 52 closed issues; and the model very likely knows this spec, so it may answer from memory, which could flatter the links. It also pins .NET SDK 10.0.403 and this machine has 10.0.302. Recommended, not confirmed: RealWorld first for the FSD side, Umbraco after for size, tickets and helpers.
  **A bigger spec (owner, 2026-11-05: "feel like i want something with bigger spec"):** the planted Sonarr FSD is 1,136 words, about 2-3 pages, so 24 of 30 and 44% say little about a real one. Recommended, not confirmed: `umbraco/UmbracoDocs` beside Umbraco itself (MIT, written by people, 7,180 merged PRs). Its `18/umbraco-cms` is 562 Markdown files, 3.37 MB, about 500 times the planted FSD, and a chapter can be taken at a time. Runner-up: the FHIR R4 spec with `microsoft/fhir-server` (normative SHALL rules, closer to an agreed spec, but no screens, and 48 closed issues with a linked PR). **What it can't give:** product docs describe, they aren't an agreed FSD, so a difference may mean the docs are stale; no answer key exists yet (one search for issues quoting the docs found 4, so one would be built from docs-fix PRs); `fsd import` doesn't take Markdown, a need real use has now shown under the proposed cap; and running it whole isn't affordable, so it depends on I.1.
  **Decided as D127 (owner, 2026-11-05).** The notes below stay for the reasons. **The owner's own project (owner, 2026-11-05: "cant we use my cyberbullying thing docx, and we cater our stangent to handle that codebase also"):** `cyberbullying-detection-system` on GitHub. It's a PDF, not Word, and Python (Flask, 35 route lines) with React, not PHP. `Documentation.pdf` imports with 110,496 words and 340 sections, about 100 times the planted FSD, verdict `ok-with-warnings`, 77 problems (not yet read). Recommended, not confirmed: use it for the links, reversing D56's note that links can't run on it. **For it:** real text not written for Stangent, little known so the AI can't answer from memory, code small enough that AI steps are cheap, and the owner wrote it, so a person can rule on each finding (D18). The need for a Python extractor is shown by real use. **Against it:** it's a thesis, so only part is spec (chapters 6, 7, 9 and 10 hold about 48,000 words, how much is rules isn't measured) and it may describe what was already built, so disagreements may be few; 0 issues and 2 commits, so no tickets or history; the detection model is in notebooks the graph won't read; nothing here tests C#. **Privacy:** the PDF holds a name and student id, and this repo is public, so results stay outside the repo and the phase file holds counts only. The clone comes fresh from GitHub, because the local copy has an untracked `docs/` with earlier notes and an issue list that must not reach the AI steps. Rough estimate for a Python extractor and Flask detector: 200-350K tokens.
  **Also looked at, not recommended (2026-11-04):** `suragms/warehouse-assistant-erp`. The closest match to D4's stack (.NET 10, React, Playwright, 11 `.csproj` in one repo), but 0 stars, one author, 42 commits in two weeks, no issues or PRs, so it has no tickets and isn't reputable. Building it runs an unknown author's restore and install steps on this machine, so it stays unbuilt.

### Ticket retrieval redesign: decided as D101
- **Ticket retrieval redesign (owner, 2026-11-02; proposed, not decided):** right place can't pass with pages as the
  retrieval unit: by the D98 stage table, the best case is 26/40 = 65%. Proposal: the ticket enters at the code graph,
  not at pages. Stack traces, code search over files/names, and the AI picker all run (a match never switches the AI
  off); candidates are merged, and code checks each one exists in the graph; the graph expands towards the bug; pages explain the
  code; FSD links judge it. The bundle keeps candidate files first, then pages, then impact. Gaps it closes:
  (a) **code no page cites:** the page is looked up in order: cites the file → its slice holds the file → walk the graph back
  to the nearest entry points that have a page (same hub stop as page slices, `spec.hubCallers`; ties ranked by
  ticket words; the bundle budget decides how many fit). Each page is labelled "describes this file", "includes this
  file" or "nearby: covers the feature, not this file"; none found → the bundle says so (D88). (b) **FSD only hangs off
  pages:** FSD links come with whatever page was found, carrying the same label; direct FSD↔code links stay out
  (D68 noise). Open: whether a "nearby" FSD link may count toward "working as agreed" (recommended: no, context only,
  a person decides; asked 2026-11-02, not yet answered); a strong baseline first (headless coding agent and an Agentless-style narrowing on the 40 bugs,
  ~$10–20) to choose between our own search and an agent's; a held-out split or second repo before tuning; report files
  handed over per bug next to recall. **Agent baseline pilot (2026-11-02, phases/3-tickets.md):** on 10 bugs our pipeline
  misses, a headless coding agent with only the ticket and the code named a fix file first in 9, at $0.04 a ticket
  (caveats: public repo the model may know; 6 tickets name the class; 10 bugs). **Full baseline (2026-11-02,
  fresh-checked):** Sonarr 36 of 40 (90%; ours 19), bitwarden held out 23 of 24 (96%; strict 20 of 21), median 4 files
  named, $3.11 for 64 tickets. **Decided as D101** (owner, 2026-11-02). Free first checks: the bundle-order fix (14 → up to 19 shown), and the
  backward walk on the 12 page-gap bugs, started from each real fix file (so it tests explaining, not finding): code
  scores whether it reaches a page, how far, how many ties, and, where the bug has a D87 right page, whether it lands
  on one; where it has none, a fresh agent judges whether the page is about the ticket's feature. Each miss is
  labelled dead end, right page not written, right page among the ties ranked lower, or wrong area.
  **Measured 2026-11-02 (phases/3-tickets.md):** the nearby page explains the fix in 1 of 12 page-gap bugs (fresh agent:
  helps 1, partly 7, no 1; 3 dead ends). It gives area context, rarely the mechanism, so gap (a) is not closed by it:
  keep it only as labelled context beside the file pointers. The gaps are provider/plugin code dispatched by interface
  or name, and framework-run code (converters, migrations, housekeepers); also an undetected `IExecute<T>` command
  handler (SONB-32) and a hub made by test callers (SONB-35: slices count tests towards `spec.hubCallers`).

### Order of the next work (2026-11-02): replaced by D112 and `roadmap.md`
- **Order of the next work (owner, 2026-11-02: agreed; (1) done, (2) under way, held-out set: D100):** clean up the
  foundation only where a measured loss traces to it: (1) the free checks first (the bundle-order fix and the nearby-page walk check; no weights, so nothing is tuned
  on the 40 bugs), whose dead ends aim the graph work; (2) the benchmark: a tuning/held-out split, a second repo, the
  strong agent baseline; (3) the graph: the missing entry-point links the walk check found, and a search over files and
  names; (4) Phase 3's retrieval redesign on top, re-measured on the held-out set; (5) D99 once a project with an FSD
  makes it measurable. Reference pages are
  not rewritten. Before starting: commit or set aside the Phase 3 worktree's uncommitted work (owner's OK), and apply
  D20 to any contract change.

### Who rules on findings on the cyberbullying project: D137
Raised 2026-11-05 after the owner said: "this cyberbullying is just for testing anyway, and im not doing this project anymore so we can just say whatever". D137 answers it: false alarms aren't counted from PM rulings on any project, and that project's rulings count toward no bar. Login's 13 decisions are in `phases/2c-spec.md`.

## Parts of "Waiting on the owner after the overhaul"

Item (b) is done, and item (f) was built as I.10 (D126). The rest of the list is in `questions.md`.

  (b) The fresh check on 2.5 and 2.6 is done, with its fix round (`phases/2b-fsd.md`, D119's note).
  (f) The drivers' refused steps still exit 0 (pinned by a test, Phase 0). Recommended: exit nonzero.
