# Decisions

Every decision still in force, one line each. Short entries are in `decisions/`: find one with `grep -n "^## D120 " decisions/*.md`. Trimmed 2026-11-07: full original text of every entry, including those marked archived here, is in [`decisions/archive/`](decisions/archive/) (D136).

- **Changing a decision:** edit its entry and note the date; don't silently contradict it elsewhere. Add a "→" note here if it changes an older one.
- **Adding one:** the short entry goes at the end of the newest file in `decisions/`, plus one line here, in the same turn. D numbers are never reused or renumbered.
- **"→ archived"** means the entry is no longer in force and its live entry was removed; the reason says why. **D155 wins over every earlier decision.**
- **Open questions** are in [`questions.md`](questions.md); answered ones in [`decisions/answered-questions.md`](decisions/answered-questions.md).

## D1–D24: foundation and the brief

[`decisions/1-foundation.md`](decisions/1-foundation.md)

- D1 · Built for the owner's own use first: company and personal projects alike
- D2 · Tickets: Jira, through Atlassian's MCP (attachments and API-token runs per D73)
- D3 · PRs on GitHub; CI defaults to GitHub Actions, behind adapters (D24)
- D4 · Target codebase shape: monorepo, many .csproj, incremental per-PR → widened by D127, D154
- D5 · Build from the ground up, contracts first; the benchmark breaks ties
- D6 · Benchmark: public repos (nopCommerce, eShop, CleanArchitecture, Kavita, bitwarden, Sonarr)
- D7 · Source of truth: files in the repo, with a SQLite index rebuilt from them
- D8 · Portability through versioned schemas and one store interface
- D9 · Each product is a CLI; the plugin is a thin layer over them
- D10 · Links live in a link file, target symbol ids, are checked in CI; FSD → reference → code → requirement hop replaced by D156 P4
- D11 · A living spec owned by BA/PM: stable ids, spec-only PRs, states, hash-checked verification → AI-written requirements replaced by D156 P4
- D12 · Specs live in the codebase; BA/PM edit them through a dashboard that writes spec-only PRs
- D13 · Spec sources and provenance labels → archived: replaced by D52
- D14 · One full audit upfront → archived: replaced by D99
- D15 · Source documents: Word, PDF, Excel, converted by code, every original kept; a person settles clashes → latest-wins reversed by D128
- D16 · Original documents live in a separate archive repo, cited by name, path and hash
- D17 · Our own code-graph extractor (C#, TypeScript, Python) with pluggable link detectors; blind spots reported
- D18 · The AI proposes, code checks; if code can't check it, a person approves → strengthened by D155
- D19 · Build order: phases 0–8; the rebuild order within 0–3 is D156's
- D20 · Every phase ends with a report; contract changes mark dependents `needs review`
- D21 · Telemetry: cost, quality, speed and failures per run, local, no content by default
- D22 · Grill each phase when it starts, not all upfront
- D23 · One `stangent` CLI is the store's only door; its core is C# (reconfirmed by D110)
- D24 · Constraints, CI and code host behind adapters, a per-repo switch that turns off every AI step

## D25–D49: the code graph and the agent-fitness pilot

[`decisions/2-code-graph.md`](decisions/2-code-graph.md)

- D25 · The code graph is rebuilt from code, never committed; CI builds it per merge to main
- D26 · What the code graph holds: method-level points, API shapes, config keys; lines labelled ✅ 🟡 📜 ❓
- D27 · Impact answers: summary first, nearest first, details on request, ❓ always listed
- D28 · How impact analysis is scored: Tests 1–4 from git history, per label, against baselines
- D29 · Phase 1 is split into 1a, 1b, 1c → archived: obsolete: an order of work that's finished (1a–1c built and reported)
- D30 · The TypeScript extractor runs in Node, inside the stangent tool
- D31 · What the frontend map holds: TS compiler, Angular templates, routes as pages, frontend → backend calls
- D32 · How frontend links are proven: frontend Test 4 and a committed openapi.json
- D33 · Test 2 is hardened before 📜 is judged: each fix once, full history, bundles as a slice
- D34 · 📜 links files, not members (members maybe later)
- D35 · 📜 evidence is a count over each file's recent commits (window 10, minShared 3)
- D36 · 📜 counts changes on main's own line
- D37 · 📜 noise filters: huge commits, bots, formatting sweeps, reverts
- D38 · 📜 follows renames and moves; not on blobless clones
- D39 · 📜 lines live in the code graph (`changes-with`, `meta.history`)
- D40 · 📜 in impact answers: its own group, last, capped, never walked
- D41 · 📜 is recomputed in full, not incrementally
- D42 · 📜 uses whatever history the clone has; CI's graph carries it
- D43 · 📜 is tuned on some repos and judged on others; a 30-fix floor
- D44 · 📜 passed its gate and ships on by default; a retune must pass all of Gate 2
- D45 · The graph's Phase 4 jobs (split, scope, check) are checked before Phase 4; context scoping still owed
- D46 · A split group "holds up" if it compiles alone, then passes the unit tests
- D47 · Split cases: recent main-line PRs from bitwarden/server and Sonarr; the rules any later split test uses
- D48 · Splits come from the after-change graph, joined within 2 steps (✅ + 🟡)
- D49 · The split check's done-when; safe failed, so Phase 4 builds parallel runs together or runs in sequence

## D50–D68: reference pages and the FSD

[`decisions/3-reference-and-fsd.md`](decisions/3-reference-and-fsd.md)

- D50 · stangent is a standalone rebuild, modelled on the company's pipelines
- D51 · The spec is built from the agreed FSD and the code together
- D52 · Two documents, linked: the FSD and a reference generated from code → outside doc as truth reversed by D128; D99 layer replaced by D156 P4
- D53 · Links: code finds candidates, AI judges, code checks, a person approves → AI quoting replaced by D156 P3; AI finder fallback by D156 P2
- D54 · Reference pages: ids from the graph, every statement cited → page split replaced by D154; writer freedom by D156 P1
- D55 · The reference catches up per change: detected on each PR, regenerated on merge
- D56 · Links are scored on a Sonarr FSD with planted discrepancies → cyberbullying note reversed by D127
- D57 · The links' done-when: finds what's planted, not too noisy, grounded, repeatable
- D58 · Decisions are fields in a committed YAML file; the website later edits the same file → requirement tick boxes replaced by D156 P4
- D59 · Phase 2 is split: 2a reference from code, then 2b FSD and discrepancies → archived: obsolete: an order of work; 2a and 2b are both built and scored
- D60 · AI steps run in Claude Code; the CLI prepares their input and checks their output → writer freedom replaced by D156 P1
- D61 · Verification by a separate agent with a fresh context, capped at one check and one fix round; per layer again by D158
- D62 · Phase 2 applies D61: a fresh verifier for pages and "agree" links → one rejection reversed by D119; agree checker under review (D156 P8)
- D63 · Hubs are included, not walked into; slice limits are settings → endpoint split and endpoint pages replaced by D154
- D64 · The split check is judged on bitwarden's first run; no rerun; the cutter fix stays in the 1d harness
- D65 · The graph's blind spots and a smarter split rule are Phase 4 work → blind spots flows need moved to the graph check by D159
- D66 · Sonnet writes and verifies pages, in single-turn calls; pages are made as needed
- D67 · Phase 2's hand checks are done by a fresh agent, not the owner
- D68 · Discrepancy findings ship off by default (D57's rule, applied)

## D69–D98: tickets

[`decisions/4-tickets.md`](decisions/4-tickets.md)

- D69 · Phase 3's bundle is read by a developer's Claude Code first; Phase 4 reads the same contract
- D70 · A ticket's starting points: code's signals, and the developer's edits → AI page picks replaced by D101
- D71 · The bundle carries pages and pointers, not source code, under a size cap → cut order replaced by D101
- D72 · Attachments: code converts text and PDF, keeps images as files, lists the rest
- D73 · The stangent CLI reaches Jira as an MCP client, behind the issue-tracker adapter
- D74 · Write-back: drafts as files; a person reads and posts them → AI wording replaced by D156 P7
- D75 · A bug is proven before it's fixed, in Phase 3; "no proof yet" says why (D106); advice only (D109)
- D76 · Bug proof uses unit tests and Playwright
- D77 · Phase 3 is proven on Sonarr issues copied into the owner's Jira, read through the MCP; saved results replayed
- D78 · The frozen set: 40 bugs and 10 not-bugs (now the tuning set, D100)
- D79 · Phase 3's done-when: right place, real proof, grounded, repeatable
- D80 · "code-wrong" decisions don't become tickets in Phase 3
- D81 · The run's cost: reproduction on 30 bugs, each attempt capped ($1, 30 turns)
- D82 · Jira through Atlassian's MCP v2 (`/v2/mcp`), with Jira-only scopes and secrets in the OS keychain
- D83 · A ticket's parts can be marked "outside the code", each tagged by who said so; proof skipped only if nothing points into code
- D84 · Drafts: framing lines need no claim, writers never talk about their input, and the run drafts after proof
- D85 · A Jira read that gets no answer is tried again, the last time on a new session
- D86 · Browser proof: the app runs while the AI works; it copies a related Playwright test, or looks with a browser
- D87 · Starting points through a page index built by code; ranking orders pages, it never hides one (under review, D156 P6)
- D88 · Impact answers say what they left out; no forward walk
- D89 · Our own setup never counts against the AI
- D90 · A test runner that can't run is the setup's failure; a mock's own check is an assertion
- D91 · A setup failure gets a plain reason and advice; stangent never installs anything itself
- D92 · Must-pass fixes after the Sonarr run: drafts that stay sourced, a keychain that survives parallel runs
- D93 · A version given in the environment counts as the version
- D94 · The generality batch: nothing in stangent assumes the benchmark repos
- D95 · A proof says which ticket sentence its failure shows; a developer confirms the match
- D96 · A failing test shows the code differs from what the reporter expects; a person makes it a bug → "bug, per R-…" replaced by D156 P4/P5
- D97 · A draft leaves out a sentence no claim backs, instead of failing
- D98 · A draft is a list of parts, each one sentence with its claims → writer composing replaced by D156 P7

## D99–D111: the structured spec, the file finder and closing Phase 3

[`decisions/5-spec-and-finder.md`](decisions/5-spec-and-finder.md)

- D99 · The FSD gets a checked layer: word-for-word quotes, stable ids, people approve → AI requirements and ticket match replaced by D156 P4/P5 → FSD journeys by code (D160)
- D100 · Held-out bugs come from bitwarden/server; Sonarr's 40 are the tuning set
- D101 · A coding agent finds the ticket's files; code checks them; the graph is the checker and the map (under review, D156 P6)
- D102 · A ticket Jira sends as HTML reads as the same Markdown as any other
- D103 · A code quote may be one whole line, even with no space
- D104 · The proof's turn cap counts model rounds, what `--max-turns` limits
- D105 · The on-fix check says what kind of failure it was; D79's bar stays
- D106 · "Not reproduced" stops saying "don't fix" and says why
- D107 · The finder's reason is a guess: labelled for people, hidden from the proof
- D108 · `ticket post` refuses a draft whose inputs changed
- D109 · Phase 3 closed on what we have; the proof ships as advice only; its re-run is owed before Phase 4
- D110 · The core stays C#; no port to Python
- D111 · Stangent imports PDF and Excel as well as Word, by code only, listing what can't come in exactly

## D112–D166: the overhaul; new decisions go here

[`decisions/6-overhaul.md`](decisions/6-overhaul.md)

- D112 · The overhaul's order (finished; D156 sets the new one); driver tests stay xUnit with fakes on the path
- D113 · The foundation's shapes: evidence, permanent ids, the client-data check
- D114 · One module for state changes: which things are lifecycles, and where history lives
- D115 · Line citations and Playwright as evidence: the read paths, and lines kept out of the walk
- D116 · Word stops on tracked changes, and a code check compares every word
- D117 · Sections get a permanent id beside their number, and only exact signals match across versions
- D118 · What counts as the same statement, where line citations live, and a severity scale a person sets
- D119 · A second agent tries to disprove each finding (`fsd.disprove`, off); refutations wait for a person → under review (D156 P8)
- D120 · Build first, then test one component at a time → "a switch for every component" narrowed by D153 → pause ended by D158
- D121 · Switches, structured import problems and one set of import steps
- D122 · Verdicts, labels, first-import approval, `recheck` and `gone`
- D123 · Excel as a main document, files inside a document, attachments in the checks and `contentHash` → one line reversed by D126
- D124 · Table facts and the mapping file, switched off → finer verdicts, grouping and asking cut by D156 P8
- D125 · What we take from the workflow comparison, in bulk
- D126 · Batch 1 of §9: a hard cost stop, checks around a run, FSD import fixes
- D127 · Python joins the code graph (extractor, Flask routes, `root-screens`, `route-render`)
- D128 · The parsed spec is the clean source of truth; new files come in as proposals a BA or PM checks → section level built by D150
- D129 · Journeys also come from end-to-end tests, read by code (built by D149, `test-journeys` off) → FSD journeys by code (D160)
- D130 · A bundle stores only what's unique; code assembles the rest
- D131 · Batch 2 of §9: bundles store what's unique, corrections stick, pages when needed
- D132 · Security review is its own checked step in Phase 6, apart from the FSD comparison
- D133 · Every link reaches both the page and the code lines, filled in by code when read
- D134 · The file an item sits in is its status; the marks go
- D135 · Twelve rules for all work live in `CLAUDE.md`, and the full entry wins
- D136 · `decisions.md` is a short index; full entries live in `decisions/`, open questions in `questions.md` → changed by D157 (an archive now holds the full text)
- D137 · "Is it true?" is Stangent's job; "which side is right?" is the PM's
- D138 · The cut rule: a component that fails a bar gets one fix attempt, then it's removed
- D139 · No cap on new items (D153 limits switches, not items)
- D140 · A section's candidates can reach shared components through a screen's (`shared-through`, off) → archived: to be replaced by D154 if its trial wins; the switch stays built until D156's cut step
- D141 · An AI flag for findings that touch security → archived: to be cut by D156 P8 (an unproven AI switch); `security-flag` stays built until the cut step, its rules in the archive
- D142 · Readable section files go into git with D128, as a generated view; `document.json` stays the record → built by D150
- D143 · A section is cut into claims by code, with a words check; ids came in D145; switch folds into `claim-links` (D156 P3)
- D144 · Workflow files sit on top of pages, written by code → archived: pages give way to D154's flows if its trial wins; `workflow-files` stays built until D156's cut step
- D145 · Claims get permanent ids, a judge names ids and copies no words, a link's id comes from ids; on by D156 P3
- D146 · A repeated step groups despite its number, a page-cut step is one claim; `disprove-callers` under review (D156 P8)
- D147 · Generated files: a marker line, same bytes, never read back, a rebuild removes only its own files → workflow files replaced by D154
- D148 · A security flag built from the disprove step (`security-flag`, off) → archived: to be cut by D156 P8; built until the cut step, its rules in the archive
- D149 · Journeys parsed from end-to-end tests compare nothing (`test-journeys`, off); builds D129's J.1
- D150 · D128 at section level: a person approves or rejects each changed section, promote applies only what was approved (`section-approval`, off)
- D151 · A page gap means a quoted code line settles it, and waits for a person (`page-gap-strict`, off); under review (D156 P8)
- D152 · `trace findings`: the BA's queue as one readable file, strongest evidence first; no switch
- D153 · No new switches until the built ones are tested or removed
- D154 · The reference layer becomes areas → flows laid out by code; a person confirms areas once; any stack; trial on one Sonarr area first
- D155 · Deterministic first: code decides what it can, an AI only words or picks among what code laid out; wins over earlier decisions
- D156 · D155's eight changes approved (P1–P8); the rebuild goes ground up: foundation, graph, areas and flows, pages, linking, tickets, cuts
- D157 · The decision log is trimmed: short live entries for what's in force, every original word for word in `decisions/archive/`
- D158 · The fresh-check pause ends: each layer gets a narrow fresh check before the next is built on it
- D159 · The blind spots flows need (screens incl. Razor views, their handlers, what they call) are fixed in the graph check, not Phase 4
- D160 · FSD journeys are built by code from a use case's numbered steps, with no AI, and compared step by step with flows
- D161 · Every AI run's model and effort are pinned in `stangent.yaml` and recorded; a run refuses without them; a three-run repeat check compares answers by code
- D162 · Every AI step runs through the batch drivers; the plugin's in-session subagent route goes
- D163 · The code graph reads what flows need, checked on Sonarr and Kavita; `code-graph` v5 (G1–G8 fixed)
- D164 · The framework's own answer first: tables from EF's snapshot; every build cross-checks endpoints with OpenAPI and tables with the snapshot
- D165 · Razor views are read from the Razor compiler's own output (forms → endpoints, buttons, `View()` → view); an app's own access filters and plain scripts are read (nopCommerce N1–N5)
- D166 · The graph's guesses and blind spots are checked after the first flows exist, only those a flow uses; blind spots sorted by cause; runtime evidence opt-in, apart from the graph
- D167 · The fresh check's T1–T12 fixed (forms POST, button targets, partials' forms, base-class view helpers, themes, global and evidence-marked access filters, script globals and handlers)
- D168 · The T1–T12 check's V1–V5 fixed (MVC's lookup by the page's controller, cancelled submits, inherited button methods, 4xx refusals, rule modifiers)
- D169 · A submit button a script writes submits the form around it, as a guess (W1)

- D170 · Layer 3 builds flows first (a screen change stops a flow and links to that screen's load flow); areas are grouped from the flows afterwards; the first bar is on flows
- D171 · A flow starts at a click or submit (one flow per handler), a screen opening, or a system job; the graph first gains "runs on load" and "is a job" facts (layer 2, fresh-checked)
- D172 · A flow is a tree walked forward; a branch ends at the database, an outside call, a message sent (handlers are linked flows, never through the bus), another screen, a hub, a ❓ or 8 real calls
- D173 · Every out-of-bounds end is a link to another flow (handlers, jobs "runs later", screens, "continues in"), explained by a code-made sentence with its proof line; the graph gains "queues" lines
- D174 · A flow step carries the condition it sits under, quoted from the code; early exits (redirect or error returns, throws) end a branch; validation rules are a "checks" step; facts added in layer 2
- D175 · A URL match is ✅ when its text is constant and exactly one endpoint fits (✅ = proved by code); other guesses are labelled per step with their reason, listed at the top of the flow
- D176 · Layer 3 is code only (the paid run moves to layer 5): fixed rules for step order, loops, names, load effects, early exits, display-only parts; the same commit builds the same flows byte for byte
- D177 · Layer 3's bar, frozen first: 15 flows on Sonarr, Kavita, nopCommerce; key by a fresh agent from source alone, three approved by the owner; no wrong ✅, ≥95% found, every 🟡/❓ explained, byte-for-byte rebuilds, no silent skips
- D178 · Areas by a fixed folder rule (screen's home folder; shared folders never a home; jobs by who links to them; split over 40 flows; never merged by code); ties listed for a person
- D179 · Every flow shows name, start, actor (fixed rule from guards and access), inputs, checks, steps, ends, scenarios (main path plus one per early exit), links, guesses, settings; all by code
- D180 · Flows and areas committed under `specs/` (`flows/<id>.json|.md`, `index.json`, `areas.yaml` by folders with ties); `stangent flows build|show|check`, `areas propose|check`; contracts `flows` v1, `areas` v1
- D181 · Layer 2's rules for load (`on-load`, effect points), jobs (`is-job`, `jobInterfaces`), queues (a command made in a call's argument → its handler), conditions and exits (quoted, D176's list), and the ✅ URL rule (`http-match` reasons)
- D182 · The fresh check's Y1 (on-load for MVC pages with a runtime view), Y2 (a job requeuing itself keeps its line) and X1 (guard clauses as `unless` conditions) fixed
- D183 · The frozen key's format: one JSON per flow under `benchmark/expected/flows/<repo>/`, every item a code line (`at` + exact `code`), steps a tree by `from`; written from git-tracked source only, checked by code
- D184 · Layer 3's walk: `call-site` facts (each call's line), a hook's own functions as points, `filterType` on access filters; filters, `OnActionExecuting` and route guards walked first; a hub reading a table ends `database`; handler flows; the flows scorer's matching rules
- D185 · Ruling R1: a key end on a call Stangent walks into counts when Stangent's steps below it end the same way (outside call, message; database only on the key's table); a key end that only says it didn't follow the call leaves the count where Stangent walks on (amended); the key isn't edited
- D186 · Ruling R4: a returned call to an app's own helper that sets a 4xx/5xx status or returns only listed results is an early exit (D176's list grows by that rule)
- D187 · Ruling R5: a filter setting `context.Result` to a listed result (or a D186 helper's) is an early exit, changing D181
- D188 · Ruling R6: a C# call into a package (not `System.*`, EF Core or logging) is a `call-site` with no target; flows show it as a step ending "a package's code, not walked"
- D189 · Shared flows: each hub and depth stop a flow continues into is its own flow (kind `shared`, walked once, transitively), named by the link's `flow`; the scorer follows them; a field or property read is no step
- D190 · A frontend URL built from parameters takes each value the repo's own callers pass (🟡, with the point that gave it); a flow keeps the values its own code gave
- D191 · A view's onclick walks into the script function it calls, the route placed where the script sends to that argument; a top-level object's functions are points; jQuery's callbacks are calls; D190 applies to every HTTP call style
- D192 · Navigation by code: a returned Angular redirect, or a navigation the function returns straight after, is an exit; any other navigation is a step ending `screen`; error callbacks are `catch` conditions
- D193 · Requests made another way: Angular's `httpResource` URLs and an image's `src` are GET calls; the URL reader takes every return of a function; a component's field initialisers are walked with it
- D194 · Rulings R8 (a key item quoting no code, like `};`, leaves the count) and R9 (R1 credits `screen` ends too); the key isn't edited
- D195 · Ruling R10: a key exit's condition counts when it's on the exit or on a step on the way to it; the key isn't edited
- D196 · A hub whose own code reads or saves a table keeps its `database` end and continues into its shared flow too
- D197 · Ruling R11: a key `hub` end counts on a step that runs that hub and ends `database` (D172 names its table first); the key isn't edited
- D198 · An outside HTTP call with a runtime URL ends `outside-call` (still ❓ where); each leaving branch of an `if … else if` chain is an `unless` guard
- D199 · A query's run reads its source's table (repository `IQueryable<T>`, `DbSet<T>`); `SmtpClient` calls are outside calls; returned MVC `View`/`Redirect…` end `screen`; a method calling itself is a call site
