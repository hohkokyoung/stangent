# Decisions

Every settled decision, one line each, settled while grilling `spec.md`. Each full entry says what was decided, why, and what it costs, and is in `decisions/`: find it with `grep -n "^## D120 " decisions/*.md`.

- **Changing a decision:** edit its entry and note the date; don't silently contradict it elsewhere. Add a "→" note here if it changes an older one.
- **Adding one:** the full entry goes at the end of the newest file in `decisions/`, plus one line here, in the same turn. D numbers are never reused or renumbered.
- **"→" notes** show only what the entries themselves say, so an entry with none may still have been refined.
- **Open questions** moved to [`questions.md`](questions.md) on 2026-11-05; older text saying "Open questions in `decisions.md`" means that file. Answered ones are in [`decisions/answered-questions.md`](decisions/answered-questions.md).

## D1–D24: foundation and the brief

[`decisions/1-foundation.md`](decisions/1-foundation.md)

- D1 · Built for the owner's own use first
- D2 · Tickets: Jira
- D3 · PRs on GitHub
- D4 · Target codebase shape → widened by D127
- D5 · Build from the ground up, contracts first
- D6 · Benchmark: three public repos
- D7 · Source of truth: files in the repo
- D8 · Portability through versioned schemas and one store interface
- D9 · Each product is a CLI
- D10 · Requirement ↔ code links live in a link file
- D11 · A living spec of the whole system, owned by BA/PM → revised by D99
- D12 · Specs are written in the codebase, BA/PM edit them through the dashboard
- D13 · Spec sources: existing documents first, code as fallback, provenance on every requirement → revised by D52, D99
- D14 · One full audit upfront, then kept current per area → audit replaced by D99
- D15 · Source documents: many formats, latest wins, every version kept → latest-file-wins reversed by D128
- D16 · Original documents live in a separate archive repo
- D17 · Our own code-graph extractor, with pluggable link detectors → widened by D127
- D18 · The AI proposes, code checks; if code can't check it, a person approves
- D19 · Build order
- D20 · Every phase ends with a report; changes ripple to the phases that depend on it
- D21 · Telemetry: cost, quality, speed and failures for every run
- D22 · Grill each phase when it starts, not all upfront
- D23 · One `stangent` CLI is the store's only door; its core is C#
- D24 · Company constraints, and CI / code host behind adapters

## D25–D49: the code graph and the agent-fitness pilot

[`decisions/2-code-graph.md`](decisions/2-code-graph.md)

- D25 · The code graph is rebuilt from code, never committed
- D26 · What the code graph holds
- D27 · Impact answers: summary first, details on request
- D28 · How impact analysis is scored
- D29 · Phase 1 is split into 1a, 1b, 1c
- D30 · The TypeScript extractor runs in Node, inside the stangent tool
- D31 · What the frontend map holds
- D32 · How Phase 1b's frontend links are proven
- D33 · Phase 1c starts by hardening Test 2; 📜 is built only if history still helps
- D34 · 📜 links files, not members (members maybe later)
- D35 · 📜 evidence is a count over each file's recent commits
- D36 · 📜 counts changes on main's own line
- D37 · 📜 noise filters
- D38 · 📜 follows renames and moves, if it's cheap enough
- D39 · 📜 lines live in the code graph (`code-graph` v3)
- D40 · 📜 in impact answers: its own group, last, capped, never walked
- D41 · 📜 is recomputed in full, not incrementally
- D42 · 📜 uses whatever history the clone has; CI's graph carries it
- D43 · 📜 is tuned on two repos and judged on two others
- D44 · 1c's done-when: two gates, judged by code
- D45 · Before Phase 2, check the graph can do its Phase 4 jobs; splitting first
- D46 · A split group "holds up" if it compiles alone, then passes the unit tests
- D47 · Split cases: recent main-line PRs from bitwarden/server and Sonarr
- D48 · Splits come from the after-change graph, joined within 2 steps (✅ + 🟡)
- D49 · The split check's done-when: safe, useful, better than split-by-folder

## D50–D68: reference pages and the FSD

[`decisions/3-reference-and-fsd.md`](decisions/3-reference-and-fsd.md)

- D50 · stangent is a standalone rebuild, modelled on the company's pipelines
- D51 · The spec is built from the agreed FSD and the code together
- D52 · Two documents, linked: the FSD and a reference generated from code → revised by D99; part reversed by D128
- D53 · Links: code finds candidates, AI judges with quotes, code checks, a person approves
- D54 · One reference page per screen, plus pages for the backend no screen reaches
- D55 · The reference catches up per change: detected on each PR, regenerated on merge
- D56 · Phase 2 is proven on an FSD for Sonarr with planted discrepancies → note reversed by D127
- D57 · Phase 2's done-when: finds what's planted, not too noisy, grounded, repeatable
- D58 · Decisions are fields in a committed YAML file; the website later edits the same file
- D59 · Phase 2 is split: 2a reference from code, then 2b FSD and discrepancies
- D60 · AI steps run in Claude Code; the CLI prepares their input and checks their output
- D61 · Verification by a separate agent with a fresh context → paused while building, D120
- D62 · Phase 2 applies D61: a fresh verifier for pages and for "agree" links → one rejection reversed by D119
- D63 · Slices split at the API boundary; every endpoint gets a page
- D64 · The split check is judged on bitwarden's first run; no rerun
- D65 · The graph's blind spots and a smarter split rule are Phase 4 work
- D66 · Phase 2a is proven on the pages 2b needs plus a sample; Sonnet writes and verifies
- D67 · Phase 2's hand checks are done by a fresh agent, not the owner
- D68 · Discrepancy findings ship off by default (D57's rule, applied)

## D69–D98: tickets

[`decisions/4-tickets.md`](decisions/4-tickets.md)

- D69 · Phase 3's bundle is read by a developer's Claude Code first; Phase 4 reads the same contract
- D70 · A ticket's starting points: code first, then AI from page titles, then the developer → starting points replaced by D101
- D71 · The bundle carries pages and pointers, not source code, under a size cap
- D72 · Attachments: code converts text, keeps images as files, lists the rest
- D73 · The stangent CLI reaches Jira as an MCP client, behind the issue-tracker adapter
- D74 · Write-back: AI drafts comments as files; a person reads and posts them
- D75 · A bug is proven before it's fixed, in Phase 3
- D76 · Bug proof uses unit tests and Playwright
- D77 · Phase 3 is proven on Sonarr issues copied into the owner's Jira, read through the MCP
- D78 · The frozen set: 40 bugs and 10 not-bugs
- D79 · Phase 3's done-when: right place, real proof, grounded, repeatable
- D80 · "code-wrong" decisions don't become tickets in Phase 3
- D81 · The run's cost: reproduction on 30 bugs, each attempt capped
- D82 · Jira through Atlassian's MCP v2 (`/v2/mcp`), with Jira-only scopes and secrets in the OS keychain
- D83 · A ticket's parts can be marked "outside the code" (server, network, deployment, data), each tagged by who said so
- D84 · Drafts: framing lines need no claim, writers never talk about their input, and the run drafts after proof
- D85 · A Jira read that gets no answer is tried again, the last time on a new session
- D86 · Browser proof: the app runs while the AI works; it copies a related Playwright test, or looks with a browser when there is none
- D87 · Starting points through a page index built by code; ranking orders pages, it never hides one → page picker replaced by D101
- D88 · Impact answers say what they left out; no forward walk
- D89 · Our own setup never counts against the AI: rules that don't depend on the shell's folder, setup failures recorded apart, fail fast, and unit targets for every starting point
- D90 · A test runner that can't run is the setup's failure; a mock's own check is an assertion
- D91 · A setup failure gets a plain reason and advice; stangent never installs anything itself
- D92 · Must-pass fixes after the Sonarr run: drafts that stay sourced, a keychain that survives parallel runs
- D93 · A version given in the environment counts as the version
- D94 · The generality batch: nothing in stangent assumes the benchmark repos
- D95 · A proof says which ticket sentence its failure shows; a developer confirms the match
- D96 · A failing test shows the code differs from what the reporter expects; only an FSD or a developer makes it a bug → revised by D99
- D97 · A draft leaves out a sentence no claim backs, instead of failing (built; how sentences are found: D98)
- D98 · The writer hands over its draft as a list of sentences, each with its claims (built)

## D99–D111: the structured spec, the file finder and closing Phase 3

[`decisions/5-spec-and-finder.md`](decisions/5-spec-and-finder.md)

- D99 · The FSD becomes a structured spec, built from word-for-word quotes; the BA approves each requirement (decided; extraction, requirement links and `ticket match` built, approval, reconcile and the other sources not)
- D100 · Held-out bugs come from bitwarden/server; Sonarr's 40 become the tuning set (decided; set frozen)
- D101 · A coding agent finds the ticket's files; code checks them; the graph is the checker and the map (decided; built, committed)
- D102 · A ticket Jira sends as HTML reads as the same Markdown as any other (decided)
- D103 · A code quote may be one whole line, even with no space (decided, built)
- D104 · The proof's turn cap counts model rounds, what `--max-turns` limits (built; not yet run)
- D105 · The on-fix check says what kind of failure it was; D79's bar stays as it is
- D106 · "Not reproduced" stops saying "don't fix" and says why (built; not yet run)
- D107 · The finder's reason is a guess: labelled for people, hidden from the proof (built; not yet run)
- D108 · `ticket post` refuses a draft whose inputs changed (built; no AI needed)
- D109 · Phase 3 closes on what we have; the proof ships as advice only → "next" list replaced by D112
- D110 · The core stays C#; no port to Python
- D111 · Stangent imports PDF and Excel as well as Word

## D112–D149: the overhaul; new decisions go here

[`decisions/6-overhaul.md`](decisions/6-overhaul.md)

- D112 · The overhaul's order: foundation first, stopping before Phase 4
- D113 · The foundation's shapes: evidence, permanent ids, the client-data check
- D114 · One module for state changes: which things are lifecycles, and where history lives
- D115 · Line citations and Playwright as evidence: the read paths, and lines kept out of the walk
- D116 · Word stops on tracked changes, and a code check compares every word
- D117 · Sections get a permanent id beside their number, and only exact signals match across versions
- D118 · What counts as the same statement, where line citations live, and a severity scale a person sets
- D119 · A second agent tries to disprove each finding; verdicts route them; the BA can say the page is wrong
- D120 · Build first, then test one component at a time
- D121 · Switches, structured import problems and one set of import steps
- D122 · Verdicts, labels, first-import approval, `recheck` and `gone`
- D123 · Excel as a main document, files inside a document, attachments in the checks and `contentHash` → one line reversed by D126
- D124 · Table facts, the mapping file, finer verdicts, grouping and asking, all switched off
- D125 · What we take from the workflow comparison, in bulk
- D126 · Batch 1 of §9: a hard cost stop, checks around a run, FSD import fixes
- D127 · Python joins the code graph, tested on the owner's own project
- D128 · The parsed spec is the clean source of truth; new Word files come in as proposals a BA or PM checks
- D129 · Journeys come from both sides: the FSD's stay, and end-to-end tests add what the app does → J.1 built ahead of the use-case runs by D149
- D130 · A bundle stores only what's unique; code assembles the rest
- D131 · Batch 2 of §9: bundles store what's unique, corrections stick, pages when needed
- D132 · Security review is its own checked step in Phase 6, apart from the FSD comparison
- D133 · Every link reaches both the page and the code lines, filled in by code
- D134 · The file an item sits in is its status; the marks go
- D135 · Twelve rules for all work live in `CLAUDE.md`, and the full entry wins
- D136 · `decisions.md` is a short index; full entries live in `decisions/`, open questions in `questions.md`
- D137 · "Is it true?" is Stangent's job; "which side is right?" is the PM's
- D138 · The cut rule: a component that fails a bar gets one fix attempt, then it's removed
- D139 · No cap on new items
- D140 · A section's candidates can reach shared components through a screen's unshared ones (`shared-through`, off)
- D141 · An AI flag for findings that touch security, sent to a developer; severity stays a person's → built by D148
- D142 · Readable section files go into git with D128, as a generated view; `document.json` stays the record
- D143 · A section is cut into claims by code, printed only (`fsd claims`, `fsd-claims`, off); ids and the judge come in a later slice
- D144 · Workflow files sit on top of pages, written by code; pages stay where facts live → built by D147
- D145 · Claims get permanent ids, a judge names ids and copies no words, and a link's id comes from ids (`claim-links`, off) → repeated steps and page-cut steps revised by D146
- D146 · A repeated step groups despite its number, a page-cut step is one claim, a disprove bundle holds an endpoint's callers (`disprove-callers`, off), and two docs say what's true
- D147 · Workflow files are built: a flow's status by code, steps by their own numbers, and older links counted rather than placed (`workflow-files`, off); builds D144
- D148 · A security flag is built: the disprove step sets it, any verdict can carry it, and only a person answers it (`security-flag`, off); builds D141
- D149 · Journeys read from end-to-end tests are built ahead of the use-case runs, and compare nothing (`test-journeys`, off); builds D129's J.1
