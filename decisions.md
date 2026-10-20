# Decisions

Settled while grilling `spec.md`. Each entry: what was decided, why, and what it costs.
Change a decision by editing its entry and noting the date — don't silently contradict it elsewhere.

## D1 · Built for the owner's own use first — 2026-10-18
Stangent targets the owner's real workflow and stack. Making it installable for
others (spec item 11) is a later phase.
- *Revised 2026-10-20:* "own use" means the owner's company **and** personal projects alike; nothing should assume
  a single company, its tools or its documents.
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
- *Refined 2026-10-19 (D52):* the chain is FSD section → reference page → code symbols; every hop is checked.

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
- *Refined 2026-10-19 (D52):* "the spec" is now the FSD, the generated reference and the links between them;
  verification (the text hash) applies to each FSD section ↔ page link and each decision.
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
- *Clarified 2026-10-19 (D51):* not "code as fallback": the FSD and the code are always read together; with no FSD,
  the spec comes from code alone.
- *Revised 2026-10-19 (D52):* no merged requirement list. The FSD and a reference generated from code stay
  separate and are linked; the provenance labels become the states of those links.

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
- *Deferred 2026-10-20 (Phase 2 plan):* versions and supersession wait for a project with several FSD versions;
  Phase 2 handles one version per document, `.docx` only.

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
- *Refined 2026-10-19:* phase 1d, an agent-fitness pilot, runs between 1c and 2 (D45).

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
  *Confirmed 2026-10-20:* the Claude account keeps its data out of training.
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
- *Tuned 2026-10-19 (D43):* the rule picked **window 10, minShared 3** (minShare then never binds: 3 of ≤ 10 is ≥ 30%;
  its default is 0.2) and D37's size cap at **50** files. Pooled over nopCommerce and Sonarr, graph + 📜 recall 0.500 vs
  0.457 for the graph alone, and 📜's precision beat the plain baseline's on both (0.015 vs 0.012, 0.028 vs 0.026).
  The starting values (20/3/30%/30) missed by 0.001 on nopCommerce. The margins are small; the rule was fixed beforehand.
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
| Huge commits | "Upgrade to .NET 10", Kavita's "Polish 6" | more than 50 files on the map (tuned with D35's; started at 30) |
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
- *Result 2026-10-19:* Gate 1 passed (Sonarr, bitwarden/server). Gate 2 passed on bitwarden/server with every
  check (recall +0.025, range 0.009–0.047; 📜 precision 0.031 vs 0.022; +4 files; 0.9 s; stable), so 📜 ships on
  by default. Gap found: the tuning rule (D43) checked precision only, and with the frozen settings Sonarr misses
  Gate 2's recall check. Next tuning uses all of Gate 2's checks as eligibility.
**Why:** a pre-agreed bar can't drift toward whatever the numbers turn out to be.
**Cost:** with 30–60 fixes, a real but small gain may not clear "above zero"; strict on purpose.

## D45 · Before Phase 2, check the graph can do its Phase 4 jobs; splitting first — 2026-10-19
Every benchmark so far scores the graph's predictions; none shows it helps the agents of Phase 4. There it
has three jobs: **(1) split** a ticket into tasks that really are independent, **(2) scope** each subagent's
context to a slice, **(3) check** what a change may have broken (roughly Test 2).
- A pilot (phase 1d, D19 amended) runs before Phase 2. **Job 1 first:** take past multi-part changes, let the
  graph split them into "independent" groups, apply each group alone, and check it builds and passes tests.
  No agent runs: cheap, deterministic, fits the benchmark harness.
- **Job 2 next:** the same task given to a subagent with and without its graph slice; tokens, build/tests, misses.
- The bar is agreed before any run (as D44). A failure reshapes Phase 4 before Phases 2–3 are built on it.
- End-to-end test links (Playwright `goto` → pages) wait until the pilot reports.
**Why:** the graph's accuracy doesn't show it makes agents safer or cheaper; 1c's gains were already small
(+2 to +6 points), and a bad split makes parallel subagents break each other.
- *Changed 2026-10-20:* Phase 2 may be **built** before the split check reports. The verdict decides Phase 4's
  shape (parallel or mostly sequential), not Phase 2's reference pages or FSD links. Phase 4 still waits for it,
  and part 2 (context scoping) may add to Phase 2 later. Phase 2 is built in its own worktree, so the running
  benchmark's binaries aren't rebuilt under it.
**Cost:** Phase 2 starts later; job 2 needs agent runs, which vary between tries and cost tokens.

## D46 · A split group "holds up" if it compiles alone, then passes the unit tests — 2026-10-19
Each group the graph calls independent is applied alone onto the change's parent commit.
- **Step A, every case:** it compiles (`dotnet build`; `tsc --noEmit` for frontends). A group that doesn't is an
  unsafe split: the graph missed a link.
- **Step B, groups that pass A:** the repo's unit-test projects run. A test that passes on the parent and on the
  full change but fails with the group alone is also an unsafe split.
- Integration tests (databases, services) are left out of the pilot.
Rejected: compile only (misses behaviour breaks); every test including integration (a test environment per repo
for little extra signal in a pilot).
**Why:** compiling is exact, cheap and repeatable (D18), and Test 4 already runs it; unit tests catch behaviour
breaks without infrastructure.
**Cost:** breaks only integration tests would catch go unseen; flaky unit tests must be screened out on the parent first.

## D47 · Split cases: recent main-line PRs from bitwarden/server and Sonarr — 2026-10-19
- **Source:** main-line commits (`--first-parent`, 1c's reader); in a squash-merge repo each is one merged PR,
  about a ticket's size.
- **Size:** ≥ 3 changed methods across ≥ 2 files, ≤ 15 files (Test 2's cap). Smaller has nothing to split; bigger
  is a refactor or a bundle.
- **Health:** the parent and the change both build and pass their unit tests; otherwise a failure says nothing
  about the split.
- **Repos:** **bitwarden/server** (squash-only, judged 1c) and **Sonarr** (adds `tsc` on a React frontend).
  nopCommerce is left out: slow builds, little extra.
- **Count:** newest first, examine cases until **30 are split** into 2+ groups, at most **200 examined** per repo.
  The split rate counts everything examined. "Safe" is judged on the plain percentage, with its 95% range
  reported; under 30 split cases, "safe" is below the floor for that repo (as D43).
- *Refined 2026-10-19:* first set at ~40 cases per repo, which gives only 10–16 split cases: too few to judge 90%.
**Why:** real changes of ticket size on repos whose history the benchmark already reads.
**Cost:** two repos, ~80 cases: a pilot's evidence, not a verdict for every codebase; recent cases lean towards
how those teams work today.

## D48 · Splits come from the after-change graph, joined within 2 steps (✅ + 🟡) — 2026-10-19
- **Graph:** built at the change itself, so added methods exist. This is the best case: more than a Phase 4
  planner knows before code is written. A pass doesn't prove Phase 4; a fail is decisive.
- **Groups:** changed methods joined by ✅ or 🟡 lines within 2 steps (through unchanged points too), in
  either direction, are one group. **Judged on this rule;** direct-only (1 step) and "+📜" are reported beside it.
- **Edits outside methods** (usings, fields, attributes) go with the group of the changed methods in their
  file; a new type is a point, so its users link to it.
- **Files with no changed methods** (`.csproj`, config, resources) form a shared group, applied together with
  every group, as Phase 4 would treat shared setup.
- *Refined 2026-10-19, while building:* these are mechanics, not a change to the rule.
  - **Owners.** A changed line belongs to the innermost member around it: method, property, field, or a TypeScript
    component. A line inside a type but outside its members makes the type a changed member only when it carries
    meaning (a header, base list or attribute; not a blank line, brace or comment).
  - **A changed type keeps its changed members.** Its text holds theirs, so a new class is never applied without
    its methods.
  - **Hunks** are cut into one piece per owner. Old and new runs of the same owner are matched; the rest become
    separate insertions and deletions, so any subset still applies as text. Only a line owned by two members at
    once (`int a, b;`) can't be cut, and it is counted as "joined by a shared hunk".
  - **Deleted members** join through the parent's graph with the same rule (1 or 2 steps).
  - *Fixed after bitwarden's first run:* 8 of its 14 failed splits were the tool's own cuts, sorted by a fresh agent
    (D61). The fixes:
    - A member whose signature changed is one member, matched by name without its parameter list.
    - A type or member that moved to another namespace or file is one member, when its name matches exactly one
      deleted and one added.
    - A type that exists on one side only is a changed member, so its members go with it.
    - A positional record's header line belongs to the type, not its properties.

    That run is kept as a record, not as the verdict.
  - **📜** comes from commits older than each change, as in 1c.
**Why:** Test 2 showed the graph's recall lives within 2 steps; direct-only misses too much.
**Cost:** within 2 steps joins more, so fewer groups; a file whose changed methods fall into two groups has its
other edits counted in both.

## D49 · The split check's done-when: safe, useful, better than split-by-folder — 2026-10-19
Agreed before any run (as D44), on the D48 rule:
| Check | Bar |
|---|---|
| Safe | of the cases split into 2+ groups, ≥ 90% have every group hold up alone (D46), **on each repo** |
| Useful | ≥ 25% of all cases are split into 2+ groups |
| Better than naive | safer than split-by-folder, whose split rate is no more than 5 points above the graph's |
- **Both pass:** Phase 4 plans parallel subagents from the graph; job 2 (context scoping) is next.
- **Safe fails:** Phase 4 doesn't trust the split: every parallel run is merged and built together before it
  counts, or tasks run one after another.
- **Useful fails:** parallel subagents seldom pay off; Phase 4 is mostly sequential.
**Why:** Phase 4 checks the merged result anyway (D18), so rare misses are caught; a 1-in-5 miss rate would
waste more than parallel runs save, and below 25% parallelism isn't worth building.
**Cost:** a pre-set bar can fail a split that would have been good enough in practice.

## D50 · stangent is a standalone rebuild, modelled on the company's pipelines — 2026-10-19
stangent depends on nothing the company runs. The company's pipelines are the **model to match**, not a system to
plug into:
- **Spec from code:** a static call graph plus E2E tests produce a cited spec per screen, reconciled each release.
- **Planning → review:** a separate pipeline from plan to reviewed change.
Phase 2 aims to do what the first one does, on stangent's own graph (Phases 1a–1c); Phases 4 and 6 aim at the second.
Where Phase 2 differs from the model, the decision says why.
- *Corrected 2026-10-19:* first recorded as "stangent is the layer under the company's pipelines"; the owner
  clarified that it's a rebuild that only borrows their shape.
**Why:** the company's pipeline shows the shape works in practice; copying a proven shape beats inventing one.
**Cost:** only a one-line description of the model is known here, so details (what "cited" and "reconciled" mean
in practice) are decided in this grilling, not copied.

## D51 · The spec is built from the agreed FSD and the code together — 2026-10-19
- The **functional specification document (FSD)** agreed with the PM and the client and the **code** are always read
  together, each requirement labelled with D13's provenance: both agree, documented only, inferred from code only, or
  conflict. Code isn't a fallback: it adds what the FSD never mentions. With no FSD, the spec comes from code alone
  (the model's way, D50).
- On a conflict the FSD is what was agreed, but stangent still doesn't pick a winner: BA/PM do (D15).
- The code graph and everything else (E2E tests, later tickets) are compared with the FSD to find inconsistencies:
  the discrepancy queue (D14).
- This is where stangent departs from the model (D50): a spec drafted from code copies the code's bugs, so it can't
  show where the code drifted from what the client agreed.
**Why:** what the client signed off on is what the system owes them; code is evidence of what was built, not of what was asked.
**Cost:** document import (D13, D15, D16) stays in Phase 2's scope, and judging "does this code do what the FSD says" is
the step code can't fully check (D18).

## D52 · Two documents, linked: the FSD and a reference generated from code — 2026-10-19
Borrowed from the company's pipeline (D50). Replaces D13's single merged spec.
| Layer | What it is | Who writes it | Checked by |
|---|---|---|---|
| **FSD** | the agreed documents, converted to text deterministically; **never rewritten by AI**; sections keep their own IDs | the client / BA, outside stangent | conversion is code; same file → same text |
| **Reference** | pages describing what the code does, generated from the graph; every statement cites symbol IDs | AI proposes | code: every cited symbol exists in the graph (D18); recorded with its inputs' hashes and the commit it was built from |
| **Links + decisions** | FSD section ↔ reference page, and every gap, conflict and decision | BA/PM and devs | the text hash on both ends (D11): editing either side drops a link back to unverified |
- The link states are D13's labels: **agree**; **gap** (FSD says it, code doesn't); **undocumented** (code does it, FSD
  doesn't, which the company's pipeline doesn't list); **conflict** (they disagree: BA decides which is right; if the code
  is wrong, it goes to PM/dev as a ticket).
- The reference is committed: it's AI output, which isn't reproducible, so it's stored rather than regenerated (D18).
  The graph it cites is not (D25).
- The chain FSD section → reference page → code symbols lets impact answers reach FSD sections (D10, D27).
Rejected: one merged spec (D13 as first written): an LLM rewriting the FSD into requirements is where hallucinations get in,
and it mixes "what was agreed" with "what was built".
**Why:** less AI (nothing rewrites the client's words), each layer has its own check, and the only layer people write is small.
**Cost:** two documents to read instead of one; an FSD section and a page rarely match one to one, so links are many-to-many.

## D53 · Links: code finds candidates, AI judges with quotes, code checks, a person approves — 2026-10-19
How an FSD section ↔ reference page link and its state (D52) are decided:
1. **Code proposes candidate pairs** where names match: the FSD's "Checkout screen" ↔ the `/checkout` route, an FSD
   field label ↔ a DTO property, a screen name ↔ a component.
2. **AI judges each pair** (agree / gap / conflict / undocumented), quoting the exact FSD sentence and the exact
   reference sentence it relies on.
3. **Code checks** both quotes exist word for word, both IDs exist, and every symbol the quoted reference sentence cites
   is in the graph. A failed check is rejected before anyone sees it.
4. **A person approves.** "Agree" links are approved in bulk per FSD chapter; gaps, conflicts and undocumented items one by one.
- Until approved, a link is unverified, and impact answers say so.
Rejected: approving every link one by one (a 200-page FSD can produce over a thousand links, which turns approval into
rubber-stamping); people linking everything by hand (slow, and it skips the code checks).
**Why:** code does what it can (candidates, quote and symbol checks); the person's attention goes to the findings.
**Cost:** bulk approval of "agree" is where a wrong AI judgment can slip through; telemetry counts links later
reopened, to show whether it does (D21).

## D54 · One reference page per screen, plus pages for the backend no screen reaches — 2026-10-19
- **Screen pages:** one per frontend route, with the route as its stable ID (`page:/checkout`). It covers what the screen
  shows, each action → the endpoint it calls → the rules and validations behind it → side effects (events, emails,
  tables written). Every statement cites symbols (D52).
- **Backend pages:** one per entry point no screen reaches (APIs only other services call, event handlers, scheduled
  jobs), grouped per area, with the entry point as the ID.
- **Shared components:** a component used by 3 or more screens gets its own page; the screens link to it.
- **Each page's input is the graph's slice for it:** the walk from its entry point within impact's depth limit (D27).
  That's what keeps each AI call small and grounded.
- Server-rendered screens (Razor/MVC, no frontend route in the graph): the controller action that returns the view is
  the screen. Default until a real repo needs it.
Rejected: screens only (backend-only behaviour such as jobs and service-to-service APIs would go undescribed); pages per
feature (where one feature ends and the next begins needs a person to decide).
**Why:** the IDs come straight from the graph, so they're stable and found by code, not chosen by an AI.
**Cost:** a renamed route changes a page's ID, so its links need the rename repair D10 already plans.

## D55 · The reference catches up per change: detected on each PR, regenerated on merge — 2026-10-19
1. **Every PR, code only:** the graph diff says which pages' slices changed (D54); the PR lists them with their linked
   FSD sections. No AI, no tokens.
2. **On merge to main, AI:** only those pages are regenerated, each version recorded with its commit and input hashes
   (D18). Links whose page text changed drop back to unverified through the text hash (D11) and join the queue.
3. **Per release, optional:** a full sweep regenerates any page whose inputs changed and was missed.
- CI holds a Claude API key as a secret (D24); the per-repo no-AI switch turns step 2 off, leaving step 1.
- Refines D14's "kept current per area": the unit is the page, and the trigger is the merge.
Rejected: regenerating on every push (tokens spent on work in progress); per release only, as the company does (drift is
found weeks after the change, when its author has moved on); a manual regenerate command only (pages go stale when nobody runs it).
**Why:** drift shows up in the PR that causes it, and AI runs once per merged change.
**Cost:** AI calls in CI on every merge that changes behaviour; a page regenerated with an unrelated wording change still
reopens its links (mitigated if regeneration reuses the old page when its inputs' hashes didn't change, D18).

## D56 · Phase 2 is proven on an FSD for Sonarr with planted discrepancies — 2026-10-19
- **Repo:** Sonarr: a real React app whose routes are in the graph (D31), documented screen by screen in the Servarr
  wiki, already in the benchmark with full history (D6).
- **FSD:** written in our own words from the wiki for 10–15 screens, as a `.docx`. *Refined 2026-10-20:* FSDs follow no
  standard (the owner's projects vary), so some chapters are numbered and some are plain prose under headings.
- **Planted discrepancies,** listed in an answer-key file: **gaps** (sections for features Sonarr doesn't have),
  **conflicts** (changed rules, e.g. "at most 3 tags per series" where the code allows any number), **undocumented**
  (real screens or behaviours deleted from the FSD).
- **Scored by code:** planted items found (recall) and unplanted findings raised; a sample of the unplanted ones is
  hand-checked once (some will be real drift between the wiki and the code).
- **Also measured:** tokens and time per page, the D18 check failure rate, and the share of candidate pairs found by
  code vs by the AI (D53).
- After Sonarr, a dry run on a real FSD from one of the owner's projects (summarised, nothing committed).
Rejected: a company FSD first (no answer key, and not cleared yet).
**Why:** an answer key turns "it found 40 discrepancies" into a score; the same reason 1a–1c used compilers and history.
**Cost:** a planted test is tidier than a real FSD (vague wording, one section covering five screens), so it overstates
quality; the company dry run is the correction.

## D57 · Phase 2's done-when: finds what's planted, not too noisy, grounded, repeatable — 2026-10-19
Agreed before any run (as D44, D49), on the Sonarr FSD (D56) with **30 planted items: 10 gaps, 10 conflicts, 10 undocumented**.
| Check | Bar |
|---|---|
| Finds what's planted | ≥ 80% of planted items overall, and ≥ 60% of each kind |
| Not too noisy | ≤ 30% of all findings are false alarms (unplanted, and the hand check says not real drift) |
| Grounded | every page and link reaching the queue passes the D18 checks; tests show a planted fake citation is rejected |
| Repeatable | the same `.docx` → byte-identical text; a re-run with unchanged inputs reuses the recorded AI output (zero tokens) |
| Cost | tokens and time per page reported; no bar yet (D28) |
- **If "finds" or "not too noisy" fails:** reference pages and links still ship, but discrepancy findings are **off by
  default** and the report says why (D44's rule, applied to findings).
- A 95% range (fixed seed, resampling planted items) is reported beside each share, as in D47.
Rejected: stricter bars or 60 planted items (a bigger FSD to write for a first measure); no bars (the bar would drift
toward the result).
**Why:** a pre-agreed bar can't move to fit the numbers.
**Cost:** with 10 per kind, one miss moves a kind's score by 10 points; the range makes that visible.

## D58 · Decisions are fields in a committed YAML file; the website later edits the same file — 2026-10-20
- Each link and finding (D52, D53) is an entry in a committed YAML file: the FSD section, the page, the state, both
  quotes, a `decision` (agree / code-wrong / fsd-wrong / defer) and `decided_by`.
- **Until the dashboard (Phase 8):** a person edits `decision` and opens an ordinary PR. Bulk approval of "agree" is
  one edit over a chapter (D53).
- **The dashboard** is a screen over the same file that opens the same PRs (D12). The file format is the contract;
  nothing is redone when the website arrives.
- **Checked in CI:** the schema and allowed values; both quotes still match the current FSD and page (the D11 hash),
  otherwise the decision drops back to undecided; `decided_by` is filled from git (the PR's approver or merger), never typed.
- "code-wrong" becomes a draft Jira ticket in Phase 3; until then it's listed.
Rejected: bot-opened review PRs with comment commands (machinery the dashboard replaces anyway); an early approval page
(pulls Phase 8 and sign-in forward).
**Why:** the simplest thing that keeps a checked, attributed record, and the website reuses it unchanged.
**Cost:** until Phase 8, reviewing means editing YAML in a PR, which is clunky for non-technical BAs.

## D59 · Phase 2 is split: 2a reference from code, then 2b FSD and discrepancies — 2026-10-20
| Sub-phase | Delivers | Proven by |
|---|---|---|
| **2a. Reference from code** | pages per screen and backend entry point (D54), every citation checked (D18), AI output recorded and reused, stale pages listed per PR and regenerated on merge (D55) | Sonarr: grounded, repeatable, cost per page; the owner hand-reads ~5 pages |
| **2b. FSD and discrepancies** | `.docx` import, candidate pairs, the AI judge with quote checks (D53), the decisions file (D58) | the planted Sonarr FSD against D57's bars |
- 2a is useful alone: it's the company's model (D50) and the spec of any project with no FSD (D51).
- 2b's conflicts depend on the pages capturing the rules ("max 3 tags"); proving the pages first keeps a 2b failure readable.
- One phase file (`phases/2-spec.md`) and one roadmap row until building starts; split into two files then, as Phase 1 was.
**Why:** the same reason as D29: something working and scored sooner, and a chance to change course before the second half.
**Cost:** 2a has no answer key for page accuracy; until 2b, it's mechanical checks plus a hand-read.

## D60 · AI steps run in Claude Code; the CLI prepares their input and checks their output — 2026-10-20
The first AI steps in the project (2a's pages, 2b's link judgments) and the pattern later phases follow:
1. **`stangent ... prepare`** (code) writes the step's input as one bundle file: the graph slice (D54) or the candidate
   pair (D53), the instructions and the output schema.
2. **A Claude Code subagent from the stangent plugin** writes the output as JSON in that schema. Its tools are limited to
   reading the bundle and writing the result, so it can't wander outside the slice.
3. **`stangent ... check`** (code) validates the schema, the citations and quotes (D18), and records the output with its
   inputs' hashes and the model used. A failed check returns one specific error for one retry.
- **Locally:** run through the plugin's commands on the developer's own Claude Code (D24); no separate API key.
- **In CI** (merge-time regeneration, D55): headless Claude Code (`claude -p`, JSON output) with a token held as a secret.
- **Telemetry** (D21) takes tokens, cost and model from Claude Code's JSON result.
Rejected: the CLI calling the Claude API directly (an API key and its separate billing for everyone who runs it, and a
second way to call AI beside the one every user already has).
**Why:** reuses the tool everyone has; the CLI stays deterministic and testable without an LLM (D9), and every AI output
still passes a code check before anything uses it.
**Cost:** CI needs headless Claude Code and a token; prompt caching and the Batch API's half price aren't directly
available; the subagent's tool limits are a setting, so `check` must still reject anything citing outside its slice.

## D61 · Verification by a separate agent with a fresh context — 2026-10-20
Whenever an AI step has to judge whether work is right (reviewing a change, sorting benchmark failures,
checking a report's claims), a **separate agent** does it, starting from a fresh context. It gets the work,
the criteria and the evidence, not the author's reasoning or conversation.
- Applies to every phase, and to how stangent itself is built.
- **Code checks still come first (D18).** A compiler, a test or a schema beats any reviewer. The fresh agent
  covers what code can't check, and its findings are themselves checked against evidence (a file, a test
  result, a line).
- **The author doesn't overrule it silently.** A finding is fixed, or rejected with a stated reason a person can see.
**Why:** an agent reviewing its own work shares the blind spots that produced it and tends to confirm its own
reasoning. A fresh context judges only what's on the page, as a second engineer would (the owner's rule,
in line with evaluator/reviewer patterns from agent builders).
**Cost:** more tokens and time per check; the evaluator needs a brief good enough to judge without the history.

## D62 · Phase 2 applies D61: a fresh verifier for pages and for "agree" links — 2026-10-20
Where Phase 2 has an AI step judging whether work is right, a separate subagent with a fresh context does it, after the
code checks (D18):
- **Pages (2a):** `stangent-page-verifier` gets the bundle and the finished page only, and flags statements the slice's
  code doesn't support, quoting the contradicting line. Code checks the quoted line exists; findings go back to the
  writer for its one retry, each fixed or rejected with a stated reason recorded on the page.
- **Link judging (2b):** the judge is already its own fresh subagent, never the page writer's context.
- **"Agree" verdicts (2b)** get a second fresh check before bulk approval; a disagreement turns the link into a finding,
  reviewed one by one. This is the guard D53 named as its cost.
- **The end-of-phase report's claims** are checked against the results files by a fresh agent.
Rejected: a second check on every finding (gaps, conflicts and undocumented items already go to a person one by one).
**Why:** D61 is the owner's rule for every phase; these are the steps where a writer or judge would otherwise grade itself.
**Cost:** roughly double the AI calls per page, plus one per "agree" link; reported apart from the writer's in 2a and 2b.

## Open questions
- Dashboard sign-in for BA/PM: company SSO or GitHub accounts? (Phase 8)
- "ministack" vs LocalStack vs Testcontainers for integration tests: was it a deliberate choice? (Phase 5)
- Bug tickets: how is a bug verified before it's fixed? Likely a test that fails because of the bug
  (the D18 check), then the fix makes it pass. What happens when it can't be reproduced, and how does
  the ticket's description become a starting point for the graph? (Phases 3–5)
- Parallel subagents: should the orchestrator fix the contract first (API shapes, shared types), then run
  the sides in parallel against it? And if a subagent finds mid-task that the contract must change, does it
  stop and hand back, or change it and tell the others? Leading answer: contract first; stop and hand back.
  Grilled with Phase 4, after the 1d verdict. (Phase 4)
