# Decisions

Settled while grilling `spec.md`. Each entry: what was decided, why, and what it costs.
Change a decision by editing its entry and noting the date — don't silently contradict it elsewhere.

## D1 · Built for our own company first — 2026-10-18
Stangent targets our own team's real workflow and stack. Making it installable for
others (spec item 11) is a later phase.
**Why:** abstracting over many ticket systems, hosts and stacks before one works end to end is how platforms stall.

## D2 · Tickets: Jira — 2026-10-18
Interactive use goes through the Atlassian MCP.
**Cost / to verify:** unattended runs (scheduled pulls, CI updates) need the Jira REST API with
an API token, not an interactive MCP login. Whether the MCP can download attachment *files*
is unverified — test before designing around it.

## D3 · PRs on GitHub — 2026-10-18
CI/CD defaults to GitHub Actions.

## D4 · Target codebase shape — 2026-10-18
One monorepo; many `.sln`, many `.csproj`; ~500k lines total; frontend (Angular and/or React)
in the same repo; .NET 8+; a mix of monolith and microservices.
**Consequences:**
- Index by `.csproj` and de-duplicate (solutions share projects).
- Roslyn needs a successful `dotnet restore`; a broken build means no graph.
- Full index takes minutes, so per-PR use must be incremental.
- Cross-service calls (HTTP, messaging) are invisible to Roslyn — they need convention-based
  linking (routes ↔ controllers, message contracts ↔ handlers). The highest-risk part of impact analysis.
- Separate repos / git submodules are out of scope for now (separate histories, PRs and pins).

## D5 · Build from the ground up, contracts first — 2026-10-18
No single pain point sets the order, so the foundation is the contracts between products
(artifact formats), not any one of the 11 products.
**Risk:** without a pain point there's no tiebreaker for tradeoffs; the benchmark (D6) stands in as the measure.

## D6 · Benchmark: three public repos — 2026-10-18
| Repo | Covers |
|---|---|
| [nopCommerce](https://github.com/nopSolutions/nopCommerce) | size (`net10.0`, 41 `.csproj`, ~386k lines C#, ~87k Razor, ~108k JS), monolith with plugins, string-based routes, in-house `IConsumer<T>` events, Autofac, linq2db |
| [dotnet/eShop](https://github.com/dotnet/eShop) | microservices (Basket, Catalog, Ordering, Identity, Webhooks), events between services over RabbitMQ, EF Core |
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | Angular and React frontends calling the API through an **NSwag-generated** client (not committed), MediatR, EF Core, Minimal APIs, Playwright tests |
| [Kareadita/Kavita](https://github.com/Kareadita/Kavita) *(added 2026-10-19, Phase 1b)* | `net10.0` + **Angular 22**, typed hand-written `HttpClient` services, a committed `openapi.json`; 3.4k commits |
| [bitwarden/server](https://github.com/bitwarden/server) *(added 2026-10-19, Phase 1c)* | `net10.0`, squash-merged PRs only (~8k commits, ~1k fixes); the judging repo for 📜 history evidence (D43). No frontend in this repo |
| [Sonarr/Sonarr](https://github.com/Sonarr/Sonarr) *(added 2026-10-19, Phase 1b)* | `net10.0` + **React 18 + TypeScript**, TanStack Query behind its own `useApiQuery` wrapper (an in-house pattern); 12k commits. Pins SDK 10.0.402, so the benchmark uses a `global.json` override |

Metric for impact analysis: for past issues with merged PRs (nopCommerce, eShop), compare
predicted files/symbols against the files the PR actually changed (precision and recall).
CleanArchitecture is a small template: it tests detector correctness, not scale or history.
**Gaps:** no Jira (needs a sandbox Jira project). *(The large real-world Angular/React gap is closed by Kavita and Sonarr.)*
Together the three frontends cover the three ways frontends call a backend: generated clients
(CleanArchitecture), hand-written typed services (Kavita) and an in-house wrapper (Sonarr).

## D7 · Source of truth: files in the repo — 2026-10-18
Artifacts are Markdown plus YAML/JSON, versioned by git. A local SQLite index is rebuilt
from them for fast queries and the dashboard.
**Why:** git gives versioning and revert (spec item 10), spec changes are reviewed in the same PR as the code, and there's no server.
**Cost:** links go stale when edited outside the tool, so CI must check them.

## D8 · Portability through versioned schemas and one store interface — 2026-10-18
- Every artifact type has a versioned JSON Schema; the schema is the contract.
- Products never read each other's files directly; all access goes through one store
  interface (read, write, query, link). One backend now (files + SQLite).
- A second backend (Postgres, a service) is built only when something forces it.

## D9 · Each product is a CLI — 2026-10-18
Each product is a CLI that reads and writes the versioned artifacts. The Claude Code plugin
is a thin layer of commands, skills, agents and hooks that call those CLIs. MCP only where it clearly helps.
**Why:** CLIs cost no context tokens until called, behave the same in CI as locally, are
testable without an LLM, and let each product use its natural language (code graph: C#/Roslyn;
frontend parser: Node/TypeScript).
**Cost:** users need both the .NET SDK and Node.

## D10 · Requirement ↔ code links live in a link file — 2026-10-18
Links are stored beside the specs (e.g. `trace.yaml`), mapping requirement IDs to code
symbols. Production code is not annotated.
- Links target symbol IDs (Roslyn documentation-comment IDs, the TS equivalent for
  frontend), never file:line.
- Requirements link coarsely, to entry points (endpoints, handlers, components); the code
  graph supplies what those reach.
- Tests carry requirement IDs (xUnit traits, Playwright test titles) — coverage per requirement for free.
- CI checks every linked symbol still exists; renames are auto-repaired from the graph diff or flagged.
- Inferred-only links (embeddings/LLM guesses) are rejected: that is the hallucination leak.
**Cost:** a refactor made outside stangent breaks links until CI catches it.

## D11 · A living spec of the whole system, owned by BA/PM — 2026-10-18
The spec describes what the system does now, organised as features, journeys and
acceptance criteria, each with an ID that is stable for the requirement's life.
A Jira ticket is a change request: it produces a spec change (add / modify / remove
requirements).
- *Revised 2026-10-18:* spec changes go in their **own spec-only PRs**, normally merged
  before development starts; BA/PM never review code PRs. Code PRs reference the ticket and
  the requirement IDs they implement.
- Spec change states: proposed → verified (BA/PM approved) → implemented (code PR merged),
  or implemented but provisional (shipped, awaiting client confirmation).
- Changes that developers find during or after development (spec item 10) become a proposed
  spec change, unverified until BA/PM approve it.
- Revert works per ticket: stangent groups a ticket's spec PR and code PR(s) by ticket key and
  reverts them together, along with their links.
- BA/PM own the spec. Each requirement can be marked **verified**; verification records a
  hash of the requirement's text, so any later edit drops it back to unverified automatically (checked in code).
- Each requirement needs an easy-to-read history and a place for comments.
**Cost:** existing code has no spec, and drafting one from code is the biggest hallucination
risk in the project. How to write specs for existing code is still open.

## D12 · Specs are written in the codebase, BA/PM edit them through the dashboard — 2026-10-18
- The spec is authored in the same monorepo as the code (`specs/` + `trace.yaml`), never in
  Confluence or the ticket system. **Why:** the spec must outlive any tool change — if Jira
  or Confluence is replaced, the spec doesn't move.
- BA/PM work in a spec view in the stangent dashboard: rendered spec, history per
  requirement, verify, comments. Its writes go back to git as spec-only PRs from a bot account.
- The dashboard is a screen over the repo files, not a second store. Until it exists, spec
  changes are plain spec-only PRs on GitHub — the same PRs the dashboard will later open.
- Rejected: Confluence as the authoring surface, one-way or two-way (lock-in, two sources of truth).
**Cost:** part of the dashboard (item 8) becomes early work, including sign-in, since a
verification must record who approved.

## D13 · Spec sources: existing documents first, code as fallback, provenance on every requirement — 2026-10-18
- Specs are built from existing documents where they exist (Word, Excel and similar); where
  they don't, from the code.
- Every requirement records where it came from and whether the sources agree:
  - **documented**: from a document, citing file plus section, page or table cell;
  - **inferred from code**: from code only, citing symbol IDs;
  - **documented, matches code**: both sources agree;
  - **conflict**: the document and the code disagree;
  - **documented, not found in code**: described but not implemented (or removed).
- Provenance is separate from verification (D11): everything is unverified until BA/PM approve.
- *Proposed, not yet agreed:* conversion to text is deterministic (no LLM); the LLM only
  structures the text; code checks that every quoted citation exists in the converted
  source text and every cited symbol exists in the graph.
**Why:** code often runs ahead of the documents, so a requirement taken from code must be visibly different from one somebody wrote down.
**Open:** what happens on a conflict; where the original documents are stored.

## D14 · One full audit upfront, then kept current per area — 2026-10-18
On adoption, stangent builds the whole spec in one pass: it imports every existing document
and reads all of the code, producing the full list of requirements and every discrepancy
between documents and code.
**Why:** a single due-diligence pass surfaces every discrepancy at once, instead of one ticket at a time.
**Conditions that make it work:**
- Generated is not verified (D11). The audit's output starts unverified; BA/PM work through
  it as a review queue, and nothing is trusted until verified.
- Discrepancies need triage, not a flat list: grouped by feature, ranked, with bulk actions
  (code is right for this area / document is right, the code is a bug → Jira ticket / defer).
- The audit runs area by area (areas come from the code graph), with bounded-context
  subagents, and is resumable if a run fails partway.
- "Once" doesn't stay true: after the audit, a code change in an area re-runs the
  code-vs-spec check for that area only (the graph says which areas changed).
- Dry-run on nopCommerce first to measure tokens, time and false-discrepancy rate before
  pointing it at company code.
**Cost:** the biggest single LLM spend in the project, and a large review backlog on day one.

## D15 · Source documents: many formats, latest wins, every version kept — 2026-10-18
- Expect many documents in many formats (Word, Excel, PDF, others). Support the common ones
  first; text conversion is deterministic (D13).
- When documents clash, the latest one wins, **and the requirement cites both**: the
  document it now comes from and the older one it supersedes. Nothing is silently dropped.
- Clashes are resolved per requirement, not per document: a newer document that covers only
  part of a feature supersedes only that part.
- Every original is archived, so any citation can be traced back to the exact file.
- Identical files (same content hash) are imported once.
- Deciding which document is newest: each document gets a version date and a status (draft or
  approved) on import, pre-filled from the file's own metadata and confirmed by BA/PM. Only
  approved documents supersede older ones; a newer draft's differences show as proposed
  changes. When the order can't be told, stangent flags it.
- Stangent never resolves a discrepancy itself: it points it out, and BA/PM clear it.
- The archive lives in a separate repo — see D16.

## D16 · Original documents live in a separate archive repo — 2026-10-18
- Originals (`.docx`, `.xlsx`, `.pdf`, ...) go in their own GitHub repo, with Git LFS for large files.
  The main repo holds only the converted plain text, which is what stangent reads.
- No symlink and no submodule. Each citation stores the archive repo, the file path and the
  file's content hash; stangent fetches an original only when someone opens it or an audit re-checks a citation.
**Why:** separate permissions for client documents, a light main repo, separate storage
limits — and an archive that's only added to doesn't suffer the two-repo problems from D4.
- For now: a private GitHub repo. The archive location is pluggable: it sits behind the
  store interface (D8) as its own backend, so it can move to SharePoint, blob storage or
  elsewhere later. Citations reference the archive by name, not by GitHub URL, so moving it
  changes one setting, not every citation.

## D17 · Our own code-graph extractor, with pluggable link detectors — 2026-10-18
- The code graph is built by our own extractor: Roslyn for C#, the TypeScript compiler for
  Angular/React. Plain references (calls, uses, implements, inherits) come from the same pass.
- The links that break things are found by **link detectors**, one per pattern, each a
  plugin: routes, dependency injection, events/messaging, database mapping, HTTP calls
  between services, frontend calls to backend URLs. Examples: ASP.NET Core routing,
  Microsoft DI, EF Core, MediatR, MassTransit, Angular `HttpClient`, `fetch`/axios —
  and in-house patterns like nopCommerce's `IConsumer<T>` events.
- Detectors are enabled per repo in config. Built-in detectors cover common frameworks;
  a company adds its own for in-house patterns without changing the core.
- **Blind spots are reported, not hidden.** Where the extractor can't resolve a link
  (reflection, strings built at runtime, unknown frameworks), the graph records an
  "unresolved" marker, and impact reports say what they couldn't see.
- A SCIP index may be read as an extra input later; it is not the base.
- First detector set: ASP.NET Core routing, Microsoft DI, EF Core, `HttpClient`, Angular
  `HttpClient`, React `fetch`/axios. Next: the company's own messaging library (cross-service
  links are the D4 risk). `scripts/scan_stack.py` lists a repo's packages to confirm the set.
  Scan of nopCommerce (2026-10-18): ASP.NET Core, Autofac as the DI container (registered
  through the Microsoft DI API), linq2db + FluentMigrator (not EF Core), jQuery (no Angular/React),
  plus its own `IConsumer<T>` events. So on the benchmark, the database and frontend detectors
  in the first set can't be tested; a linq2db and an in-house event detector would be needed for it.
- *Refined 2026-10-19 (Phase 1a):* events also match across services by type name (🟡), since
  services keep their own copies of integration events; `http-services` takes a `services` map for
  service names without Aspire.
**Why:** plain-reference indexers miss the links that break production (string routes, events,
DI choice, cross-service calls); nopCommerce alone has 234 string-based route definitions.
**Cost:** we maintain the extractor and keep up with new C# and TypeScript language features.
"Comprehensive" can't mean every framework on day one; it means adding a framework costs one detector.

## D18 · The AI proposes, code checks; if code can't check it, a person approves — 2026-10-18
One rule for every product:
| Task | Who decides |
|---|---|
| Code graph, link detection, impact analysis, test selection, document → text conversion, deploying | code only, no AI |
| Document text → requirements | AI proposes; code checks every quote exists in the source text |
| Requirements drafted from code | AI proposes; code checks every cited symbol exists; BA/PM verify |
| Code for a ticket | AI proposes; code checks it compiles, tests pass and only expected areas changed; a senior reviews the PR |
| Tests | AI proposes; code checks each test runs, fails without the change and passes with it |
| PR summary | AI writes; code checks every claim links to a real file or test result |
| Ticket update to a non-technical client | AI writes; a person approves before it's sent |
The AI never triggers a deployment.
- **Code is the default.** Every AI step must justify why code can't do it; when a recurring
  AI task becomes predictable (a template, a rule), it is replaced by code.
- **AI output is recorded, not regenerated.** An LLM isn't fully repeatable even at
  temperature 0, so each accepted AI output is stored as an artifact with its inputs' hashes
  and the model used; re-runs reuse it unless an input changed.
**Why:** a hallucination is caught at the step that made it, so it can't spread to the next
product; a failed check gives the agent one specific error to fix, which is cheaper than redoing the work.
**Cost:** every AI step needs a checker; steps with no good check fall to a person.

## D19 · Build order — 2026-10-18
0 Foundation → 1 Code graph → 2 Spec → 3 Tickets → 4 Agents → 5 Testing → 6 PR → 7 CI/CD →
8 Dashboard; Demo can slot in at any point. Full table with dependencies: `roadmap.md`.
- The dashboard comes late even though BA/PM need it (D12); until then spec changes are plain GitHub PRs.
- Telemetry's event format is part of Phase 0, so no product has to retrofit it.

## D20 · Every phase ends with a report; changes ripple to the phases that depend on it — 2026-10-18
- Each phase has a file (`phases/<n>-<name>.md`) whose front matter declares the contract versions
  it consumes and provides, and whose body ends with an end-of-phase report (format in `roadmap.md`).
- A finished phase can be reopened. If a change alters a contract, its version is bumped; a
  check (built in Phase 0) lists every phase still consuming the old version, and those are marked
  `needs review` with a follow-up task each.
- Dependents are flagged, never changed automatically: each is updated and re-tested by its own follow-up (D18).
- *Refined 2026-10-19:* an unmarked phase on an old contract version fails the check; once marked
  `needs review` it passes, so an upstream change can merge while its follow-ups are open.
- Docs change in the same commit as the behaviour they describe.

## D21 · Telemetry: cost, quality, speed and failures for every run — 2026-10-18
- Recorded per run: **cost** (tokens, money per run / ticket / step), **quality** (D18 check
  failures, human rejections of AI output, benchmark scores, false-discrepancy rate),
  **speed** (time per step, ticket → merged PR), **failures** (errors, retries, timeouts, failing step).
  Usage analytics can wait.
- **Not in git** (an exception to D7): events are written locally and saved with each CI run,
  then gathered into the SQLite index for the dashboard.
- **OpenTelemetry format**, using its conventions for AI calls, so exporting to a team-wide tool
  later is a setting (pluggable, D8).
- **No content by default:** counts, durations, hashes and IDs; never prompts or code. A per-run
  debug switch can record content locally, only when someone turns it on.
- The event format is a Phase 0 contract (`telemetry-event`).
**Why:** cost and quality are what prove stangent is effective and token-efficient.
**Cost:** debugging a bad AI output without recorded prompts means re-running the step.

## D22 · Grill each phase when it starts, not all upfront — 2026-10-18
Only what Phase 0 needs is settled now. Each later phase gets its own grilling when it starts,
recorded in its phase file (D20) and here.
**Why:** building Phases 0–1 will change the answers for testing, PRs and agents; designing them now means designing them twice.

## D23 · One `stangent` CLI is the store's only door; its core is C# — 2026-10-18
- Products read and write artifacts through one `stangent` CLI (`stangent store get spec CHK-012 --json`);
  input and output are JSON. Store logic, validation and checks exist once.
- The core is C#, installed as a `dotnet tool`. The code graph (also C#) uses the core as a
  library in-process for heavy writes; every other product calls the CLI.
- The CLI accepts batches of reads/writes in one call, so start-up time doesn't add up.
**Cost:** Node products and hooks pay start-up time per call; contributing to the core needs .NET.
Rejected: a store library per language (two copies drift); products touching files directly (breaks D8).

## D24 · Company constraints, and CI / code host behind adapters — 2026-10-18
- Developers can install `dotnet tool`s and npm packages freely.
- GitHub Actions is allowed (with secrets). CI must not be locked to it: workflow files stay
  thin and only call `stangent` commands, so moving to GitLab CI means rewriting thin YAML,
  not logic. The same goes for the code host (GitHub PRs now, GitLab merge requests later):
  it sits behind an adapter, like tickets (Jira) and the document archive (D16).
- Sending client code to Claude is approved. Whether data is used for training is set by
  the account's terms and admin settings, not by anything a prompt says — confirm the
  company's plan before pointing stangent at client code.
- Everyone who'd use stangent has Claude Code.
- A per-repo switch turns off all AI steps (only code-only parts run), for any client whose
  contract forbids external AI. Cheap now, expensive to retrofit.

## D25 · The code graph is rebuilt from code, never committed — 2026-10-19
- The graph lives in `.stangent/graph.sqlite` (git-ignored), tagged with the commit it was built from.
- CI builds it once per merge to `main` and saves it as a build output; developers and agents
  download that copy instead of re-indexing, then re-index only what changed locally.
- Same commit → same graph; tested by building twice and comparing.
- The rule (refines D7): git holds what people decide (specs, trace links, verifications,
  discrepancy decisions); anything code can regenerate stays out. The committed link file
  (D10) is checked in CI against a freshly built graph.
**Why:** committing it would add graph churn to every PR, cause unresolvable merge conflicts,
and risk a stale graph — and CI would have to rebuild it to verify it anyway.
**Cost:** a fresh checkout has no graph until it downloads one or indexes (minutes at 500k
lines); CI build outputs expire, so old commits eventually need re-indexing.

## D26 · What the code graph holds — 2026-10-19
- **Points, at method level:** project, file, class/interface, method, property; entry points
  (HTTP endpoint, frontend page/component, event/message, database table); test methods (marked as tests).
- **Also, so non-call breakages show up:**
  - **API data shapes:** each endpoint's request/response fields, plus entity/DTO properties
    (respecting each project's JSON naming settings). Frontend fields are linked only when the
    frontend type is clearly tied to the endpoint (typed interface, generated client); otherwise ❓.
  - **Config keys,** linked to the code that reads them (including bound settings classes).
    Environment variables and external secrets are ❓.
- **Lines:** calls, uses, implements/overrides, inherits, wired to (DI), routes to, publishes/handles,
  reads/writes table, HTTP call, reads config, tests.
- **Every line is labelled:** ✅ certain (compiler), 🟡 guessed (detector pattern),
  📜 usually changes together (from `git log`, filtered: no merges, no huge commits, older history
  weighs less; a hint only, never blocking), ❓ unknown (blind spot).
- Later: test coverage data (Phase 5); real traffic between services from OpenTelemetry traces, if the company has them.
- *Refined 2026-10-19 (Phase 1c):* 📜 is file to file (D34), counted over each file's recent commits
  (D35), and "no merges" became "main line only" (D36).
- Rejected: numeric confidence scores (made-up precision).
**Cost:** Phase 1 takes longer (three extra detectors); the graph is roughly 1.5–2× bigger (estimate, to
measure); history needs full clones, not shallow ones. Reports list ✅ first, then 🟡, then 📜 and ❓,
grouped and capped, to avoid alert fatigue. The benchmark scores each label separately; a label that
mostly adds false alarms is switched off or tuned.

## D27 · Impact answers: summary first, details on request — 2026-10-19
- An impact answer stops at entry points people recognise: endpoints, pages, event handlers,
  requirements (via D10 links), and the tests that cover the change. Grouped by kind and by
  label (✅ 🟡 📜 ❓), each group with its count.
- Details come on request (`--expand <group>`); the methods in between stay available, not shown.
- ❓ unknowns are always listed one by one, never folded into a count.
- One result, two formats: compact JSON for agents, Markdown for PRs and people (reused in Phase 6).
- Built by code (graph walk + grouping), no AI: the same change always gives the same report (D18).
- *Refined 2026-10-19 (Phase 1a):* event handlers are end points of the walk. Past one, only further
  events are followed, never the dispatcher behind it (otherwise one change reached 2,534 nopCommerce endpoints).
- *Refined 2026-10-19 (D28 scores):* items are listed nearest first, with each item's distance and each
  group split into direct (1 line away), near (2–3) and far (4+). `--max-depth` cuts off the rest
  (`impact-report` v2). Following every caller of a caller gave 0.5% precision on nopCommerce;
  within 2 lines it was 7%.
**Why:** an agent reads ~15 lines instead of hundreds; a BA/PM can follow it through requirement IDs.
**Cost:** group counts can hide something important; mitigated by counts on every group and listing every unknown.

## D28 · How impact analysis is scored — 2026-10-19
Three tests, all run by code from git history (no AI, no manual labelling):
1. **Finds the rest of the change:** given one file of a past PR, how many of the PR's other files
   does impact analysis find, and how many wrong ones does it add.
2. **Predicts what broke (headline):** pairs of a change and a later fix ("fix", "regression", a
   referenced issue); does impact on the first commit include what the fix touched.
3. **Stable and fast:** two builds of the same commit are byte-identical; full and incremental build times.
- Tests 1–2 report found/wrong **per label** (✅ 🟡 📜 ❓, D26) in every end-of-phase report.
- A sample of ~20 Test 2 pairs is hand-checked once to say how reliable the pair-finding is.
- No targets yet: measure first, set targets from the first results.
- The benchmark needs full git history for these repos, not shallow clones.
- *Refined 2026-10-19:* scoring starts from changed lines, finding changed methods by name in today's
  graph. Every test also scores free baselines (same project, same folder, co-change) on the same cases.
  An exact-graph check on a sample of past commits measures the error left by using today's graph
  (median agreement 1.0).
- *Refined 2026-10-19:* **Test 4, compile-break ground truth.** Rename sampled methods in memory,
  recompile the projects that depend on them, and score against the files that fail to compile. It
  checks the ✅ graph directly. Direct ✅ links scored recall 1.00 on nopCommerce and 0.97 on eShop.

## D29 · Phase 1 is split into 1a, 1b, 1c — 2026-10-19
| Sub-phase | Delivers | Proven on |
|---|---|---|
| 1a. C# map | Roslyn extractor (method level); detectors: routes, DI, events, EF Core / entity tables, config keys, HTTP between services; impact answers (D27); scoring tests (D28) | nopCommerce, eShop |
| 1b. Frontend + API shapes | TypeScript extractor; Angular/React → backend HTTP links; API request/response fields | CleanArchitecture |
| 1c. Git history | 📜 "usually changes together", with noise filters | nopCommerce, eShop |
Each ends with its own report (D20). 1b depends on 1a; 1c may wait if 1a's scores are good enough.
- *Refined 2026-10-19 (Phase 1c):* 1c is tuned on nopCommerce and Sonarr and judged on bitwarden/server (D43; Kavita and eShop have too few fixes).
**Why:** a working, scored C# impact analysis much sooner, and a chance to change course before the frontend half.

## D30 · The TypeScript extractor runs in Node, inside the stangent tool — 2026-10-19
- `stangent graph build` runs a bundled Node extractor for each frontend project (each `tsconfig.json`),
  using the real TypeScript compiler. It writes its fragment in the same format as the C# side
  (`code-graph`); linking frontend calls to backend endpoints happens in C#, in the same link step.
- One install (the dotnet tool carries the bundled extractor); Node must be on the machine (D9).
- Types need the frontend's packages: `graph build` runs `npm ci` (or yarn/pnpm, whichever the project
  uses) when they aren't installed, just as it runs `dotnet restore`; `--no-restore` skips both.
  Without packages, frontend links fall back to 🟡 name matching and stangent says so.
- One bundled TypeScript version; code it can't parse is recorded as ❓, never skipped silently.
Rejected: parsing TypeScript from C# (no types); a separate npm tool (two installs to keep in step).
**Cost:** first-time `npm ci` per frontend takes minutes; Node is a hard requirement for frontend analysis.

## D31 · What the frontend map holds — 2026-10-19
- **From the TypeScript compiler (✅):** files, classes, functions, methods, properties; calls and uses;
  React components through JSX (`<SeriesCard />` is a resolved reference).
- **Angular templates:** parsed with Angular's own template parser (`@angular/compiler`, bundled).
  Child components link by selector (✅ when exactly one component declares it, 🟡 when several do);
  template bindings link to the component members they call or read. Both NgModule and standalone
  apps are supported (the owner's apps weren't specified; CleanArchitecture uses NgModules).
- **Pages:** Angular Router and React Router routes are frontend entry points, a new `pages` group in
  impact answers (`impact-report` v3, recorded per D20 when built).
- **Frontend → backend calls:** one frontend-HTTP detector, matched against backend endpoints like
  `http-services`:
  - hand-written calls (Angular `HttpClient`, `fetch`, axios): URL and verb from the code, 🟡;
  - in-house wrappers (e.g. Sonarr's `useApiQuery`): named in `detectorSettings`, like `handlerInterfaces`;
  - generated clients: read the generated file when it exists (exact URLs and types); otherwise the
    generator's naming rules (NSwag: `TodoListsClient.getTodoLists()` → `GetTodoLists` in `TodoLists`), 🟡.
    NSwag first; other generators each get their own small rule set when needed.
- Out of scope until a repo needs them: Vue and Svelte templates.
**Cost:** a larger extractor bundle (`@angular/compiler`); templates using new control flow (`@if`, `@for`) need a recent parser.

## D32 · How Phase 1b's frontend links are proven — 2026-10-19
| Link | Ground truth | Test |
|---|---|---|
| TypeScript code links (calls, uses, JSX, Angular templates) | the TypeScript / Angular type-checker | Test 4 for the frontend: rename a sampled method, property or component input; files that fail the type-check are what broke |
| Frontend call → backend endpoint | Kavita's committed `openapi.json` | every endpoint we extract should be in it; every linked frontend call should land on one that is |
| API data fields (C# `SeriesDto.Name` ↔ TS `Series.name`) | the same `openapi.json` | JSON field names per endpoint vs our naming-policy mapping and TS interface matches |
- Sonarr gets the history tests (1–2, 12k commits); CleanArchitecture a small hand-checked list of its endpoint links.
- Whole-stack breakage ("rename a backend field, which frontend code breaks") has no compiler answer;
  it's estimated from the two checks it's made of.
**Why:** the 🟡 cross-stack links are the ones no compiler checks; an OpenAPI file is a free, exact answer key.
**Cost:** the OpenAPI check only works where a project publishes its OpenAPI file.

## D33 · Phase 1c starts by hardening Test 2; 📜 is built only if history still helps — 2026-10-19
1b's "co-change beats the graph on Kavita" rests on too little to build on:
- 24 usable pairs on Kavita and 17 on Sonarr. On Sonarr, 9 of the 19 sampled pairs share one fix, so
  it's about 10 independent fixes.
- Kavita squash-merges mixed PRs ("A collection of bug fixes", "Polish 6"). Its pairs mostly link one
  bundle of edits to a later bundle, which co-change history naturally predicts.

So 1c's first step is the measuring stick, before any 📜 code:
- each fix counts once, however many change commits it pairs with;
- full history, not a 36-month window;
- nopCommerce and eShop pairs join Kavita and Sonarr;
- bundle PRs (Kavita's style) are reported as their own slice.

Then graph vs co-change is re-run. **If co-change adds nothing over "graph within 2 lines" on those
numbers, 1c stops there** and records why (the exact bar is part of 1c's done-when).
**Why:** a pass or fail on ~10–24 independent cases is noise; a day of benchmark work decides whether
the rest of 1c is worth building.
**Cost:** 📜 arrives later; the 1b history results are re-measured, not reused.

## D34 · 📜 links files, not members (members maybe later) — 2026-10-19
- A 📜 line joins two **file points already in the graph**: C#, TypeScript, Angular templates. Files
  that aren't on the map (`.resx`, `.sql`, `.csproj`, JSON config) are ignored for now.
- Only `git log --name-only` is needed: seconds, even on 12k commits, with dense evidence across the stack.
- Hub files (`Startup.cs`, a big service) are handled by thresholds and commit-size filters, not by going finer.
- **Follow-up, not in 1c:** member-level pairs, tried only if file-level 📜 proves too noisy on the hardened Test 2 (D33).
**Why:** member pairs need every past commit diffed and parsed (Roslyn and the Node extractor on old
code; minutes per build), give sparse evidence (two methods rarely change together twice), and can't reach templates.
**Cost:** coarse: "this file usually changes with that one" doesn't say which part of it.

## D35 · 📜 evidence is a count over each file's recent commits — 2026-10-19
- For a file A, look at **its last 20 commits** that survive the noise filters. A busy file is judged on
  recent months, a quiet one reaches further back; anything older counts zero. This is how "older
  history weighs less" (D26) is met without a decimal score.
- B is 📜 for A when B changed in **at least 3** of those commits **and at least 30%** of them.
- Evidence is shown as a count anyone can check with `git log`: "changed together in 7 of the last 12".
- Directional: A → B and B → A are judged separately. A hub as the starting point yields almost
  nothing (no partner reaches 30% of its many commits); a small file that nearly always drags `Startup.cs` along shows it.
- 20, 3 and 30% are starting defaults, tuned once on the hardened Test 2 (D33), then fixed in `stangent.yaml` (per repo, overridable).
**Why:** D26 rejected made-up numeric confidence; a half-life decay produces exactly that ("3.41").
**Cost:** a hard cut at 20 commits ignores older evidence entirely; a file that changes in bursts can
have its whole window filled by one burst.

## D36 · 📜 counts changes on main's own line — 2026-10-19
- History is read with `git log --first-parent` on the graph's commit. A PR merged with a merge commit
  counts **once** (the merge, diffed against main before it); its branch commits aren't counted again.
  Squashed PRs and direct commits count once each. Closed or unmerged PRs aren't on main, so they're ignored.
- Refines D26's "no merges": with `--no-merges`, a PR of 8 commits that each touch the same two files
  counts as 8 pieces of evidence for one change.
- **Rebase-and-merge PRs** land as separate commits and count separately. Git keeps no reliable trace of
  which commits formed one PR: on Sonarr, six unrelated commits share one timestamp because a
  maintainer rebased them together, so grouping by timestamp would invent PRs. Rebasing a branch
  *locally* before a squash or merge changes nothing.
- **Advised, not required:** squash merges for our own repos (one PR = one commit; revert per ticket, D11,
  becomes one commit). Merge commits work as well; rebase-and-merge with messy commits ("wip", "fix typo") inflates 📜.
- Sonarr (rebased, tidy commits) is in the benchmark, so the effect is measured, not assumed.
- **Follow-up, not in 1c:** group commits by PR through the code-host adapter (D24; GitHub's commit → PR
  lookup), built only if a team's rebased history measurably hurts the scores.
**Why:** one logical change should count once, the same reason D33 counts each fix once.
**Cost:** rebase-merged history is counted per commit; the fix needs network and a token at build time.

## D37 · 📜 noise filters — 2026-10-19
Four filters, each checkable by code and reported as a count ("312 commits dropped: 40 huge, 251 bots, …"):
| Filter | Example | Rule |
|---|---|---|
| Huge commits | "Upgrade to .NET 10", Kavita's "Polish 6" | more than 30 files on the map (default, tuned with D35's) |
| Bots | `dependabot[bot]`, Sonarr's "Automated API Docs update", Weblate translations | author ends in `[bot]`, plus a per-repo author list in `stangent.yaml` |
| Formatting sweeps | "Run dotnet format" | listed in the repo's `.git-blame-ignore-revs` (the file teams already keep for `git blame`); most are also huge |
| Reverts | "Revert 'Add series filter'" | a commit whose message says `This reverts commit <hash>`, **and** that commit: the change didn't stick, and keeping both counts it twice |
- Not filtered: lock files, `.csproj` and generated files (off the map, D34, so already invisible);
  EF Core migrations and model snapshots (true and useful: "changing `Series.cs` usually needs a migration").
- Rejected: diffing every commit to find whitespace-only changes (minutes per build instead of seconds).
**Cost:** a formatting sweep that isn't listed and isn't huge gets through; a bot not in the list
counts as a person until someone adds it.

## D38 · 📜 follows renames and moves, if it's cheap enough — 2026-10-19
- git's normal rename detection (`-M`) joins a moved file's old history to its current path, so a
  folder reorganisation doesn't wipe its 📜 evidence. Moving a C# file usually changes its `namespace`
  line, so exact-only matching would miss most moves.
- It needs the contents of commits that add and delete files; a blobless clone downloads them on demand.
- **Measured in 1c on nopCommerce.** If the first build gets more than ~30 s slower, 1c falls back to
  not following renames and records the gap.
- *Measured 2026-10-19:* nopCommerce has 1,838 renames of map files on its main line, so following them matters.
  Reading only the map's file types (`git log -- "*.cs" "*.ts" …`) keeps every one and cuts `git log -M` from
  7.1 s to 0.75 s on a full clone. A **blobless** clone is the exception: it fetches old contents over the network
  on *every* build (159 fetches, 34 s), so there renames aren't followed and the build warns. A clone with
  `--filter=blob:limit=200k` (315 MB vs 1.6 GB full) has them: 4.9 s on its first build, 0.8 s after.
**Cost:** a one-off download in blobless clones; renames git doesn't recognise (heavily edited in the same commit) still start fresh.

## D39 · 📜 lines live in the code graph (`code-graph` v3) — 2026-10-19
- A new line kind, `changes-with`, between file points in `.stangent/graph.sqlite`, labelled 📜, carrying its evidence count.
- Shared with the graph (D25): CI builds with full history, so a developer with a shallow clone still
  downloads 📜. "Same commit → same graph" holds: history up to a commit and the filter inputs
  (`stangent.yaml`, `.git-blame-ignore-revs`) are fixed by the commit.
- A shallow clone would build a different graph for the same commit, so the graph records it
  (`meta.history`: full or none) and `loadWarnings` says so.
- **D20:** 1c reopens 1a and bumps `code-graph` v2 → v3. 1b consumes v2, so it's marked `needs review`
  with a follow-up: confirm nothing in 1b breaks, then move it to v3.
- Rejected: a separate `history.sqlite` (two files for CI to share and impact to join, and D26 already defines 📜 as a graph line label).
**Cost:** a contract bump and a `needs review` on 1b for what is, for 1b, an additive change.

## D40 · 📜 in impact answers: its own group, last, capped, never walked — 2026-10-19
- **Never walked.** 📜 is looked up one step out from the changed files only; the walk never follows a 📜
  line or continues from a file found through one (chains of "usually" are how D27's 0.5% precision happened).
- **Its own group, after every ✅/🟡 group:** "usually changes together", top 10 by count, each with its
  evidence ("7 of the last 12"); `--expand history` shows the rest. A hint only, never blocking (D26).
- A file the graph already found keeps its ✅/🟡 place and gets an "also 📜" tag. Re-ranking by that
  tag waits until the benchmark shows "found by both" is clearly more precise.
- **Contract:** `impact-report` v3 → v4 (the group and the tag). Only 1a provides it and no phase consumes it yet.
- Rejected: mixing 📜 into the existing groups (hints among certain answers); corroboration only (drops
  the files only history finds, which is what co-change won with on Kavita).
**Cost:** one more group to read; a capped list can hide the 11th file that mattered.

## D41 · 📜 is recomputed in full, not incrementally — 2026-10-19
- 📜 is recomputed in full whenever the graph's commit or a filter input (`stangent.yaml`,
  `.git-blame-ignore-revs`) changes, and skipped when neither did.
- Reading the main line and counting pairs should take seconds (≤30 files per commit, so ≤~900 pairs each).
- Incremental history is built **only if nopCommerce's full recompute takes more than ~10 s**.
- Test 3 ("incremental = full", two builds byte-identical) covers 📜 with no extra work.
**Why:** incremental history has to handle force-pushes, branch switches, filter changes and D35's
sliding 20-commit window, each a correctness bug in waiting, to save a few seconds.
**Cost:** every new commit pays the full recompute.

## D42 · 📜 uses whatever history the clone has; CI's graph carries it — 2026-10-19
- `stangent graph build` never fails over a shallow clone. It records the history it used (`meta.history`:
  the commit range, or none) and warns when there's none.
- **CI builds the shared graph (D25) from a full-history clone without large files** (`fetch-depth: 0`,
  `filter: blob:limit=200k`), set in the thin workflow file (D24). `actions/checkout`'s default (depth 1) would
  ship graphs with no 📜. *Refined 2026-10-19:* `blob:none` was the first choice, but it can't follow renames
  without fetching on every build (D38, measured).
- No cut-off date: D35 reaches back 20 commits per file, and a quiet file needs years of history to fill that.
- **A local rebuild on a shallow clone keeps the downloaded 📜 lines**, marked with the commit they were
  computed at, rather than dropping them. A few commits stale is fine for a hint.
- Rejected: failing without full history (annoying for every developer); dropping 📜 on shallow builds
  (most local rebuilds would lose it silently).
**Cost:** the graph can carry 📜 a few commits older than its code; `meta.history` says by how much.

## D43 · 📜 is tuned on two repos and judged on two others — 2026-10-19
| Role | Repo | History | How changes land |
|---|---|---|---|
| Tune | nopCommerce | 16,295 commits since 2010 | direct + merges |
| Tune | Sonarr | 12,099 since 2010 | direct / rebased |
| Judge | Kavita | 3,452 since 2020 | squash bundles + direct |
| Judge | eShop | 349 since 2023 | squash |
- D35's and D37's defaults are chosen on the tuning repos only, then **frozen** before the judging repos are scored.
- That also tests whether defaults tuned on direct-commit repos survive squash-merge repos, which is
  what the company's repos (style unknown) will get.
- **Floor:** a repo with fewer than 30 independent Test 2 fixes is reported but doesn't count toward pass
  or fail. eShop will probably fall below it, so the verdict may rest on Kavita alone (bundles and all).
  Accepted; the report says so.
- CleanArchitecture stays out (a template with little history).
- *Refined 2026-10-19 (Gate 1 run):* Kavita has 23 usable fixes and eShop 1, so neither reaches the floor and
  Gate 2 had no judging repo. **bitwarden/server** joins the judging side: 7,963 commits, every one a squashed
  PR, ~15% of them fixes, a different team and style from both tuning repos. Kavita and eShop are still
  reported. Rejected: lowering the floor or raising Test 2's 15-file cap after seeing Kavita's number (moving
  the goalposts); splitting Sonarr by time (judging on the tuning repo's own team and style).
**Why:** tuning on the repos that are then scored measures the tuning, not 📜.
**Cost:** half the history is spent on tuning; a thin judging side.

## D44 · 1c's done-when: two gates, judged by code — 2026-10-19
Both gates use the hardened Test 2 (D33), each fix counted once.
- **Gate 1, before any 📜 code (all four repos):** "graph within 2 lines" vs "graph within 2 lines + the plain
  co-change baseline's top 10". If adding history raises recall on no repo with ≥ 30 fixes, 1c stops (D33).
- **Gate 2, the done-when (judging repos, D43, frozen defaults):**
  | Check | Bar |
  |---|---|
  | 📜 adds something | recall of graph + top-10 📜 beats graph alone, and the 95% range of the gain (resampling fixes, fixed seed) stays above zero |
  | Filters help, not hurt | 📜's precision ≥ the plain baseline's, and its recall ≥ 90% of the baseline's |
  | Answers stay short | the median answer grows by ≤ 10 files |
  | Cheap | ≤ 10 s added to a nopCommerce build (D41); ≤ 30 s added to the first build by rename following (D38) |
  | Stable | Test 3 holds: byte-identical builds, incremental = full |
- **If "adds something" fails,** 📜 ships **off by default** (`history: off` in `stangent.yaml`) and the
  report says why: D26's "a label that mostly adds false alarms is switched off", applied literally.
- Rejected: report-and-decide with no pre-agreed bar; also requiring graph + 📜 to beat plain co-change alone.
**Why:** a pre-agreed bar can't drift toward whatever the numbers turn out to be.
**Cost:** with 30–60 fixes, a real but small gain may not clear "above zero"; strict on purpose.

## Open questions
- Dashboard sign-in for BA/PM: company SSO or GitHub accounts? (Phase 8)
- "ministack" vs LocalStack vs Testcontainers for integration tests: was it a deliberate choice? (Phase 5)
- Confirm the company's Claude plan excludes training on its data (D24), before running on client code.
- Bug tickets: how is a bug verified before it's fixed? Likely a test that fails because of the bug
  (the D18 check), then the fix makes it pass. What happens when it can't be reproduced, and how does
  the ticket's description become a starting point for the graph? (Phases 3–5)
