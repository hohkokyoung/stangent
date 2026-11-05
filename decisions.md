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
- *Answered 2026-10-25 (D73):* the Atlassian MCP can download attachments (`downloadJiraIssueAttachment` returns a
  short-lived URL), and it accepts an API token for unattended use once an organization admin enables it. The
  stangent CLI itself is an MCP client (D73).

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
- *Refined 2026-11-02 (D99, not built):* the chain gains a finer first hop: requirement (from the FSD) ↔ page
  statement ↔ code symbols. Tests carry scenario ids as well as requirement ids.

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
- *Revised 2026-11-02 (D99, not built):* the features / journeys / acceptance criteria structure returns, built on the
  FSD from word-for-word quotes rather than replacing it; the BA approves each requirement. Spec for existing code with
  no FSD: as-built drafts from the reference pages, context only, adopted by the BA one at a time.

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
- *Refined 2026-11-02 (D99, not built):* until the dashboard, the BA approves requirements by ticking boxes in the
  spec-only PR's description and approving the PR; `decided_by` comes from git.

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
- *Revised 2026-11-02 (D99, not built):* requirements with provenance return as a layer built on the FSD (every item
  quotes its source; a document or a change-request ticket). "Code as fallback" becomes as-built drafts from the
  reference pages: context only, never judging bugs, adopted by the BA one at a time.

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
- *Replaced 2026-11-03 (D99):* `/stangent:adopt` runs the existing steps in order; there's no separate audit.

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
- *Partly built 2026-11-04 (D117):* a new version of one document comes in as a candidate, and a person promotes it. Version dates, draft or approved status, and clashes across documents aren't built.
- *Changed 2026-11-03 (D111):* PDF and Excel are to be imported as well.
- *Picked up 2026-11-02 (D99, not built):* `fsd reconcile` carries out this entry. The version date is the one the
  document states (`--effective`), not the file's own metadata (which changes when a file is copied); the BA confirms
  it. Draft vs approved status is kept: only an approved document supersedes; a newer draft's differences show as
  proposed changes.

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
  *Changed 2026-11-04 (D115):* built in 1.2, as separate lines nothing walks, so they can't affect what the pilot measures.
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

    That run is still the one judged on, as bounds (D64).
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
- *Revised 2026-11-02 (D99, not built):* the "Rejected: one merged spec" reason is answered rather than dropped: a
  structured spec is built on top of the FSD, which stays the record byte for byte; every item quotes it word for word
  (checked by code), a fresh agent checks each item says no more than its quotes, and the BA approves each one. The FSD,
  the reference and the links stay separate layers.

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
- *Refined 2026-10-20 (Phase 2b build, after a fresh review):* a quote is a sentence, not a fragment: word for word, and
  at least 4 words or a whole table cell, list item or statement. A section whose candidate pages have no recorded
  version is reported as failed, never turned into a gap. "Undocumented by code" only covers the FSD's scope
  (`fsd.scope`): the areas it says it covers, as route prefixes, so an FSD for part of a system doesn't flag the rest.

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
  *2026-11-03 (owner):* `Documentation.pdf` from the owner's `cyberbullying-detection-system` is treated as a spec agreed with a client
  for import and extraction tests. Its code is Python and JavaScript, so links through the graph can't run on it; those still need
  a C# project with a real FSD.
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
- "code-wrong" becomes a draft Jira ticket in Phase 3; until then it's listed. *Changed 2026-10-25 (D80): not in
  Phase 3; it stays listed and a person creates the ticket.*
Rejected: bot-opened review PRs with comment commands (machinery the dashboard replaces anyway); an early approval page
(pulls Phase 8 and sign-in forward).
**Why:** the simplest thing that keeps a checked, attributed record, and the website reuses it unchanged.
**Cost:** until Phase 8, reviewing means editing YAML in a PR, which is clunky for non-technical BAs.
- *Refined 2026-10-20 (Phase 2b build):* a decision drops back to undecided when either quote no longer matches its
  section or statement (each quote's hash is stored), not on any edit to the section: a typo fixed elsewhere in a long
  section shouldn't reopen every link in it. An entry's id is the hash of its link and quotes, so an edited quote is
  caught as a hand edit. On merge, CI fills `decided_by` and runs `trace check --fix` in the bot's spec PR.
- *Refined 2026-11-02 (D99, not built):* requirements are approved by tick boxes in the spec-only PR's description,
  not by editing YAML; `decided_by` still comes from git. Trace-link decisions keep this entry's YAML field.

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
- *Refined 2026-10-20 (Phase 2 build):* the batch driver enforces the tool limits with permission rules, not only the
  subagent's tool list: `--permission-mode dontAsk --allowedTools "Read(.stangent/**)" "Edit(.stangent/**)"`, so a
  subagent can't read or write outside its working folder (checked: a write elsewhere is refused). `prepare` and
  `check` return the next step (subagent and prompt), so drivers (plugin command, CI, benchmark) hold no logic.

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
- *Capped 2026-11-04 (owner: "yes cap it", after PDF import took about 1.8M subagent tokens over five checks):* by default each build gets
  **one fresh check and one fix round**, on Sonnet with a narrow brief, with a cost estimate first. What's left is recorded as a known gap or brought to
  the owner; no further round starts on the builder's own call. **Cost:** more edge cases get through, as listed gaps rather than fixes.

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
- *Refined 2026-10-20 (Phase 2a build, after a fresh review):*
  - A "contradicted" finding needs a quoted source line of at least 12 characters and two tokens (`return` proves nothing);
    an "unsupported" finding has no line to quote and is kept. Rejecting an unsupported finding needs the supporting line.
  - The verifier gets one retry when its output is missing or breaks the schema, as the writer does.
  - A page fixed after the verifier's findings isn't verified again: code checks each "fixed" statement changed and each
    rejection has a reason. A second verifier pass would double the cost of every page with findings; reported in 2a.
- *2026-11-04:* "Rejected: a second check on every finding" is reversed by D119, off by default until a paid re-score says otherwise.

## D63 · Slices split at the API boundary; every endpoint gets a page — 2026-10-20
Asked while building Phase 2a, because the plan's slice rule ("the walk from the entry point within impact's depth
limit", D54) doesn't hold on Sonarr: at depth 4 a screen stops at the endpoint, so the rules behind an action aren't in
its page; at depth 6, 125 of ~600 bundles pass 200k characters (one is 868k) and every screen repeats the same backend
rules. "Impact's depth limit" isn't a fixed number either.
- **Screens walk the frontend only and stop at the endpoints they call** (linked, like shared components).
- **Every endpoint gets its own backend page**, as do event and command handlers. This changes D54, which gave pages
  only to entry points no screen reaches.
- **A point with 10 or more callers is a leaf:** included, not walked into (logging, `translate()`, base services).
- Frontend depth 6, backend depth 4, bundle cap 260k characters; all settings in `stangent.yaml` (`spec:`).
- 2b gives an FSD section its screen pages plus the endpoints those screens call (code), so the rules are still in reach.
- Still open to change when 1d part 2 (context scoping) reports (D45): every page then goes stale through its input hash.
**Why:** bounded, layered slices (Sonarr: 718 pages, medians 8k–51k characters, largest 254k); one place per backend
rule instead of one per screen; a backend change makes its endpoint page stale, not every screen that calls it.
**Cost:** more pages (all 364 Sonarr endpoints, many near-duplicates across API v3 and v5); a screen page alone doesn't
tell the whole story of an action.

## D64 · The split check is judged on bitwarden's first run; no rerun — 2026-10-20
The plan asked both repos to run to 30 split cases or 200 examined, with every failure counted.
- **What happened:**
  - **bitwarden/server** reached 30 split cases (116 examined), but the tool's hunk cutter had bugs that added
    failures. A fresh agent (D61) sorted the 14 failed splits: 8 were the tool's cuts, now fixed, and 6 were real.
  - **Sonarr** crashed on a .NET runtime segfault after 32 examined cases. That run used the same buggy cutter.
- **Decision:** don't rerun. Judge "safe" from bitwarden's real failures alone. "Useful" is left unmeasured: the
  bug's false splits inflate the first run's 26%. A re-grouping with the fixed cutter was started, then stopped by
  the owner (D65).
  - "Safe" must pass **on each repo**. On bitwarden, at most (16 + 8) / 30 = 80% of the split cases could hold up,
    even if every tool-made failure had passed. That's below the 90% bar.
  - "Better than naive" isn't met as specified, whatever the cutter does: the graph is safer, but split-by-folder
    splits 98% of cases against the graph's 26%, far over the 5-point condition.
  - So no rerun can turn the verdict into a pass. Sonarr's partial run is reported, not judged.
- The cutter is fixed and tested (d770578, b7e90e9) for whoever uses the harness next, for example to test a
  Phase 4 split rule.
**Why:** a rerun is about 22 hours of building and testing to confirm a fail the real failures already show. The
tool's bugs added failures and false splits, so they can't have raised the safe share. A rerun walks the same commits newest first, so
the 6 real failures would very likely fall among its first 30 split cases, and 90% of 30 allows at most 3.
**Cost:**
- The numbers come from a run with known bugs, so they are bounds, not measurements.
- Sonarr has no judged result.
- If the bar had been close to passing, this shortcut wouldn't have been allowed.

## D65 · The graph's blind spots and a smarter split rule are Phase 4 work — 2026-10-20
The split check (Phase 1d) found two reasons the graph can't yet split a change safely on its own:
- **What it doesn't see:**
  - Razor views (`.cshtml`);
  - `.csproj` and `Directory.Build.props` files, and package versions;
  - `using` edits that change what a name means.
- **What it likely over-joins (inferred, not measured):** unchanged hub types (`Organization`, `User`) may link
  unrelated edits within 2 steps. That's suggested by direct-only splitting 3× as often on bitwarden.

Both go into **Phase 4**, not a separate graph phase:
- The graph learns those files.
- The splitter joins only through points that changed or that the change affects. Contract-first splits are
  considered with it (see the open question).
- Each change is proven with the 1d harness (`--split`) before Phase 4 relies on it.
- The merged build and test stay, whatever the graph learns (D18). Some links (a test counting routes) are in no graph.

Rejected: a small "1e" graph phase first. It would help Phases 5 and 6 sooner, but the owner chose to keep the work
with the phase that needs it most.
**Why:** Phase 4 designs the splitter, so it can shape the graph work around what the splitter needs. All of it is
plain code (no AI tokens at runtime).
**Cost:** Phases 5 and 6 get the better "what may break" only once Phase 4 lands; Phase 4 grows.

## D66 · Phase 2a is proven on the pages 2b needs plus a sample; Sonnet writes and verifies — 2026-10-20
*Numbered D64 on the Phase 2 branch; D66–D68 there were D64–D66 until main's D64 and D65 were merged in (2026-10-25).*
The first full Sonarr run used far more than estimated: 193k input tokens per page on average (two headless Claude
Code sessions per page, each re-sending its system prompt and the bundle chunks every turn), about $0.31 per page at
list price, ~$220 for all 718 pages; it hit the owner's usage limit twice.
- **Pages generated for the proof:** every page 2b's candidates need (the FSD's in-scope screens and the endpoints and
  components they link to, ~103), plus a fixed sample of 10 event/command handlers (seed 42). The rest (~550, mostly
  endpoints, half of them v5 copies of v3, and handlers) is left to `spec-run.sh --all`.
- **Every page is still checked by code without AI:** each of the 718 fits under the size cap or is listed as failed.
- **Calls become single-turn first** (the bundle in the prompt, structured output, no tools), measured on 10 pages.
- **Model:** Sonnet 5.5 writes and verifies. The owner's 5-page hand-read decides whether the writer needs Opus.
  Weakening the verifier to save tokens was rejected: it's the guard D62 relies on.
- Changes 2a's done-when box "every Sonarr screen and backend entry point has a page" to the set above; decided by the
  owner before the bar was measured, not to fit a result.
**Why:** the other ~550 pages only show that more of the same works; the questions Phase 2 answers (does the FSD
comparison find planted drift, are pages grounded, what do they cost per kind) are answered by the smaller set.
**Cost:** Sonarr has no complete reference; per-kind cost is measured on smaller samples (10 handlers).
- *Refined 2026-10-20 (single-turn calls, measured):* the batch driver sends each step as one call: input in the prompt,
  `--tools ""`, `--json-schema`, no settings, skills or MCP servers (a bare Claude Code call carried 41k tokens of
  its own context; now 1.2k). On the same 10 pages (6.5k–152k characters, Sonnet 5.5, before and after in
  `benchmark/results/2026-10-20-phase2a-singleturn-measure.json`): **41k input tokens per page against 200k, $0.18
  against $0.31, 111 s for all ten at 3 jobs**; the same verifier findings or one more or fewer per page. The cost left
  is mostly the bundle written to the prompt cache once for the writer and once for the verifier (their schemas
  differ, so they can't share it); cost ≈ $0.035 + $4 per million bundle characters. The plugin's subagents stay for
  interactive use and as `spec-run.sh --agents`.
- *Refined 2026-10-25 (the writer model):* the 5-page hand-read, made by a fresh agent instead of the owner (D67),
  found 2 wrong statements in 88 (one inverted condition, one wrong endpoint link); the verifier caught neither.
  **Sonnet stays the writer;** the owner had asked for the hand checks to be decided for him.

## D67 · Phase 2's hand checks are done by a fresh agent, not the owner — 2026-10-24
D56, D57 and 2a planned two checks by a person: 20 unplanted findings marked real drift or false alarm (the "not too
noisy" bar), and 5 pages hand-read for wrong statements. The owner asked for them to be decided for him (Sonarr is a
demo); decided before either check was run.
- **A separate agent with a fresh context** does both (D61): it gets the criteria, the FSD text, the pages and the
  Sonarr source at the pinned commit, nothing about how the pages or findings were made. It writes one verdict per
  finding and one count per page, each with its reason and evidence (a source line or FSD sentence).
- **Not the author:** the session that built and ran the pipeline doesn't mark its own findings.
- The reports say the checks were made by an AI checker, and the bar is read with that caveat.
Rejected: the author deciding (it would grade its own work, D61); skipping the checks (the noise bar would stay
unmeasured).
**Why:** keeps the noise bar measured by someone other than the author while the owner's time goes elsewhere.
**Cost:** an AI checker can share the judge's blind spots; a person's check is still owed before the findings are
relied on in a real project (the dry run on a real FSD, D56, is checked by people).

## D68 · Discrepancy findings ship off by default (D57's rule, applied) — 2026-10-25
On the planted Sonarr FSD, "finds what's planted" passed (27 of 30, each kind ≥ 60%) but "not too noisy" failed: 31.3%
of findings are false alarms (95% range 14–45%) against a bar of 30%, from a hand check of 20 unplanted findings by a
fresh agent (D67). D57 says what follows: reference pages and links still ship; findings are off by default.
- **`fsd.findings` in `stangent.yaml`, default `false`:** sections record only links ("agree", confirmed or not);
  gaps, conflicts and undocumented items, including disputed agrees turned into findings, aren't written to
  `specs/trace.yaml`, and `fsd undocumented` lists none. A project turns them on with `findings: true`.
- Turning the setting off or on makes each section run again (it is part of the section's input hash only when off,
  so runs recorded with findings on stay current). The Sonarr benchmark turns them on: it measures findings.
- The bar isn't moved: the report states the result and why findings are off.
Rejected: shipping findings on because the miss is narrow (the bar would drift toward the result, D57); dropping
the agree checker's disputes to cut noise before re-measuring (tuning to the frozen key; a change is a new benchmark run).
**Why:** D57, agreed before any run.
**Cost:** a project that wants the drift list must opt in and accept roughly one false alarm in three; the likely
lever (the agree checker turning "partial" agreements into gaps, 25 of the 62 unplanted findings) is a follow-up.

## D69 · Phase 3's bundle is read by a developer's Claude Code first; Phase 4 reads the same contract — 2026-10-25
- A developer runs `/stangent:ticket PROJ-123` in their own Claude Code. The CLI builds the bundle (code, D18) as JSON
  for the agent plus a short Markdown view for the developer; the bundle becomes that session's starting context.
- Phase 4's planner later reads the same `ticket-bundle` contract unchanged.
Rejected: a Phase 4-only input (nothing would show it helps until Phase 4 exists); a BA/PM summary as well (another
audience to word for; BA/PM's view is the Phase 8 dashboard, D12).
**Why:** useful from the day it ships, and a person using it shows what a bundle needs before agents depend on it.
**Cost:** a developer's session isn't a measurement; the proof needs its own scored benchmark.

## D70 · A ticket's starting points: code first, then AI from page titles, then the developer — 2026-10-25
Answers the open question "how does the ticket's description become a starting point for the graph".
1. **Code finds exact signals** in the ticket text: a stack-trace frame → its method (✅); a URL path
   (`/series/12/edit`) → the route's page; a quoted screen name, page title or FSD section title → those pages (FSD
   sections through `specs/trace.yaml`); a symbol name that exists in the graph.
2. **If code finds none, AI picks** (*refined by D87: from the ranked page list*) from the ticket and the **list of page titles only** (not the pages): up to 5
   pages, each with a word-for-word quote from the ticket saying why. Code checks each page exists and each quote
   matches; a failed check gets one retry (D60). Same shape as 2b's candidate finder.
3. **The developer sees the picks** (with their reason: the signal or the quote) in the Markdown view and can add or
   remove before work starts; the bundle records who changed what.
- The impact answer starts from the chosen pages' cited symbols and any matched symbols.
Rejected: the developer naming them by hand (the step a junior or a Phase 4 agent can't do, and no unattended runs); AI
reading the whole reference (hundreds of pages per ticket, picks hard to check); embedding search (no word-for-word
reason, close to the inferred links D10 rejects).
**Why:** code takes the certain cases at no cost; the AI's step is small (~10k tokens) and checkable; a person corrects the rest.
**Cost:** tickets that name nothing the code can match depend on page titles being good; a page list with titles only
can't tell two similar screens apart.

## D71 · The bundle carries pages and pointers, not source code, under a size cap — 2026-10-25
Built by code (D18). In order:
- **The ticket:** key, type, summary, description, comments, links, and its attachments (D72).
- **Starting points** with their reasons (D70).
- **The chosen reference pages** (statements with citations) and their **linked FSD sections' text** with each link's
  state and whether a person decided it (D53). Findings only when `fsd.findings` is on (D68), marked unconfirmed.
- **The impact answer** from the starting points within 2 lines, summary first (D27).
- **Pointers:** the files and symbol ids behind the pages and the impact answer. No source code: the developer's
  Claude Code opens the files it needs.
- **Cap ~40k characters (~10k tokens)**, a setting. Over the cap, the lowest-ranked pages become pointers, and the
  bundle says so.
- **Missing or stale pages are flagged, never generated by the bundle** (a page costs $0.05–0.38, 2a); the developer
  can run `/stangent:spec-generate` for them.
Rejected: the source slices too (a screen's slice is ~53k characters median, up to 254k, paid on every turn of the
session); pointers only (the agent re-reads code to learn what the pages already say).
**Why:** the pages are Phase 2's checked summary of the code, so they're the cheapest grounded context there is.
**Cost:** a page describes only its slice (D63), so an action's backend rules sit on the endpoint page, which may be
a pointer; on a repo with few generated pages the bundle is mostly pointers.
- *Fixed 2026-11-02 (owner's OK):* a cut page's statements went, but so did the files it cites: the pointers listed only
  the included pages' citations. On Sonarr that lost the fix file in 5 bugs whose starting files held it (SONB-6, 14,
  27, 38, 39; 28 of 40 bundles cut a page). Now a cut page keeps its cited files and symbols among the pointers, as
  "become pointers" meant. No contract change (`ticket-bundle` v1: pointers already carry `from`, the page id).
  Result (re-made bundles, `2026-11-02-phase3-sonarr-score-bundle-fix.json`): the bundle shows a fix file in 19 bugs
  (was 14). **Cost:** pointers no longer shrink as pages are cut (median 7k → 14k characters, 27 → 54 pointers for a
  median 30 files), so the cap is missed in 19 of 50 bundles (was 5) and every page is cut in 25 (was 18): in those, page
  statements are dropped and the cap still fails, while the impact answer (median 11k) is never cut. Open for the
  owner: what gives way under the cap (see open questions).

## D72 · Attachments: code converts text, keeps images as files, lists the rest — 2026-10-25
- Code downloads every attachment (and images inline in the description) into the bundle's folder, each with its
  content hash.
- **Text** (`.txt`, `.log`, `.json`, `.csv`, `.xml`, and similar) goes into the bundle up to a per-file cap (the rest is
  a pointer to the saved file); stack traces in them feed the starting points (D70).
- **`.docx`** goes through 2b's converter (same file → same text).
- **Images** are saved and listed by name, size and where they appear. The developer's Claude Code can look at them;
  **no stangent AI step describes them**, since nothing could check the description (D18).
- **PDF, Excel, video and anything else:** listed by name, type and size. Each gets a converter when a real ticket
  needs one, as 2b did for FSD formats. *D111 (2026-11-03) adds PDF and Excel import; whether attachments use it is still to grill.*
  *Changed, 2026-11-04 (D121): a `.pdf` goes through the same import steps as a `.docx`, so its text reaches the ticket. It never stops the pull: one that can't be read is listed with why. A saved ticket with a PDF attachment gains text the next time it's pulled, so its bundle changes then. Excel and the rest stay listed.*
Rejected: an AI describing each image into text (a call per image, unverifiable); text only (many bug tickets are a
screenshot and one line).
**Why:** everything deterministic is converted by code; what isn't stays in its original form for whoever reads it.
**Cost:** an agent without vision (or a headless run) gets nothing from a screenshot but its name.

## D73 · The stangent CLI reaches Jira as an MCP client, behind the issue-tracker adapter — 2026-10-25
- The C# CLI talks to Atlassian's MCP server with the official C# MCP SDK, calling its tools from code (no AI in
  the fetch path). Locally it signs in with its own browser login (OAuth 2.1) and keeps the refresh token in the OS
  keychain; Claude Code's own MCP login can't be shared.
- **Unattended runs** (CI, later phases) use the MCP server's API-token sign-in (email + token, Basic auth), which an
  organization admin has to enable. Checked 2026-10-25 in Atlassian's docs; on the owner's personal sites the owner
  is the admin.
- **Behind the issue-tracker adapter (D24):** Jira through MCP now; a **file** backend reads tickets from JSON files,
  for tests and the benchmark. Code reads only fields it names from the tool results and keeps the raw result's hash,
  so a change in Atlassian's output shows up as a failing adapter test, not as a quietly different bundle.
- Attachments: the `downloadJiraIssueAttachment` operation gives a short-lived URL that code downloads (D72).
- **First step of the build is a spike:** sign in, read a ticket with comments and an attachment, and post a comment,
  from C#, locally and with an API token. If any of that fails, the question comes back to the owner before more is built.
- Answers D2's open question.
- *Refined by D82 (spike, 2026-10-25):* the endpoint is `/v2/mcp`; v1 has no attachment download.
Rejected (the owner's call, against the recommendation): Jira's versioned REST API with an API token (a fixed,
versioned contract, the same path locally and in CI); the plugin fetching through the developer's MCP login (an AI
session in the fetch path, no unattended runs).
**Why:** one integration Atlassian maintains for AI tools, with OAuth sign-in locally instead of a token for every developer.
**Cost:** the MCP server's tools are shaped for AI readers and aren't versioned like REST, so outputs can change
under stangent (Atlassian already retired its `/v1/sse` endpoint on 30 June 2026); unattended use depends on a
company admin enabling API tokens; the C# MCP SDK and an OAuth flow are new dependencies.

## D74 · Write-back: AI drafts comments as files; a person reads and posts them — 2026-10-25
Two kinds of comment, both drafted by an AI step (D60) into a file, never posted by it:
1. **Questions at pull time,** when the ticket is unclear or clashes with the FSD or a page: "the ticket says at most 5
   tags; FSD 4.2 says 3, which is right?". Each question quotes the ticket sentence and the FSD or page sentence word
   for word, and code checks both (as D53).
2. **A resolution summary after the work,** from the developer's merged PR: code supplies the diff's impact answer,
   the pages and FSD sections it reaches; the AI words it.
- **Tone per draft:** `internal` (technical: screens, FSD sections, PR) or `client` (plain words, no code names). A
  project sets the default; the developer can switch it.
- **Code checks every claim** links to a real page, FSD section, file, PR or ticket sentence (as D18's PR summary row);
  a client draft is also checked for code names and file paths.
- **A person posts it:** the developer reads the draft and runs `stangent ticket post`, which posts it under their
  own Jira login. A fresh-agent check (D61) isn't added: the person reading it is the check.
- **Nothing else is written in Phase 3:** no status transitions, no field edits.
Rejected: internal comments posting automatically (a wrong comment is seen at once and can't be unsent); moving the
ticket's status (workflows differ per project and a transition can set off the company's automations).
**Why:** D18 says a person approves what goes to a client; the same review costs little for internal comments, and
everything the comment claims is checked by code first.
**Cost:** every comment waits for the developer; a summary for a ticket closed outside stangent isn't drafted.

## D75 · A bug is proven before it's fixed, in Phase 3 — 2026-10-25
Answers the open question "how is a bug verified before it's fixed". The owner chose proof in Phase 3 over recording
the rule for Phases 4–5 (first recommended), because a wrongly reported bug should be caught before any fix is attempted.
- **Spotted by code:** a bug ticket is one whose Jira issue type is a bug (the mapping is a setting). Its steps,
  expected and actual result, version and environment go into the bundle; missing ones become a draft question (D74).
- **Proof:** an AI step (D60) writes a reproduction test from the bundle. Code checks it compiles, **fails on an
  assertion** (not a compile error, an exception in setup or a timeout), and the assertion checks the ticket's
  expected result. The developer confirms the failure matches the reported actual result.
- **Reproduced:** the test is kept with the ticket; the fix must make it pass, and nothing else may start failing (D18's
  test row: fails without the fix, passes with it).
- **Not reproduced** (the test passes): no fix starts. A draft comment says what was tried and asks for more (D74).
  The developer reviews the test first, since a badly written test also passes.
- How far the proof goes (which kinds of test) is D76.
- **"Don't fix yet" is a label and a suggested next step in Phase 3,** not a lock: the developer can still fix. From
  Phase 4, an agent doesn't start a fix on a not-reproduced ticket until a person says go.
  *Changed by D106 (2026-11-02): Phase 3 now says "no proof yet" and why, not "don't fix yet". The Phase 4 rule stands.*
- **The developer can suggest another way to reproduce it** (`stangent ticket prove <KEY> --hint "set the server time
  zone to UTC+10"`): a new attempt with the hint added to its input, checked the same way. Each attempt costs about
  what the first did (~$0.50–2) and is recorded with its hint. *(Added 2026-10-25, the owner's idea.)*
**Why:** a fix for a bug that isn't there costs a developer's or an agent's time and can break correct behaviour.
**Cost:** Phase 3 takes on test writing and running, first planned for Phase 5; an AI-written test can be wrong in
either direction, so a person confirms both outcomes.

## D76 · Bug proof uses unit tests and Playwright — 2026-10-25
The owner chose unit tests plus Playwright over unit tests only (recommended), so UI bugs can be proven too.
- **Unit tests:** the reproduction test goes into the repo's existing unit-test project for the code it exercises
  (xUnit/NUnit; Jest/Vitest for the frontend). No databases or services, as in 1d (D46).
- **Playwright,** for bugs a unit test can't reach (a screen showing the wrong thing): the test drives the running app.
  How to start the app and give it data is a **per-repo setting** (`tickets.app`: build and start commands, URL, a
  seed step); stangent runs it, it doesn't invent environments. A repo without that setting gets unit proof only, and
  a UI bug there is marked "not provable here" for the developer to reproduce by hand.
- **Flakiness:** a Playwright reproduction counts only if it fails the same way on 3 runs in a row; a test that passes
  once is "not reproduced, flaky", not proof.
- Integration tests with databases or containers stay in Phase 5.
**Why:** many bug tickets are about what a screen shows; unit tests alone would leave them all to a person.
**Cost:** the app's start-and-seed setting is real work per repo (Sonarr has no Playwright tests, and its screens need
series and episodes in its database); browser runs are slower and flakier; Phase 5's browser-test work partly moves here.

## D77 · Phase 3 is proven on Sonarr issues copied into the owner's Jira, read through the MCP — 2026-10-25
- **Tickets:** real Sonarr GitHub issues, frozen once. Sonarr has 289 main-line fixes since 2022 that close an issue
  (`Closes #9126`), so each comes with its answer: the fix commit.
- **Copied into a new project on the owner's Jira site** (`peachpersonal.atlassian.net`, connected through the
  Atlassian MCP), attachments included, each ticket noting its original issue number. Creating them is a one-off,
  asked for before it's done.
- **Read through the MCP** by stangent, as in real use (D73). The first read saves each raw result; scoring replays the
  saved results, so a re-score is repeatable when the network or Atlassian's output changes. A live re-read is its own check.
- **The answer key is git history,** never put in a bundle: the fix's changed files, and whether a reproduction test
  fails on the fix's parent and passes on the fix (D75).
- Sonarr already has the graph, 113 reference pages and the FSD links (Phase 2). Closes D6's "no Jira" gap.
Rejected: planted tickets (we'd grade tickets we wrote; real reporters write messier ones); the owner's own project
first (no answer key; kept as a dry run afterwards, as D56).
**Why:** real tickets with known fixes give a score by code; going through the MCP proves the path real use takes.
**Cost:** a one-off copy into Jira; issues from before the pinned commit are scored against today's graph and pages
(D28's approach, median agreement 1.0 for the graph).

## D78 · The frozen set: 40 bugs and 10 not-bugs — 2026-10-25
- **40 bug issues,** newest first before Sonarr's pinned commit, each closed by one main-line fix commit of at most 15
  files (Test 2's cap); labelled by what the fix touched (backend → unit proof, frontend → Playwright, both).
- **10 not-bugs:** issues closed as not a bug, by design or can't reproduce, with no fix. Their reproduction should
  *not* reproduce: they test the reason bugs are proven first (D75).
- Features are left out: they have no proof step, and the bundle is scored on the bugs.
- The selection rule is code; the list is frozen and committed before the first run.
- *Changed by D81:* the reproduction runs on the 30 newest bugs and the 10 not-bugs; bundles on all 50.
Rejected: adding 10 feature issues (fewer bugs for the proof bars); bugs only (nothing tests a wrong report).
**Why:** enough bugs for the bars, and the not-bugs measure what the proof is for.
**Cost:** change requests, most of a company's tickets, aren't scored in Phase 3; 40 cases give wide ranges (as D47).

## D79 · Phase 3's done-when: right place, real proof, grounded, repeatable — 2026-10-25
Agreed before any run (as D44, D57), on the frozen set (D78). 95% ranges from 1,000 resamples, fixed seed, beside each share.
| Check | Bar |
|---|---|
| **Bundle: right place** | ≥ 70% of the 40 bugs have at least one file the fix changed among the bundle's starting points' files |
| **Bundle: beats a keyword search** | that hit rate beats a code-only keyword search (ticket words vs file paths and symbol names, top 5 files), with the 95% range of the gain above zero |
| **Grounded** | every starting point, quote and draft claim passes the code checks; tests show a made-up quote and a non-existent page are rejected |
| **Repeatable** | replaying the saved MCP results gives byte-identical bundles; a re-run with nothing changed makes zero AI calls |
| **Proof is real** | of the bugs marked reproduced, ≥ 90% pass on the fix commit |
| **Proof finds bugs** | ≥ 50% of the 40 bugs reproduced (unit and Playwright reported apart). *Changed by D81: of the 30 bugs the proof runs on* |
| **Proof catches wrong reports** | at most 1 of the 10 not-bugs reproduced |
| **Jira round trip** | live on the owner's site: read a ticket with comments and an attachment, post a comment |
| Reported, no bar | how much of each fix the whole bundle covers; bundle size; cost and time per ticket and step |
- **If "proof is real" or "catches wrong reports" fails:** the proof ships as **advice only**: the developer sees the
  test, but it can't block or clear a fix.
- **If "right place" or "beats a keyword search" fails:** the AI picks (D70 step 2) ship **off by default**; code
  signals and the developer remain.
- **If "proof finds bugs" fails** *(added 2026-10-25, before any run)*: "not reproduced" stays as information (the
  attempt and its test are shown) but no longer means "don't fix yet" and no longer drafts a question to the reporter,
  since with under half of real bugs reproduced it would usually be the tool missing. "Reproduced" still counts as proof.
- The bar isn't moved after a run; the report states the result and what follows (D57).
- **Expected cost of the run:** about $30–100, mostly the reproduction agent (~$0.50–2 per ticket); bundles and drafts
  about $5. The run stops and asks the owner past $100. *Changed by D81: reproduction on 30 bugs, $1 cap per attempt,
  ≈ $55–65 worst case including missing pages.*
Rejected: stricter or looser bars.
- *Clarified 2026-10-25, before any run (after a fresh check of the plan, D61):*
  - **The run uses product defaults:** AI picks on, `fsd.findings` off, the cap; no developer edits to the starting
    points. "Starting points' files" are the files of the symbols the starting pages' statements cite plus the files of
    matched symbols.
  - **Missing pages are generated first:** starting points are picked for all 50 (from page titles, which exist for
    every page the graph calls for), then the picked pages with no written version are generated (~50–100 pages, about
    $10–20 on top, reported apart), then bundles are built. A project in real use would have its reference generated.
  - **No developer in the run:** a test that passes the code checks counts as reproduced; "proof is real" measures what
    the developer's confirmation would catch. The 10 not-bugs have no fix commit: they run on the commit before the issue
    was closed and are scored only on "catches wrong reports".
  - **Drafts in the run:** a questions draft for all 50 and a resolution draft for each bug from its fix commit, so
    "grounded" covers drafts.
  - "Grounded", "repeatable" and "Jira round trip" are must-pass: a failure is a bug to fix and re-run, not a result.
**Why:** a pre-agreed bar can't drift toward the result.
**Cost:** with 40 bugs, one case moves a share by 2.5 points; a gain over the keyword search may be real but not clear zero.

## D80 · "code-wrong" decisions don't become tickets in Phase 3 — 2026-10-25
Changes D58's "code-wrong becomes a draft Jira ticket in Phase 3". Phase 2 already points them out in
`specs/trace.yaml`; a person who decides "code-wrong" creates the ticket by hand. Later, an AI step can draft a ticket
from a discrepancy on request; it's left out now.
**Why:** these are discrepancies between the FSD and the reference pages, not confirmed bugs in the code, and Phase 2
already lists them; few projects will have them while findings are off (D68).
**Cost:** the step from a decision to a ticket stays manual.

## D81 · The run's cost: reproduction on 30 bugs, each attempt capped — 2026-10-25
Changes D78 and D79, before any run. The owner asked whether the run needs to cost $30–100; almost all of it is the
reproduction agent.
- **Bundles and drafts** still run on all 40 bugs and 10 not-bugs (~$5): the bundle bars keep 40 cases.
- **Reproduction runs on 30 bugs** (the 30 newest of the 40, D78's order; D47's floor) **and the 10 not-bugs.** "Proof
  finds bugs" becomes ≥ 50% **of those 30**; "proof is real" is judged on the reproductions among them.
- **Each attempt is capped** at $1 and 15 turns (*raised to 30 turns on 2026-10-28, $1 kept: see D86*); an attempt
  that hits the cap is "not reproduced". Real use needs a
  per-ticket cost limit too (`tickets.proveBudget`), so the run tests the product as it ships.
- **Worst case:** ~$40 for reproduction + ~$5 bundles and drafts + ~$10–20 for missing pages ≈ $55–65.
Rejected: 20 bugs and 10 not-bugs throughout (saves ~$20, but one case moves a share by 5 points and "beats a keyword
search" could hardly clear zero); running 5 tickets first to measure cost (the cap already bounds it).
**Why:** cut where the money is, and keep the bundle bars on 40 cases.
**Cost:** the proof's ranges are wide (about ±18 points around 50%); a result near a bar is reported with its range,
judged on the plain share as before; 10 not-bugs catch only gross failure.

## D82 · Jira through Atlassian's MCP v2 (`/v2/mcp`), with Jira-only scopes and secrets in the OS keychain — 2026-10-25
Refines D73, from the spike (Phase 3 step 0). The owner was told before the build went on.
- **Endpoint `https://mcp.atlassian.com/v2/mcp`,** not `/v1/mcp` as D73 assumed. v1 serves an older set of 32 tools with
  no attachment download and no `discover`/`execute*`; v2 has a few primary tools and the rest (~320 operations) behind
  `discover`, run by name through `executeRead` / `executeWrite`. stangent calls operations by fixed name, so it never
  needs `discover` at run time.
- **The calls Phase 3 uses:** `getJiraIssue` with `view: "evidence"` (the default view drops the issue type and
  comments), `executeRead` → `listJiraIssueComments` (paged) and `downloadJiraIssueAttachment` (a short-lived URL code
  fetches; only https on an Atlassian host), `addOrEditJiraIssueComment` to post. Every one passed live, with OAuth and
  with an API token, on SCRUM-7.
- **OAuth on v2** goes through `auth.atlassian.com` with dynamic client registration, a fixed loopback redirect
  (`http://127.0.0.1:47813/callback`) and its own consent. The server offers every product's scopes (delete, Bitbucket,
  Loom…); stangent asks only for `read:jira:agent-interface`, `write:jira:agent-interface`, `search:jira:agent-interface`,
  `read:me`, `read:account`, `email`, `offline_access`. API tokens get the same three Jira scopes.
- **Secrets** (the token container with the refresh token and registered client; API tokens) live in the OS keychain
  through the OS's own API (macOS Security framework, Windows Credential Manager; Linux `secret-tool` on stdin), never on a
  command line, in a file or in an error message. Keychain account per endpoint (`oauth:mcp.atlassian.com/v2/mcp`).
**Why:** v1 can't download attachments (D72), and Atlassian is moving tools to v2 (its changelog, 2026-10); asking for
fewer scopes limits what a leaked token can do.
**Cost:** v2's operations are reached by name through `execute*`, so a renamed operation fails at run time, not at
`tools/list`; the adapter tests on saved results (D73) are what catch it. A broad consent given before the scope filter
existed stays until it is revoked.
- *Incident during the spike:* the first keychain write went through `security -i`, which rejected the long v2 token and
  echoed it in its error, printing the tokens into the session. The owner revoked the grant; the code now uses the
  Security framework and its errors carry only a status code.

## D83 · A ticket's parts can be marked "outside the code" (server, network, deployment, data), each tagged by who said so — 2026-10-26
From the Sonarr run: a ticket about the server, the network or the deployment (a full disk, a load balancer timing out)
got app pages picked for it, a bundle of app code, and a $1 proof attempt pushed to write a unit test that can't reach
the problem. Tickets often mix both: a screen bug that only shows behind a proxy.
- **Per part, not per ticket.** Alongside the starting points in the code, a ticket can have **outside-the-code parts**,
  each with an area (server, network, deployment, data, third party, other) and the ticket's own words as evidence.
- **Tagged by who said so,** as starting points are (D70): `ai` (the point picker may return them; its quote is checked
  word for word, D18), `developer` (added or removed by hand). Code doesn't guess these.
- **The bundle shows both** in separate sections, so the developer sees what's in the code and what isn't, and who
  said each.
- **Bug proof** runs for the code parts as before. When the AI answers "not provable" because of an outside-the-code
  part, that's accepted without the push-back retry.
- **The questions draft** asks for the environment details an outside-the-code part needs.
**Why:** the owner's tickets often point at infra, sometimes alongside code; a bundle of app code for an infra problem
misleads, and the proof attempt wastes money.
**Cost:** `ticket-bundle` and the point picker's output change while Phase 3 is still in progress (v1 is not yet
published); an extra kind of AI claim to check; infra that lives in the repo (Dockerfiles, workflows, Terraform) isn't in
the code map yet, so it shows only as an outside-the-code part (mapping it is Phase 7's).
- *Settled 2026-10-31 (the open question):* **proof is skipped only when every part is outside the code and nothing
  points into it** (no starting point by code, AI or developer). It's recorded `not-provable`, marked `outside`, with
  no AI call. "Outside" means the cause lives where the repo doesn't: the server, network, deployment, live data, a
  third party. UI, API and app-level security bugs (XSS, injection, a missing auth check) are in the code and always get
  an attempt. A developer removes a wrong tag (`--remove-outside`) and the next run tries the proof. The questions draft
  asks *where* only when there is neither a starting point nor an outside part.
  **Why:** a wrong "outside" tag would quietly skip proof on a provable bug, the worst outcome; requiring no code point
  at all keeps that rare, and a developer can undo it. **Cost:** a mixed ticket still pays for an attempt.

## D84 · Drafts: framing lines need no claim, writers never talk about their input, and the run drafts after proof — 2026-10-26
Refines D74. On the Sonarr run 21 of 50 questions drafts and 12 of 40 resolutions failed the check, many on lines that
state nothing (*"Hi, a question before we start:"*, a bold heading), some on real problems (an unmarked cause, *"The
input I have holds no proof"*).
- **Exempt from the claim rule:** questions, a greeting of four words or fewer (as before), and now a heading (`# …` or
  a whole line in bold) or a lead-in line ending with a colon: one phrase of eight words or fewer, with no full stop. A bold label followed by a
  sentence (*"**Cause:** the scheduler runs twice"*) is still a claim.
- **The writers are told:** never mention "the input", their instructions or what they were given; link
  `missing` only to a field listed as missing; with no bug proof, say nothing about how the fix was checked.
- **The benchmark runs drafts after proof** (points → pages → bundles → prove → drafts → replay), as in real use, where
  the fix comes after the investigation. D79 didn't fix this order.
**Why:** the checker should stop made-up claims, not greetings; a comment that talks about "the input" can't be posted.
**Cost:** a short one-phrase lead-in ending with a colon can still state a small claim unmarked (*"The cause is the
scheduler:"*), and so can a short heading; the eight-word, no-full-stop limit keeps that to a phrase. A check rejects a
draft that talks about its own input (*"the input I have"*, *"these instructions"*), not about an input field. The change
is tuned on the same 50 tickets it will be scored on, so the drafts' result flatters a little; the
report says so (D57's rule: the bar doesn't move, the result is stated with what changed).

## D85 · A Jira read that gets no answer is tried again, the last time on a new session — 2026-10-26
Refines D82. On the Sonarr run about 1 in 40 `getJiraIssue` calls was taken by Atlassian's MCP server and never
answered (seen live: the connection open, no reply; the same ticket answered in a second on the next try).
- **Reads** (a fixed list: `getJiraIssue`, `executeRead`, `atlassianUserInfo`, …) get 30 s per try and 3 tries; the
  third opens a new session in case the session itself is stuck. **Writes** get one try of 7 minutes: a retry could post
  twice.
- The time limits wait while the browser sign-in page is open inside a call (the token exchange after it isn't covered;
  a sign-in inside a read is rare, as the saved sign-in normally renews without the browser).
**Why:** a pull hung for an hour and stopped a batch run.
**Cost:** a read that is really slow (over 30 s) fails after three tries instead of finishing; the new-session path has
no test yet (it needs a stalling MCP server) and hasn't run live.

## D86 · Browser proof: the app runs while the AI works; it copies a related Playwright test, or looks with a browser when there is none — 2026-10-26
Refines D76. On the Sonarr run the reproducer wrote Playwright tests blind: it never saw the page or ran its test, so it
guessed the clicks (SONB-5 clicked a profile name that opens nothing and waited 30 s for a dialog).
- **The app runs during the attempt.** Before the AI starts, stangent builds and starts the app (`tickets.app`) and
  stops it after; stangent's own check still starts it fresh and runs the test 3 times (D76).
- **Related Playwright tests first:** code finds the repo's existing specs that open one of the bundle's screens (their
  `goto` path matches the screen's route) and runs them once against the app before the AI starts (free; the result is
  in the AI's input). The AI copies the closest one, so the way to the screen is already right.
- **No related test → a browser:** the AI gets a headless browser (Playwright MCP, only `http://127.0.0.1:<port>`) to
  look at the screens before writing.
- **Test first (the owner's TDD rule):** the AI runs its own test against the app and hands it in only once it fails on
  the named assertion.
- **The test is kept** for the developer to commit with the fix, so the next bug on that screen has a related test.
- **Sonarr has no Playwright tests,** so two are written by hand for the benchmark, on screens chosen without looking at
  the 50 tickets (the series list and the settings pages); the report says so.
**Why:** a test that never saw the page fails on the wrong thing; existing tests already know the way to the screen, and
the owner prefers test-first.
- **Built with these guards** (after a fresh review, D61): the app never outlives `ticket serve` (stopped on a cancel
  while it starts, a 90-minute cap, the driver's trap); a cancelled command's process tree is killed; when the app
  doesn't come up, the AI isn't run (a setup failure, not the AI's); the reproducer runs with no user or project Claude
  settings (their allow rules, such as `git push`, would widen its own) and may write only its result file, not the
  attempt's state; its run script takes only a plain path in its folder or a related spec; proofs run one at a time
  (the app's port); Sonarr's start script does its first, unseeded start on another port.
- *Decided 2026-10-28, after the pilot:* **30 turns per attempt, $1 kept.** SONB-5 (browser) had looked at the page,
  written its spec and run it by turn 12, then hit 16 of 15 turns at $0.23 of its $1; SONB-13 (unit) reproduced well
  inside the cap ($0.13). The dollar cap is what bounds the run's cost (D81's worst case is unchanged); the turn cap was
  cutting off attempts that were on track.
**Cost:** every browser click or test run is a turn, so the 15-turn cap (D81) binds sooner (raised to 30, above);
the app is built twice per attempt (for the AI and for the check); the seeded tests make Sonarr look a
little better tested than it is. **Not a sandbox:** a spec the AI writes is code Node runs with the user's rights, as
the unit tests it writes always were; the permission rules limit its tool calls, not that code. The browser server
(`@playwright/mcp@0.0.83`) is fetched by version at run time and uses the repo's own Chromium; `--allowed-origins` isn't
a security boundary, the tool subset is. Related specs are found by a literal `goto` path only (no template strings,
no clicks through), and a kept reproduction stays in the reproducer's folder, so it's related to the next bug only once
the developer moves it beside the other specs.

## D87 · Starting points through a page index built by code; ranking orders pages, it never hides one — 2026-10-27
Refines D70. On the Sonarr run code found starting points for 17 of 50 tickets; for the other 32 the point picker got
the whole page list (718 pages, ~63k tokens written, ~$0.25 a pick) because tickets are written in plain words, not
URLs, stack traces or quoted names. The picks were right; the missing piece was a way from the ticket's words to pages.
- **A page index, built by code and kept current with the graph and pages:** each page's title, route or endpoint
  split into words, code names split into words, its written statements (Phase 2), FSD sections, and the text the code
  holds: messages in the code (exceptions, validation, logs), `.resx` resources, markup text (Razor, Blazor, HTML,
  JSX/TSX), config keys. General to C# projects; a project's own text helpers (Sonarr's `translate()` + `en.json`,
  react-i18next's `t()`) come in through a setting, as `detectorSettings` does today.
- **Ranking is code:** the ticket's words against the index, exact multi-word phrases highest, rare words over common.
- *Dropped 2026-10-28, see the result below:* ~~**A clear winner is picked by code** (no AI), with the matching phrase as
  evidence checked word for word, tagged `code`; the next code-ranked pages are listed in the bundle as "also possibly".~~
- **Otherwise the AI sees every page:** the top 20 in detail plus all pages as one compact line each (number, kind,
  title: ~7k tokens on Sonarr instead of ~28k+), so a word mismatch can't hide the right page; cached across a batch.
- **Low confidence is said:** the bundle says "no confident starting point" and the questions draft asks the reporter
  which screen they were on, rather than a silent guess.
- **Measured before it's relied on,** at no AI cost, on past bugs of Sonarr and bitwarden/server (backend only, no UI
  text): a right page among the picks, "also possibly" or the top 20 for ≥ 95% of bugs, agreed before measuring; every
  miss is looked at.
  - *Agreed 2026-10-27:* **a right page** is one whose walk from its entry point, at the page's own depth plus 2 lines,
    reaches a file the fix changed: a developer starting on the Import Lists page reaches the Simkl parser two calls
    past what the page describes. The 95% counts only bugs that have a right page; the others are reported apart as
    "no page covers this code", a gap in Phase 2's pages rather than the index, each one looked at (Sonarr: 6 of 40).
    With a clear winner only the winner and its "also possibly" pages count; otherwise the top 20 the picker sees in
    detail. A right page the AI could still find below 20 in the compact list isn't credited. Rejected: the page's own
    slice only (9 of 40 Sonarr bugs unscored), a fixed depth 8 (a random 20 pages would hit 41%), every bug counted (a
    coverage gap would fail the index).
  - *Agreed 2026-10-27:* **bitwarden/server is a 5-bug check**, not a share: the 5 newest closed GitHub bug reports whose
    merged server PR closes the issue or says it fixes it and changes non-test C# (31 such bugs exist; bitwarden tracks
    most bugs in a private Jira). The ticket is the issue's title, description and comments posted before the fix; fix
    files are traced through renames to the pinned commit. It passes when every scored bug hits; each miss is looked at.
    Sonarr's 40 are where the ranking's settings were tuned; bitwarden is measured once, after they're frozen.
    Rejected: all 31 or 41 (the owner: no need for that much).
  - *Result and decision, 2026-10-28:* **the bar failed by one bug.** Sonarr 32 of 34 (94%, bar 95%); bitwarden 3 of 3
    (passed). Not softened. The clear winner measured worst (5 on Sonarr, 2 right; 2 fell on bugs no page covers and
    would have gone out with no AI and no warning), so **the clear-winner shortcut is dropped:** whenever code finds
    no starting point, the AI picker sees the top 20 in detail plus every page as one numbered line, and picks.
    The ranking only orders pages; a low-ranked right page can still be picked from its line. The top 20 alone held a
    right page for the same 32 of 34 and 3 of 3, so dropping the shortcut loses no hit; it costs an AI pick on the few
    tickets that had a winner. Pages are given by their number in the list. Measured on 3 Sonarr tickets: ~11.5k tokens
    a pick (top 20 ~2k, the full list ~8.5k), not the ~7k estimated above, against ~28k+ before. "No confident starting point" is said when the picker picks nothing: the bundle says so
    and the questions draft must ask the reporter where it happens (`where` is listed as missing).
**Why:** the owner: missing the right starting place is the worst outcome; and the whole-list pick was costly and didn't
scale to one-off tickets.
**Cost:** reading UI text is per stack (Razor, Blazor, Angular each need a reader); words that differ between ticket and
code ("size editor" vs "Quality Profile") rank low and rely on the AI seeing the full list; meaning-based search
(embeddings) is the next step only if the measure says word matching isn't enough.

## D88 · Impact answers say what they left out; no forward walk — 2026-10-28
Prompted by an outside checklist (look both ways, check the diff separately, test entry points, report blind spots).
Most of it is already covered: `graph impact --diff` (D27), the `tests`, `endpoints` and `pages` groups, ❓ listed one
by one, the merged build and test (D65), bug proof (D75), ranking by code (D87). Two gaps remain where an answer looks
complete but isn't:
- **A depth limit cuts silently.** `--max-depth N` drops everything past N, and the ❓ past N go with it
  (`ImpactAnalyzer.Analyze`). It will report, per group, how many items and how many ❓ it left out.
- **Files the graph can't read are skipped, not flagged.** A change touching them will list each as ❓: D65's list
  (Razor views, `.csproj` / `Directory.Build.props`, `using` edits) and what Phase 3's fix coverage missed (stylesheets,
  migrations, `package.json` and lock files). A file the graph later learns (e.g. stylesheets) leaves the list.

**No forward walk (what the changed code uses).** Phase 3 checked it before building: one step forward from the page
reached no fix file, and the one "used, not dependent" miss (`SimklAPI.cs`) sits behind the impact layer, where a
forward step would add to ~1,150 files. When an agent's diff is checked (Phase 4), the compiler and tests already cover
the used side (D18). A point's outgoing lines stay available through `graph show`. Revisit only if a Phase 4 miss is
traced to a file the change uses.

**Ranking:** D87 covers bundles. Impact answers keep their order (distance, then label; 📜 by share); no ranking by
meaning inside them (D18). Phase 3's hint that ordering may matter as much as new links is unmeasured until a run with
written pages.

Built with D65 in Phase 4, as `impact-report` v6 (consumers marked per D20).
Rejected: a "calls" group in impact answers (drafted on main the same day, before Phase 3's check was seen); choosing
the walk depth per ticket automatically (depth stays the caller's `--max-depth`).
**Why:** D27 promised every ❓ is listed; a silent cut or an unread file breaks that promise. A forward walk had evidence
against it and a list already too long.
**Cost:** longer answers (mitigated by summary-first, D27); another contract bump for Phases 5 and 6; the ❓ list for
unread files must be kept in step with what the graph learns.

## D89 · Our own setup never counts against the AI: rules that don't depend on the shell's folder, setup failures recorded apart, fail fast, and unit targets for every starting point — 2026-10-31
The first full Sonarr run (D79) was stopped at SONB-22. Of 19 proofs, 8 "hit the 30-turn cap" and 2 left no output, but
reading the transcripts showed stangent, not the reproducer, caused most of it:
- **Permission rules read from the shell's folder.** The rules were repo-relative (`Edit(e2e/stangent/**)`, the result
  file). Claude Code reads such a rule from the agent's current folder, so after a `cd src` every allowed write was
  refused. The first call ended with no result; the retry got the leftover 2–15 turns and hit the cap. SONB-1, 4, 5,
  15, 16, 19 (and part of 17). Reproduced in a throwaway folder; both fixes below confirmed there.
- **Backend points picked as pages offered no unit test.** AI picks (D87) and endpoint points are stored as pages, even
  a C# handler (`ImportListSyncService.Execute`); test targets came only from symbol points, so only Playwright was
  offered and the reproducer rightly said a browser can't reach server code (SONB-11, 14).
- **A start command that died was waited out.** The benchmark's seed broke on older Sonarr (both series TvRageId 0, a
  unique index then); the start script exited and serve waited 300 s to say only "didn't answer" (SONB-20 on).

Decided, all general (no project's layout, no tuned numbers):
1. **Absolute rules.** Edit rules and the result file are `//<absolute path>` (`ClaudeRules.Absolute`), and the driver
   runs Claude Code with `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1`, so each command starts back in the repo and the
   repo-relative commands stay valid. Same for `spec-run.sh`'s agent mode.
2. **Setup failures are recorded apart.** `ticket check` reads Claude Code's `permission_denials`. A refusal of what
   stangent's own rules allow (a write the target allows, the result file, a command line made only of allowed
   commands and `cd`s; anything piped, redirected or joined with another command doesn't count) means the attempt
   measured the setup: unless the test still reproduces the bug (code checked that), it's `failed` with
   `setupBlocked: true` (bug-proof v1, unpublished, no bump), no retry, status `blocked` (`setup_blocked`), and the
   driver stops the batch. A refusal of anything else is the rules working. The next run tries the ticket again. The
   score lists setup-blocked proofs apart.
3. **Fail fast.** A started process reports whether it exited and its last output; the app start stops at once when
   its command exits non-zero, with that command's own error. One that exits 0 (it may have put the app in the
   background) is waited for as before; the start command should stay in the foreground.
4. **Unit targets from the code at every starting point.** A point that is code is itself; a page point is its entry
   (a handler's page: the handler). Only when no unit test covers that code is the code the point pages run (what
   their slices walk: an endpoint's action and the services behind it) tried next, so it widens the search without
   outranking the points' own projects (a fresh review found the wider set could push the right project out of the
   top 3).

**The 30-turn cap stays (D86).** Of the 8 "cap" tickets only 3 (SONB-6, 10, 18) really used their turns; the cap is
judged again on a run without the setup failures, from where successful attempts' turns go, not by guessing a number.
The seed fix is benchmark data, not stangent.
**Why:** a score that mixes our setup bugs with the AI's misses can't tell anyone what to fix; the owner wants fixes
that hold for any project, not numbers tuned to one.
**Cost:** a failed attempt is re-run (its spend so far is lost); the driver depends on a Claude Code environment
setting; a ticket whose targets change gets a new attempt on its next run (its input changed, D18), which also clears
an earlier confirmation; a refusal reported by a different spelling of the repo's path (a symlink) isn't recognised and
counts against the reproducer as before.

## D90 · A test runner that can't run is the setup's failure; a mock's own check is an assertion — 2026-11-01
The second full Sonarr run was stopped at SONB-27: from SONB-23 the commits target .NET 8 and the benchmark Mac had
only the .NET 10 runtime, so `dotnet test` couldn't start the test host. stangent read the empty report as "no test
named … ran" and blamed the reproducer (SONB-23 to 26, ~$1 lost). Once .NET 8 was there, a second machine-side cause
showed: Sonarr builds all tests into one shared folder, and a newer commit's NUnit adapter left there finds no tests at
all in an older commit's build. Separately, SONB-19's test failed through Moq's `Verify` and wasn't accepted.

Decided (general; the machine fixes are the benchmark's):
1. **A runner failure is a setup failure.** When a run has no test result (no test in the TRX with an error from the
   run, or "no test named X ran"), stangent lists the project's tests (`dotnet test --list-tests --no-build`). Only if
   the runner lists none at all (no adapter loaded, no runtime) is it `runner-error`; if it lists tests, a run that
   stopped with an error is the test crashing the host (a stack overflow: the test's doing, it gets the retry) and a
   missing name is the reproducer's. A `runner-error` is (bug-proof v1 run result; unpublished, no bump). The attempt is `failed` with `setupBlocked: true`,
   no retry, and the batch stops (as D89).
2. **A mock's own check is an assertion.** Moq's `MockException`, NSubstitute's `ReceivedCallsException` and
   FakeItEasy's `ExpectationException` count as assertion failures: "expected this call once, got 0" is the test's
   assertion, written against a mock instead of a value. A strict mock refusing a call it wasn't set up for ("with
   mock behavior Strict", "unconfigured method of strict fake") is the code under test failing, not a check. NUnit's
   own format counts too: it trims its frames from the stack, so a classic `Assert.AreEqual` shows only "Expected: 2 /
   But was: 1" (SONB-23 was rejected for that).
3. **Benchmark only:** a .NET for the benchmark with the 10 SDK and the 8 runtimes (`~/stangent-bench/dotnet`, from
   Microsoft's `dotnet-install.sh`, no admin), used by `run.sh` when present; between commits `run.sh` also clears
   Sonarr's shared build folders (`_tests`, `_output`), and the app scripts find the app's framework folder instead
   of assuming `net10.0` (a fresh review found older commits' browser proofs had run another commit's app). `run.sh`
   stops at the first setup failure (D89), a runner failure on the fix commit included.
**Why:** a score must not count the machine against the AI; mocks are how most C# tests check side effects.
**Cost:** one extra `--list-tests` run when a name isn't found; a full test build per commit in the benchmark.

## D91 · A setup failure gets a plain reason and advice; stangent never installs anything itself — 2026-11-01
In normal use a developer proves one ticket. When the machine can't run its tests (a missing .NET runtime, no Node,
an app that won't start, a runner that finds no tests), D89/D90 record `failed` with `setupBlocked: true` and the raw
error, which is hard to act on.
Decided (owner, 2026-11-01): the attempt stays `failed` (setup, not the bug) and also carries **advice**, chosen by
code from the error: the missing runtime and its version with where and how to install it, the app's start step and
its error, Node/Playwright to install, a runner that lists no tests (clean rebuild, its adapter), or "a stangent bug,
report it" for our own rules. Anything unrecognised: the raw error and "fix the environment, then prove again".
`/stangent:ticket` shows it. **stangent never installs software or changes the machine on its own**: advice is text
for the developer to act on. Batches are rare (the owner proves one ticket at a time); when one runs (CI, the
benchmark) it still stops at the first setup failure (D89), since every later ticket would hit the same wall.
Built 2026-11-01: `SetupAdvice` (bug-proof v1 attempt `advice`, unpublished, no bump); app build/start/seed
failures are setup failures now too (they stop a batch with advice); `/stangent:ticket` shows the advice and doesn't
install or change anything unless the user asks.
**Why:** a developer needs to know what to do, not read a host error; installing without asking is the developer's
call, not the tool's.
**Cost:** a short list of known errors to keep in step with runners and platforms; unknown ones fall back to raw text.

## D92 · Must-pass fixes after the Sonarr run: drafts that stay sourced, a keychain that survives parallel runs — 2026-11-01
D79's grounded and repeatable failed on the scored run (phases/3-tickets.md): 18 of 90 drafts failed the claim check,
one draft was lost to `keychain_failed`, and every failed draft re-calls the AI on a re-run. D79: a must-pass failure
is a bug to fix and re-run. Decided with the owner ("fix the drafts now"), all general:
1. **The claim check stays as strict.** Most failures were "Label: finding" lines ("Fix: dates were compared in the
   wrong time zone"), restating the ticket without a link, or an id not copied as given. The question and summary
   writers get those as examples (neutral ones, not from any benchmark ticket) and a check-every-line step; the
   check's feedback names the "Label: finding" case, and a "Label: finding" line is no longer a framing line in bold
   or as a heading either. After the first re-run (7 questions drafts still failing): the questions writer is told to
   link what it repeats from the ticket or ask straight away, and a request ("Please send…", "Could you…", "We'd
   like…"; not "Please note…") counts like a question: it asks for something, it states nothing.
2. **One process at a time opens a Jira session with the shared sign-in** (a lock file under the user's local data
   folder): the four parallel pulls had all found the access token expired and refreshed and saved it at once. The
   token cache reads the keychain each time (another process's refresh is seen); a refreshed token that won't save is
   a warning, not a failed call. Keychain calls also retry a passing failure (up to 5 tries; not "not found", a
   refusal or a bad call). A failed pull now fails `ticket-run.sh` with its message (it used to return 0, so a draft
   was lost silently).
3. **Benchmark only:** `run.sh` passes page ids with spaces whole (they were never generated, so D79's "missing pages
   first" didn't happen), and its replay also checks resolution drafts (it under-counted re-run calls) and keeps a
   failed draft's output and feedback with the saved results before the check clears them.
Then pages → points → bundles → drafts → replay again (no new proofs), and a new score.
**Why:** D79 makes grounded and repeatable must-pass; loosening the check would hide unsourced claims (D18).
**Cost:** longer writer instructions; a keychain call can wait up to ~1.5 s on a busy keychain.

## D93 · A version given in the environment counts as the version — 2026-11-01
The fresh check of the final drafts found `version` listed missing on 49 of 50 Sonarr tickets, though 41 give it among
the environment lines ("- Sonarr: 4.0.11.2815"). So question drafts asked for a version the reporter had given, and a
`missing:version` link could back "the version field is empty". Many issue templates do the same.
Decided (owner, "fix version bug"): with no version field or heading, a "Label: value" line in the environment whose
value holds a version number counts as the version (`from`: "environment: a line with a version number"). Lines naming
the machine around the product (OS, browser, database, runtime, container, proxy, server…) don't count. General: no
product name is assumed.
**Why:** asking for what the reporter already wrote wastes their time and makes the draft wrong.
**Cost:** a product-specific label that isn't the product (a plug-in's version) may be read as the version; the list of
"around the product" labels is a heuristic.

## D94 · The generality batch: nothing in stangent assumes the benchmark repos — 2026-11-01
The audit after the Sonarr run (owner: "after the run", then "continue fixing others") found no hard-coded project names
but assumptions copied from the benchmarks. Fixed, each general:
1. **MTP repos:** the reproducer may run `dotnet test --project '<proj>'` (Microsoft.Testing.Platform's form) as well
   as VSTest's; the allowed command and the one it's given now match.
2. **Playwright config:** stangent runs Playwright with `.stangent.playwright.config.ts`, written beside the repo's own
   config: the repo's config unchanged, then the base URL set to the served app (whatever the repo reads), the test
   folder widened to take `tickets.app.specDir` and its `*.spec.ts|js`, and the repo's `webServer` off. Checked live on
   Sonarr's config and on a layout with `testDir: ./tests`, `testMatch: **/*.e2e.ts`, a fixed `baseURL` and a
   `webServer`.
3. **The proof's build** gets the graph build's properties (the main solution's folder, NuGet audit off, Windows
   targeting) unless `tickets.dotnetArgs` sets them.
4. **Test projects** are found through any chain of project references (was two levels), paths compared ignoring case.
5. **Ticket lines "- Key: value":** the key is dropped only before a short answer (two words or fewer).
6. **Rank weights** (D87) are settable in `tickets.rank`; defaults unchanged.
7. **Instruction examples** are neutral (no Sonarr words). Changing page-writer's marks existing reference pages out
   of date; they're re-written only when the owner asks (a benchmark run doesn't).
8. **Event handlers:** built-ins are general libraries only (MediatR, MassTransit; nopCommerce's interface moved to
   its benchmark config); same-named events in other assemblies link only for `busInterfaces`.
9. **Frontend API helpers:** `@VERB` and `#N` arguments in `wrappers`; the verb read off the name also knows
   delete/remove, put/update and patch.
After a fresh review (same day): the generated Playwright config leaves the repo's projects alone and adds one,
`stangent` (run with `--project=stangent`), with the first ordinary project's settings and dependencies, so a login
setup project still runs first and a reproduction runs once, not in every browser; spec globs use forward slashes
(Windows); the file is added to `.git/info/exclude` and opens with `@ts-nocheck`/`eslint-disable`. MTP test runs use
`--no-build` (the build with the args already ran). A frontend run that can't find Node or a Playwright browser is a
setup failure with D91's advice. Version labels are judged word by word ("OS Version", "Chrome", ".NET Version",
"Host" aren't the product) and an IP address isn't a version. A wrapper's verb is read off whole words of its name
(`dispatch` isn't "patch"). The repo scan for build properties runs only when a .NET test target is found; values
with shell characters are quoted. Bus linking has its own tests.
Left open: Karma/Jasmine and Mocha test targets (a runner and report reader each).
**Why:** stangent must work for most C# and TypeScript projects (owner); every one of these misbehaved on a plausible
repo shape.
**Cost:** a config file stangent writes into the repo's Playwright folder (untracked, regenerated each run); picks
re-run where the picker's instructions changed.

## D95 · A proof says which ticket sentence its failure shows; a developer confirms the match — 2026-11-01 (revised the same day; built, not yet run on Sonarr)
On Sonarr, 5 of 22 "reproduced" tests still failed on the developers' fix (SONB-1, 11, 14, 30; SONB-2 a compile
quirk): they tested the AI's guess at the cause, not the reported behaviour.
- **First decided (owner, "require it"), then measured and dropped before building:** code would count a failure as
  proof only when its message shares a value or a run of words with the ticket's actual result. On the saved run (no
  AI) a loose match caught 2 of the 5 bad proofs but rejected 8 of the 17 good ones; a strict one (only the value the
  test got) matched 3 of the 17. Tickets say what users see ("it fails to import any anime titles"), failures print
  code values ("Expected collection {empty} to contain …"): code can't line them up.
- **Decided instead (owner, "yes, go with that"):** the reproducer also returns `actualQuote` (the ticket's sentence
  giving the actual result, word for word) and `shows` (the words of the failure message that show it). Code checks the
  quote is in the ticket and that `shows` is in the whole failure message of every run, and not in the test's own source
  (its expected value or "because" text says nothing about what the code did); either failing gets the one retry.
  `shows` is stored only on a reproduced attempt, once checked. The developer sees "the ticket says … / the test got …" side by side and confirms with
  `ticket prove --confirm` (D75). Until then the proof is advice (D79's fallback). No new outcome value.
**Why:** a code-only match was wrong both ways; a checked side-by-side makes the developer's call quick and honest.
**Cost:** a person decides whether the failure is the reported one; a reproducer that can't name the line costs the retry.

## D96 · A failing test shows the code differs from what the reporter expects; only an FSD or a developer makes it a bug — 2026-11-01 (honest result and developer's call built; document verdicts wait for D99)
On Sonarr, 7 of 10 not-bugs were "reproduced": the test asserts the reporter's expectation (SONB-43 "no auth
required", by design in v4), the code deliberately does otherwise, so it fails. D95 doesn't stop it: the failure does
show what the reporter saw. Code alone can't tell intended from broken; only something people wrote says what should
happen.
Decided (owner, "unless we have FSD to back it up"):
1. **Honest result.** A failing test (that passes D95) is recorded as "the code does Y, the reporter expected X", not
   "bug proven". It becomes a bug when an FSD section backs the reporter's expectation (a confirmed FSD link that
   agrees with the expected result: "bug, per FSD §…"), or when a developer confirms it (`ticket prove --confirm`).
2. **Likely intended.** When an FSD section says the current behaviour is how it should work, the ticket is marked
   "looks intended (FSD §…)" and the questions draft asks the reporter instead of proposing a fix.
3. **No AI judge** of bug versus intended: nothing could check it (D18).
The FSD is converted by code, word for word (Word only today; D13, D16); nothing about it is rewritten.
**Why:** a proof that can't tell a bug from intended behaviour misleads; the people's written agreement decides.
**Cost:** on a repo without an FSD (Sonarr) nearly every result stays "needs a person's call"; scoring D79's "catches
wrong reports" needs the new result values.
- *Revised 2026-11-02 (D99, not built):* "an FSD section" becomes **an approved requirement** (from a document or a change-request
  ticket; never an as-built draft): "bug, per R-0042" / "looks intended (R-0042)". The matching scenario names which
  requirement decides. Until D99 is built, FSD sections as above.
- *Split 2026-11-02 (owner):* FSD links join a page to a section (D53), so `agree` says the code matches the FSD, never
  the reporter's expectation, and nothing checked can pick the section a ticket is about (D99's open question). So
  "until D99, FSD sections" is dropped: **built now:** `reproduced` stays the test's outcome and reads as a difference to
  decide; `--confirm` means "the reported problem and a bug", not only "the reported failure"; a questions draft gets the
  expected result and isn't to call an unconfirmed proof a bug (an instruction, no code check); a resolution draft, after
  the fix, is as before. **With D99:** "bug, per R-0042" and
  "looks intended (R-0042)". Until then a person makes every bug-or-not call. No new outcome value and no contract
  version change: the fields stay the same, `confirmedBy` means more.

## D97 · A draft leaves out a sentence no claim backs, instead of failing — 2026-11-01 (built; how sentences are found: D98)
After D92 about 7 of 90 drafts still failed both tries per run (a different 7 each time): the AI adds one unsourced
line ("Thanks for the detailed report.", "We couldn't reproduce this."), so grounded/repeatable passed by chance.
Decided (owner, "drop the line"): code leaves such a sentence out of the draft (`Drafts.LeaveOut`) and records it
(`ticket-update` v1 optional `dropped`, listed under the draft as "Left out"); everything left is still checked. A short
greeting or thanks (8 words or fewer) needs no claim. A draft with nothing left gets the retry. Link and quote errors
still fail the draft.
**Why:** nothing unsourced reaches Jira either way; a draft lost to one stray line helps no one.
**Cost:** a dropped line can leave the text reading abruptly; the person reading before posting sees what was dropped.

## D98 · The writer hands over its draft as a list of sentences, each with its claims — 2026-11-01 (built)
D97 first cut sentences out of the writer's free Markdown by guessing where sentences end. A fresh review (D61) found
unsourced text still reaching Jira that way: a sentence after `[C1].` that starts lower-case, with `` ` ``, `**` or a
`;` rode on its neighbour's claim; lines wrapped short escaped the check; the cut could hit the same words in another
sentence; `**bold**` lost a `*` and could turn into a list item; greetings ("Hi team, the export drops rows.") and
requests ("Please be advised …") let statements through.
Decided (owner, "okay sure"): `draft-output` v2 is `{"parts":[{"text","claims":[ids],"start"}], "claims":[…]}`: each
part is **one sentence or one framing line**, `start` says whether it begins a new line or paragraph. Code needs no
sentence guessing: a part with a line break, or a sentence end (`.`, `!`, `?`, `;`) followed by more text, is an error
(one retry); text inside quotes or backticks, "..." and a lower-case letter's full stop ("e.g.", "5 p.m.") don't
count, but a quote closing on a sentence end does unless lower-case text goes on, and an initial ("J. Smith") costs the
retry rather than let a second sentence through. A part
with a claim is kept; a part without one is kept only if it states nothing; every other part is left out (D97).
A second fresh review closed what still leaked (owner, "yes, close them"):
- **A claimless question or request must be a pure ask:** it starts by asking (a question word, "Please", "Could
  you", "Let us know"…) and holds no reason ("because", "as", "so we", "now that"), no `:`, `;`, dash, bracket or
  "which", no "confirm"/"verify" and no "did you know". "The ticket says 40, but the FSD says 100: which is right?" needs
  its claim. A greeting may come first ("Hi Sam, could you…?"). The writer instructions name the same words.
- **Greetings, thanks and sign-offs** are kept only when they are nothing else; a name after "Hi" must be a name.
- **A claim backs one part:** one `pr` link can't stand behind the whole comment.
- **Every kept part with no claim is listed** in the record (`unchecked`) and the `.md` ("not checked, read them").
- **`Join` keeps Markdown whole:** headings, list items and quotes start a new line, `---` a new paragraph, the line
  after a heading or rule a new one, and a plain line after a list item or quote a new paragraph; top-level numbered
  items are renumbered; a nested item keeps its indent; an empty part is an error, never a crash. No code blocks or
  tables (a part is one line).
- **Known limits, all but the last shown as "not checked":** a question that takes a fact for granted ("Why does the
  cache corrupt every order?"), Title Case words after "Hi" read as a name, a comma splice inside an ask; and a writer
  can make several claims with the same `pr` or `file` link (those links have no quote to check).
The `ticket-update` record keeps its shape (`text`, `claims`, optional `dropped`, `unchecked`).
**Why:** the writer knows its sentences; code checking a list is exact, guessing sentences in prose isn't (D18). Code
can't tell whether a sentence says more than its source; the person posting reads it first (D74), now with the
unchecked parts in front of them.
**Cost:** "etc." or "vs." in a part costs the retry; a legit question with a reason in it is left out unless it has a
claim; the writer instructions and output shape change (internal to the step, no contract bump); a short heading or
lead-in can still state a finding (D84's trade-off, open, but now shown as "not checked").

## D99 · The FSD becomes a structured spec, built from word-for-word quotes; the BA approves each requirement — 2026-11-02 (decided, not built)
The imported FSD (D52) stays the record, byte for byte. On top of it, an AI step builds a structured spec: features,
requirements (rule, validation, flow, data, permission, non-functional), acceptance criteria, journeys and data
definitions, each with a stable id (D11's original aim). Then requirement ↔ page statement ↔ code are linked.
- **Every item cites word-for-word FSD quotes** (section id + text); code checks each one, as for links (D53).
- **A fresh AI checks each item says no more than its quotes** (D61, like the agree-checker).
- **Ambiguities and clashes in the FSD become questions for the BA**; the AI never resolves them.
- **The BA approves each requirement**, in a spec-only PR (D11). Until then it is `proposed`: shown as context, never
  used to judge "bug or by design". Editing a requirement or its quoted FSD text drops it back to `proposed` (D11's hash).
- **Reference pages stay:** they are the "does" side that requirements ("should") are compared with, and the checked,
  stored reading of the code that linking would otherwise redo each time.
Reopens D52's "Rejected: one merged spec": the FSD isn't rewritten, a checked layer is built on it, and a person owns it.
**Why:** an agent can use a requirement with acceptance criteria; it can't reliably use a section of prose. The BA knows
the business logic, so only their approval makes a requirement the truth; quotes and the checker keep the AI honest
until then.
**Cost:** the BA's time, requirement by requirement (the largest cost); one AI pass per FSD section on import, re-run
when a section's text changes; a new contract beside `source-document` (D20: consumers marked `needs review` when it
lands); designed without a real FSD so far (D56). Doesn't raise right place (Sonarr has no FSD); aims at not-bugs
"proved" as bugs.
- *2026-11-02, owner:* designed and first measured on the planted Sonarr FSD (`benchmark/fsd/sonarr/`, 2b): tidier than
  a real FSD, so a pass there doesn't prove a real one; the real-FSD run (D56) stays owed. Its frozen answer key (30
  planted items) gives a ready bar: requirement-level links should find as many planted items as 2b's section-level
  links (27/30) with fewer false alarms (2b: 31.3%, bar ≤ 30%; 25 of 62 unplanted were agrees disputed because the
  page covered only part of the section, which requirement-level links address).
- *2026-11-02, owner — the spec's shape:*
  - **Items:** features (one per FSD section) holding requirements (id, kind, statement, source quotes, acceptance
    criteria, status, links); journeys (ordered steps, each pointing to requirements); scenarios per journey (happy,
    alternate and error paths); data definitions; and, only when the FSD says something about them: scope / out of
    scope, glossary, roles, states, messages and errors, outside systems (D83). Success criteria only when the FSD
    states them. Left out: background prose, revision history, layout and images (no quote), build hints.
  - **No bloat:** no quote, no item; one fact in one place (items refer to data, glossary and requirements by id;
    journeys and scenarios add no statements); no empty sections; **every FSD sentence is either cited by an item or
    marked `background`** (a code check; the BA reviews what was marked background).
  - **Scenarios combine requirements, never add any:** every step and outcome points to a requirement; an alternate or
    error path exists only if the FSD states it, otherwise it is a question for the BA. Tests carry scenario ids as
    well as requirement ids (D10), so coverage shows which scenarios have a test.
  - **Diagrams are drawn by code** (Mermaid) from journeys and states: a view, not a source, never written by the AI.
  - **Files:** `specs/requirements/<doc>/` with `index.yaml` (one line per item, read first), `glossary.yaml`, one file
    per feature (`F-<id>.yaml`) and `questions.yaml`; agents open only the features they need.
  - **Acceptance criteria may add example values** ("Firefly", "10 selected"), **never an outcome the FSD doesn't
    state**; an unstated outcome becomes a BA question (owner, 2026-11-02). The fresh checker treats an outcome with no
    quote behind it as "says more than its quotes".
  - **Big features (owner, 2026-11-02):** features follow the FSD's own section tree, split by code (a section with
    sub-sections becomes a parent feature listing its children; no size limit to tune). Agents read one item by id
    (a command like `stangent spec req <id>`, returning the item with its quotes, criteria and links), never a whole
    file. A section with many requirements and no sub-headings is flagged to the BA as a question: the BA adds
    headings to the FSD (the next import splits it), or approves AI-proposed sub-groups labelled as the AI's; the AI
    never splits silently. A compound sentence ("a series, a season, or a single episode") becomes several atomic
    requirements citing the same quote.
  - **Ids (owner, 2026-11-02):** plain numbers assigned once and never reused (`R-0042`, `F-0007`, likewise journeys,
    scenarios, data and questions); section and title are fields, never part of the id, so renumbering or renaming
    the FSD changes no id (D11: stable for the requirement's life). On re-import, an item whose quotes are unchanged
    keeps its id and status; a changed quote keeps the id and goes back to `proposed`; a removed quote retires the id.
    People read the title (`R-0042 · Can't add a series twice`).
  - **Reworded quotes in a new FSD version (owner, 2026-11-02):** code first matches by exact quote; for what's left
    (old items with no match, new text with no item) the AI proposes pairs ("R-0042 was reworded as …"); code shows
    the BA old and new text side by side; a confirmed pair keeps the id and goes back to `proposed`; a rejected pair
    retires the old id and the new text gets a new one. Treating every rewording as new was rejected: each typo fix
    would break test and link ids and force a fresh approval.
  - **Several documents and a reconcile command (owner, 2026-11-02):** each document (FSD, change request, addendum) is
    imported on its own with the effective date the document states (`fsd import … --effective <date>`, confirmed
    by the BA) and a status, draft or approved (D15: only an approved document supersedes; a newer draft's
    differences show as proposed changes); the same `--id` with a new file is a new version of that document, the old one stays in the archive. A requirement may cite
    quotes from several documents (current and superseded); a change request touches only what it mentions. After
    any import, `stangent fsd reconcile` (name not final) works out what changed, by code: unchanged items kept,
    removed quotes → ids to retire, new text → items to extract, reworded quotes → pairs for the AI to propose, and
    clashes between documents → the newest approved document proposed as the winner. The AI parts run as the usual prepare → AI → check
    loop (D60). Each result is an entry with an empty `decision` (in a reconcile file beside the spec, plus a Markdown
    view for the PR); **the BA confirms every clash**, reworded pair and retirement; `reconcile apply` then updates
    ids and statuses. Until confirmed, affected items stay `proposed` (context only), never blocking other work.
    **It runs automatically (owner, 2026-11-02):** when an import lands (a change under `specs/fsd/`), CI runs
    reconcile and its AI steps and opens a spec-only PR for the BA, as D55 does for stale pages (`plugin/ci/`).
  - **Jira tickets as sources (owner, 2026-11-02):** a change-request ticket (story or feature request; which issue
    types count is a setting) may be a requirement's source: the requirement quotes the pulled ticket word for word,
    code checks the quote, it is labelled with its key (`source: Jira SONB-41`) and the BA approves it like any other.
    When a later document covers it, reconcile pairs them and the document becomes the main source. **A bug report
    never creates a requirement:** its "expected" is the reporter's view, not an agreement (D96).
  - **No FSD: as-built drafts (owner, 2026-11-02):** with no document, an AI step turns reference page statements into
    draft requirements in the same shape, quoting the statement (`source: code (as built)`). They are context only and
    never judge "bug or by design" (drawn from the code, they always agree with it). The BA adopts or corrects them one
    at a time (no bulk approval for as-built drafts); an adopted one records "agreed by the BA from as-built"; a
    corrected one that the code now disagrees with is a bug.
  - **Testing without a BA (owner, 2026-11-02):** for benchmark runs only, until a testing project with a proper FSD is
    found, approval is skipped: a setting (off by default, never in a user's config) treats `proposed` items as
    approved, and every result made with it says so. **As-built drafts still never judge bugs in that mode** (owner):
    with approval skipped they would mark every reported behaviour "by design", so the judging score would only
    measure that loop. They are still scored for extraction, shape and links.
  - **How the BA approves before the Phase 8 dashboard (owner, 2026-11-02):** the spec-only PR's description lists each
    item as a tick box (title, quotes, acceptance criteria, any clash); the BA ticks what they approve, then approves the
    PR; on merge, code marks ticked items approved and leaves the rest `proposed`; `decided_by` comes from the PR's
    approver via git, never typed (as D58). One PR per feature for big batches. `stangent spec approve <id>` (name not
    final) stays as a backup for developers. Needs the BA to have repo access; otherwise approval waits for Phase 8.
  - **A confirmed bug the spec doesn't cover (owner, 2026-11-02):** refines "a bug report never creates a requirement".
    The report alone still doesn't, and nothing reaches the spec automatically. When a developer confirms a bug
    they also say whether it exposes a gap in the spec, default no: most bugs are code mistakes, and a question for each
    would bloat the spec and waste the BA's time. A yes opens a BA question quoting the ticket. The BA chooses: an
    acceptance criterion on a requirement, a scenario, a new requirement (source: the ticket), or no change, which is
    recorded. The proof's test then carries the new id. **Why:** otherwise each repeat is a person's call again.
    **Cost:** a developer can miss a real gap; the spec is then no worse than before.

- *Placed 2026-11-02 (D109, owner):* built before Phase 4, as phase 2c on the roadmap.
- *Matching a ticket to its requirement (owner, 2026-11-02, "yeah sure"):* code limits the candidates to requirements linked, through
  page statements, to the code at the ticket's starting points; no link means "no requirement covers this". An AI picks one or none,
  quoting both word for word, and code checks the quotes. A fresh AI, without seeing the pick, checks that the requirement says what
  should happen in this situation; if it disagrees there's no match. **A person always confirms:** until then it reads "suggested:
  R-0042", and only then "bug, per R-0042" or "looks intended (R-0042)", for an approved requirement only. Rejected: automatic when
  the checks agree (both AI steps can share a misreading, and there's no data on how often). May loosen later on measured numbers.
- *Scoring the match (owner, 2026-11-02, "lets go"):* only 7 of Sonarr's 50 tickets reach any FSD section, 1 of them a not-bug, and
  reaching one doesn't mean the ticket is about it (SONB-46 reaches only the Library chapter); the 27/30 bar scores requirements and
  links, not this step. So a fresh agent writes a key on the planted FSD before anything is built, frozen: tickets that clearly fit a
  requirement and tickets that clearly don't. **Cost:** it's our own test set, so a real FSD (D56) stays the judge.
- *A requirement no page statement covers (owner, 2026-11-02, "yes, labelled"):* it can still match, second after linked ones. The AI
  sees such requirements' titles and FSD sections; the same checks and a person's confirmation apply; it reads "no code found for
  this rule", so it says the feature may not be built, not that existing code is wrong. **Why:** a feature that was never built is
  what a spec is best at catching ("the episode list can be exported as a CSV file"), and links alone would say nothing covers it.
  **Cost:** more candidates, so more wrong picks; the key tests this case.
- *Bars for the match (owner, 2026-11-02: strict, since an agent acts on the business logic it's given):* on the frozen key of 30 tickets
  (15 a requirement with code covers, 5 one with no code, 10 none): at least 90% of suggestions name the right requirement; at least 8 of
  the 10 say "none"; at least 14 of the 20 find it. If either of the first two fails, matching ships off and requirements stay context; if
  only the third fails, it ships on and the report says so. Agreed before any run; not moved after one.
- *A fix that goes against an approved requirement (owner, 2026-11-02, "stop and ask"):* an agent trusts the spec, so a wrong approved rule
  gets implemented faithfully and every check passes. An approved requirement outranks a ticket: the match says "looks intended", no fix
  starts, and a rule that's wrong changes through the BA first (D11). A diff touching code linked to an approved requirement is flagged;
  code can tell it touched that code, not that it broke the rule, so a person judges. Details with Phase 4. **Cost:** no check can show a BA
  read carefully; D53's telemetry idea (approvals later reopened, time per approval) applies here too, as a follow-up.
- *Recheck triggers (owner, 2026-11-02, "as recommended"):* when a page goes stale (D55), its links are judged again, in 2c; a broken link stops
  saying `agree` and shows as a conflict for a person, and the BA's approval stands, since the FSD's text hasn't changed. A code PR listing the
  requirements it reaches with no test waits for tests that carry ids (Phases 5–6). No trigger after a failed proof: 8 of the 9 saved
  `not-reproduced` only ran out of turns, and a developer already flags a spec gap when confirming a bug. **Cost:** an AI step per merge
  that changes a page's links; small, not yet measured, reported.
- *Adoption replaces D14's audit (owner, 2026-11-03):* no separate audit. `/stangent:adopt`, with a driver script behind it as for tickets and
  pages (names not final), runs the existing steps in order: graph, pages, FSD import, extraction, links, spec-only PRs. Each skips what
  its inputs say is done, so it resumes, and the pages step asks before spending. The difference list appears only if 2c's links pass the noise
  bar and findings go on. After day one the triggers keep it current. **Why:** a separate audit would repeat steps needed anyway, and could
  drift from them. **Cost:** no single report; still the largest AI spend and a large BA backlog on day one.
- *Phase 3's follow-up when 2c lands (owner, 2026-11-03, "as recommended"; D20):* `ticket match` is its own step after the bundle; `ticket-bundle`
  v3 adds the linked requirements and keeps FSD sections; `bug-proof` v2 records the verdict, and `--confirm` also confirms the match; drafts may
  link a requirement, and "looks intended" asks the reporter. The finder doesn't change. Verdict: the requirement agrees with the reporter and a
  test fails → bug; it agrees with the code and a test fails → looks intended, no fix starts; no code found → may not be built; no match → a
  person decides, as today. Skipping the proof when a match says "intended" waits for numbers. **Cost:** saved drafts go stale; two contract
  bumps, with no consumer yet; one more AI step and its fresh check per ticket.
- *What "cited or background" means (found by a fresh check of the first build, 2026-11-03):* a loose check let a rule leave through either exit: `Orders
  over 50 need` covered "Orders over 50 need approval unless the customer is a VIP", and a rule marked background was seen by nobody. So: **cited** means the
  quotes of requirements, criteria and definitions together cover every word of the sentence, whole words only; journey and question quotes don't count, a
  piece under 4 words counts only as a whole sentence, and a table row must be quoted whole, since any cell could be the rule ("Refunds need a sign-off | ✓"). The
  fresh checker also rejects a quote added to an item it has nothing to do with, which code can't tell. **Background** is one sentence exactly as listed; the
  checker sees it and can say it needs an item, and the BA will see it in the approval list (step 4). A table's first row is a sentence, since Word doesn't say
  whether it's a header. **Cost:** more retries, and one more thing for the checker and the BA to read.
- *Ids on a re-run, until reconcile (same check):* items with the same quotes and words keep their id; reworded items keep it only when there's one old and one new
  item on those quotes. With several, a guess could swap two ids and carry links and approvals to the wrong rule, so they get new ids. The title and the words of
  definitions it uses are in the hash, and any answer from the checker but `ok` drops approval too. **Why it isn't finished:** D99 says unchanged quotes keep id and status, and also that editing a requirement
  drops it back, and an AI re-run rewords freely. Step 6 settles it at the root: code carries forward every item whose quotes are still in the text, and only new or
  changed sentences go to the AI (D18). Also step 6: a changed quote keeps its id, a renumbered section keeps its ids, and retiring waits for the BA.
- *Requirement-level links, first measured (2026-11-03, sonnet):* worse than 2b on both bars: 24/30 found (2b 27), false alarms 44%, range 29–59% (2b 31.3%), from a fresh agent's hand check of 20
  (`benchmark/results/2026-11-03-phase2c-sonarr-links-score.json`). All 20 planted gaps and conflicts were found; 6 of 7 undocumented behaviours weren't. 11 of the 12 false alarms are gaps where the code does what the FSD says
  and the summary page left it out, so the hope above, that a finer grain gives fewer false alarms, was wrong: a page saying less doesn't show the code lacks it. **Open, for the owner:** 56 of 109 requirements read "no code found", and only 10 of those
  are really unbuilt; proposed: "the page doesn't mention this", with gaps naming a page at the ticket's code in the main candidate list. Bigger option: check each gap against the code. Findings stay off (D68).
- *A clash between sections (same check, no owner decision yet):* D99 says clashes become BA questions, but one pass per section can't see one: on the planted FSD,
  R-0014 lists 3 things tags link to and R-0001 lists 6, both `confirmed`. Added to step 6 as one AI pass over a document's requirements, proposing questions.

## D100 · Held-out bugs come from bitwarden/server; Sonarr's 40 become the tuning set — 2026-11-02 (decided; set frozen)
Every Phase 3 change so far (the page index, the picker, the bundle, the free checks) was judged on Sonarr's 40 bugs,
so no half of them is unseen and a split of them would still flatter the result.
- **Sonarr's 40 bugs (D78) are the tuning set** from now on: changes may be tried and compared on them.
- **The held-out set is fresh bugs from bitwarden/server** (C#, graph already built, the D47 split check's repo),
  picked by code with D78's rules where they apply (closed GitHub bug reports with one merged server fix, non-test C#,
  at most 15 files), excluding the 5 issues D87 already measured. It is frozen and committed before anything is run on
  it, and measured only to judge a finished change, never to tune one.
- **Fallback:** if bitwarden has too few such bugs for the bars to mean anything, newer Sonarr bugs (after the frozen
  set) instead, reported as same-repo (unseen, but no test of generality).
**Why:** a measure that tuning has seen can't tell a real gain from fitting; a second repo also checks stangent isn't
shaped around Sonarr (the owner wants any C#/TS repo).
- *Frozen 2026-11-02:* `benchmark/tickets/bitwarden/heldout.json`, 24 bugs (`select.py --heldout`): of 281 closed bug
  reports, 62 have a linked merged server PR, 29 fit (non-test C#, ≤ 15 files, merged before the pinned commit), less
  the 5 D87 cases. Fixes merged 2020-06 to 2026. 24 is below Sonarr's 40, so ranges are wider; enough to tell a result
  near the 70% bar from one far from it, so no fallback. No not-bugs (bitwarden's are not selected).
- *Tickets through Jira (owner, 2026-11-02):* the 24 are copied into their own Jira project (`--copy-to-jira`, as
  Sonarr's SONB) and pulled from there, so the held-out run tests the pull too; local files that skip Jira were
  rejected (a benchmark-only path real use never takes). `freeze.py` lays `heldout.json` out as Sonarr's set is
  (text unchanged; authors and dates read from GitHub; answer key = the merge commit's own files, as the agent
  baseline's). Images are not downloaded: the finder reads text only (D101). (The pull drops link and image URLs from
  that text: phases/3-tickets.md, 2026-11-02.)
- *Measured 2026-11-02 (D101, phases/3-tickets.md):* right place 20 of 24 (83%, range 67–96%) ✅, $1.28; the agent
  baseline 23 of 24. Not tuned on: the gap goes to Sonarr first.
- *Owner, 2026-11-02:* no proof run on bitwarden. Only the finder has run there (D101, D102), so the proof's bars stay
  measured on Sonarr alone, the tuning set, and Phase 3's report says so.
**Cost:** bitwarden needs its own setup for what a run uses (tickets in Jira or replay files, reference pages for the
page-based sources); bitwarden's bugs may be fewer and different in kind; the owner decided "your call".

## D101 · A coding agent finds the ticket's files; code checks them; the graph is the checker and the map — 2026-11-02 (decided; built, committed)
Replaces D70's starting points and D87's page picker as the way into the code. The agent baseline (phases/3-tickets.md,
2026-11-02, fresh-checked): a headless coding agent with only the ticket and the code, Read/Grep/Glob, named a fix file
in 36 of 40 Sonarr bugs (ours: 19) and 23 of 24 held-out bitwarden bugs (D100; strict 20 of 21), median 4 files, about
$0.05 a ticket. Pages as the way in cap right place at 65% (D98's stage table), and a nearby page explains uncovered code
in 1 of 12 bugs.
- **The agent searches the repo** for each ticket, read-only, capped (D86's 30 turns / $1 as a start), and names files.
- **Code checks every file it names** (D18). *Settled 2026-11-02 (owner):* for each file the agent returns the path,
  a word-for-word quote from the ticket (the part this file explains) and a word-for-word line from the file (the
  code that connects to it); code checks the file exists at the commit worked on and both quotes match. A failed check
  gets one retry with the reason (D60); a file that still fails is listed as "named, not shown in code", not dropped.
- **The graph stops being a way in.** It checks what the agent named, expands from it (impact, D27), links across
  boundaries (HTTP, events, tables), picks tests (Phase 5) and splits work (Phase 4). Pages explain the named files
  where they cover them; a nearby page is at most labelled context.
- **Removed:** D70's code-only shortcut (a text match skipping the AI) and the page-title / page-index picker as the
  source of starting points. The page index (D87) may stay as a hint; to be decided in the design.
- **Misses** (SONB-1, 18, 21, 32; bitwarden #2506) are grouped by cause; only repeated causes get a fix, tried on Sonarr's
  40 and judged on bitwarden's 24.
- *Design settled 2026-11-02 (owner: "go with the most optimal option"; built, uncommitted):*
  - **Under the cap** (closes the open question after D71's fix): the starting points are never cut; the impact
    answer's item lists go first (counts kept; `stangent graph impact` lists them); then the lowest-ranked pages. A
    cut page that is a starting point keeps its citations as pointers; a cut page that only explains a file doesn't.
  - **Pages explain found files:** the written pages whose statements cite code in a found file, most files first;
    for a file no page cites, the pages whose slice holds it. No nearby page (1 of 12).
  - **Impact starts from the starting points:** the found files' code, symbol points, page points' citations.
  - **Code's signals stay as the fallback,** not a gate: with `tickets.aiPoints` false or `ai.enabled` false they are
    the starting points (no AI); with the finder on they are only the ticket's text the finder reads.
  - **The page index (D87)** is no longer an input to starting points; it stays for the benchmark's measures.
  - **The finder's input is the ticket only** (prose and text attachments), as in the baseline; it searches the repo
    at the commit checked out, read-only, never `.stangent/` or `.git/` (permission rules; tested 2026-11-02 that an
    allow rule on the repo bounds Grep and Glob too). Its cap: `tickets.findBudget` (30 turns, $1 a call).
  - **Contract:** `ticket-bundle` v2 (file points with `line` and `why`; pages' `explains`; `rejected`;
    `cap.impactTrimmed`, `cap.dropped`). D20: Phase 4 consumes it and hasn't started; Phase 3's own readers updated.
  - *After a fresh review (D61), same day:* the finder's answer is kept while the ticket is unchanged and marked
    `stale` when the code moves on (a new search would see the half-done fix); `--refind` searches again. It searches
    a throwaway git worktree of the commit (no `.stangent/`, pages, build output or untracked files), and code checks
    files against git (tracked at HEAD, exact case) and lines against HEAD's text. An import or namespace line is no
    evidence. Files that failed their checks are shown in the bundle, and a ticket with them isn't "only outside the
    code". A retry gets what's left of the cap. Pages that only explain a file are left out (counted) when cut; an
    unwritten page whose slice holds a file no written page explains is listed as missing.
  - **Measured on Sonarr (2026-11-02, phases/3-tickets.md):** right place **36 of 40 (90%) ✅**, was 19; no bundle over
    the cap (was 19 of 50); $2.68 for 50 tickets. D79's fallback "AI picks off by default" no longer applies: the
    finder is on by default (`tickets.aiPoints: true`). A private repo was owed; dropped by the owner 2026-11-02.
  - **Held out (bitwarden, D100, 2026-11-02):** right place **20 of 24 (83%, range 67–96%) ✅**, $1.28; the agent baseline
    23. The finder names fewer files (median 2 vs 4); 3 of its 4 misses the baseline reached lower in a longer list.
**Why:** measured, on a held-out repo: the agent finds what our own search can't, cheaply; building our own search would
need to close 47.5% → 90%.
**Cost:** an AI call on every ticket (about $0.05 on these repos); both repos are public, so the rate may lean on what
the model already knows (no private-repo check: dropped by the owner 2026-11-02); about half the hits come from tickets that name the file; `ticket-bundle` and the points
record change shape (D20: Phase 4 consumes the bundle and hasn't started, so no consumer to mark).

## D102 · A ticket Jira sends as HTML reads as the same Markdown as any other — 2026-11-02 (decided)
The pull asks Jira for Markdown; Jira sends HTML instead when an issue holds what Markdown can't (media, panels). The
pull flattened that HTML to plain text, losing link and image addresses, list numbers, task-list ticks and code marks:
BWB-3's fixing-PR link reached the finder as "PR up". 5 of 51 Sonarr tickets and 11 of 24 bitwarden ones came as HTML
(45 links, 121 code marks).
- **The pull turns HTML into the Markdown Jira would have sent** (owner, 2026-11-02): links `[text](url)` (a bare URL when
  the text is the URL), images `![alt](src)`, numbered lists keep their numbers, task items `- [x]`, inline code in
  backticks, code blocks fenced with their language, quotes `>`, headings `#`, bold/italic/strike, rules, tables as
  `|` rows; Jira's media blocks stay `[attachment: name]`. Our own code (Jira's HTML is a small, known set of tags), no
  new library; tests built from the saved real tickets.
- Rejected: only adding each link's URL after its text (code marks and numbers stay lost).
- *After a fresh review (D61), same day:* it lost no word or URL on the 93 real HTML bodies. Fixed: a list keeps
  children that aren't items (Jira nests task lists directly); tags are read in linear time; an unquoted URL ending in
  "/" isn't a self-closing tag; a tick doesn't eat a line break; tables keep captions and count only their own rows. The
  output is Jira's Markdown shape *without* its backslash escapes (better for code's own signals). Two readers learned
  Markdown marks, which Markdown tickets already needed: the quote check's whole-line rule (D53) ignores a line's
  quote, heading, bullet, number and tick marks; a label may be `_Label:_`. A quoted heading is never a label (a quoted
  error page mustn't end the part it is in). All 93 bodies' Markdown is pinned in `tests/fixtures/tickets/html/`.
- *Measured 2026-11-02 (phases/3-tickets.md):* the 16 HTML tickets re-pulled and searched again ($0.84): right place
  Sonarr 36 of 40 (unchanged), bitwarden 21 of 24 (one flip, run-to-run variation). It fixes lost text, not the rate.
  The quote check accepts a short whole line with or without its marks (a fresh check found the marks-only form
  rejected "1. Save it").
**Why:** the same ticket should read the same whichever format Jira picked; links and code marks are leads for the
finder and for code's own signals (backticked names).
**Cost:** the text of HTML tickets changes, so their ticket hash does: their finder answers are marked changed and are
found again (≈ $1 for the 16). No contract changes shape. Measured on Sonarr's 5 first (D100), then bitwarden's 11.

## D103 · A code quote may be one whole line, even with no space — 2026-11-02 (decided, built)
Code checks every line the file finder (D101) and the page checks (D53) quote from code: at least 12 characters and
two words, so a bare name proves nothing. Real lines are often one token: `model.ValidateEdit();`,
`app.UseForwardedHeaders(options);`. On record, all 5 rejected code lines were one token, and 4 of them were whole
lines of their file (BWB-9 lost `ServiceCollectionExtensions.cs` that way). A fresh check (D61) counted every
rejection on record, retries included: 13, of which 10 were whole lines (SONB-1, 6 ×2, 18, 42's first try; BWB-9 ×2,
11, 18) and 3 were rightly refused (a string fragment, a bare name twice).
- **A quote with no space counts when it is a whole line** (after trimming), as a ticket quote counts when it is 4
  words or a whole line (D53). A piece of a line still needs two words (SONB-42's string fragment stays rejected).
  The 12-character floor stays. Owner: "fix that rule" (2026-11-02).
- The finder's instructions still say "two words" and are left as they are: they are part of its input hash, so
  editing them would drop every saved answer. The retry message says "two words, or a whole line"; the instruction
  text follows at its next real change.
**Why:** a whole line is as specific as two words; the rule existed to stop fragments, not statements.
**Cost:** none measured: it changes only which answers pass the check, so it is seen on the next finder run.

## D104 · The proof's turn cap counts model rounds, what `--max-turns` limits — 2026-11-02 (built; not yet run)
The audit of the 40 proof results (phases/3-tickets.md): `--max-turns` limits model rounds, while Claude Code's `num_turns`
on a normal finish is tool results + 1, so parallel tool calls inflate it. `BugProof` summed `num_turns` and compared it
with the limit before reading the answer, and threw out 6 finished answers unrun (SONB-6, 9, 18, 29, 46, 47: 22-29 real
rounds, under $0.45). It also cut SONB-10's retry to 3 rounds when 12 were left.
- **The driver counts rounds** (one per model message id in the transcript) and adds `rounds` to the result given to
  `check`. Claude Code's own stop, the attempt's rounds and the dollars still cap an attempt (D81); the 30 is unchanged.
- **With no `rounds`** (a result passed by hand), only Claude Code's own stop and the dollars apply, and a retry gets the
  whole limit. `num_turns` isn't a fallback: it would bring the bug back.
- *Observed, not read from Claude Code's source:* the pattern holds on all 46 saved transcripts; a stopped run ended at
  exactly its limit (SONB-28 at 30, SONB-10's retry at 3).
**Why:** a cap that throws out finished work measures our own bookkeeping, not the reproducer; D81's money bound is the dollar cap.
**Cost:** attempts use more of the turns they were given; expect few extra real proofs (of the 4 real bugs, SONB-29 would
probably pass on its fix; SONB-6, 9, 18 probably wouldn't). D86's raise to 30 stands: its deciding case, SONB-5, was a real stop at 15 rounds.
Nothing tests the driver's count in `dotnet test` (no test covers `ticket-run.sh`); a fresh check ran it on all 46 saved transcripts.

## D105 · The on-fix check says what kind of failure it was; D79's bar stays as it is — 2026-11-02 (built; shows-based comparison not yet run on real proofs)
The audit of the 40 proof results (phases/3-tickets.md): `--verify` knew only pass or fail, and a fail meant different
things. SONB-30's reported `ArgumentNullException` is gone, and the test's own setup then throws a `NullReferenceException`;
SONB-14 fails exactly as before, since the test never fires the event the fix relies on; SONB-2 can't compile, since the fix
changed a constructor the test calls.
Decided (owner, "of course lets go"): `verification.result`, by code:
- **`passed`:** the test passes and nothing new fails. **`new-failures`:** it passes, other tests started failing.
- **`same-failure`:** it still fails with the words its proof named (`shows`, D95). **`different-failure`:** it still fails,
  those words gone: the reported failure looks gone and something else fails.
- **`not-compiled`:** it can't build here; `reason` says whether the error is in the test. **`not-run`:** a timeout, no test
  found, a runner error, or the test file isn't on the branch (nothing is run, so a same-named test elsewhere can't pass for it).
- A proof with no `shows` (before D95) compares the whole message without its stack lines.
- **Also:** `testChanged` when the test was edited after the proof (updating it is the normal answer to `not-compiled`, and a
  pass then isn't on the test that was reproduced; recorded, not blocked); the reproduction test is listed in `newFailures`
  only when it passes alone and fails with the rest, since otherwise it's the result itself; a failed build skips the full
  test run; `othersChecked` is false when no full run judged the other tests, as on Playwright, which has none. Optional fields in `bug-proof` v1, as D95's; nothing outside Phase 3 reads it yet.
- **D79's bar isn't moved:** only `passed`, by the proof's own test on the answer key's fix commit, counts (a hand-run
  `--verify` elsewhere or on an edited test doesn't).
  Everything but `same-failure` is reported as a range beside it (`shareIfUnclearReal`). A test that passes while others
  newly fail isn't counted as a pass, which is stricter than the bar's wording; no saved proof has it.
- **Re-scored on the saved run, no AI:** 17 of 22 pass (77%), up to 19 of 22 (86%): same-failure SONB-1, 11, 14;
  different-failure SONB-30; not-compiled SONB-2. The bar stays failed.
Rejected: an AI judging the failure on the fix (nothing could check it, D18); checking the test runs the lines the fix changed
(SONB-26's false pass goes through the fix's own new early return, so it wouldn't catch it, and a proof has no fix to compare
with); counting the unclear ones as real (moves the bar after seeing a result).
**Why:** a person can't act on a bare "failed", and the score shouldn't hide what's behind it.
**Cost:** code describes what happened, not why. SONB-14 reads the same as SONB-1 and 11, and SONB-26 stays a false pass, so
a pass can still be for the wrong reason and a person reads the rest. A `shows` holding a value that changes per run (a time, a
temp path) reads as `different-failure`; a very general one reads as `same-failure`. With bare test names (Sonarr's reports),
two fixtures' tests with the same method name can't be told apart: an already-failing one dropped from the baseline can read as
`new-failures`, and a newly failing one that shares a baseline test's name is missed. No saved baseline has either (all empty).

## D106 · "Not reproduced" stops saying "don't fix" and says why — 2026-11-02 (built; not yet run)
Changes D75. In the saved run, 7 of the 9 not-reproduced tickets are real bugs with real fixes, and 8 of the 9 only hit the
cap (6 of those through D104's bug); only SONB-16 ran a test that passed. So "don't fix yet" mostly held back real bugs, and a
questions draft had to tell the reporter what was tried and ask for more when the tool had only run out. D79's own fallback
applies only when "proof finds bugs" fails, and it passed (73%), so this is the owner's call (owner, "ur call, just go ahead").
- **Why, by code:** `noProof` on the attempt: `ran-out`, `test-passed` or `flaky`. Outcome values are unchanged; a record
  from before reads it from its reason. Optional field, `bug-proof` v1.
- **Phase 3 says "no proof yet, the developer decides".** **Phase 4's rule stands:** an agent waits for a person's go,
  since with no failing test it has nothing to check its own fix against. That's a limit on the agent, not the proof blocking a
  fix, which D79's advice-only forbids: a person's go always clears it. To confirm when Phase 4 is grilled.
- **Drafts:** a questions draft must cover the attempt only if a test ran. For `ran-out`, the writer is told to leave it out and
  code refuses a claim linked to the proof, so an unlinked "we couldn't reproduce this" is left out (D97).
**Why:** a label that holds back real bugs and misleads reporters costs more than the 2 not-bugs it would have caught.
**Cost:** a developer may fix a wrong report with no warning from the proof; the Phase 4 rule is the guard for agents.

## D107 · The finder's reason is a guess: labelled for people, hidden from the proof — 2026-11-02 (built; not yet run)
The owner objected to the finder naming a cause at the start. It only reads code (Read, Grep, Glob) and nothing has run, so its
one-line `why` ("recursing infinitely") is a guess no code checks, yet it went into the bundle and into the reproducer's input.
- **The reproducer's input leaves it out;** the files, quotes and lines stay. **People see it as "The finder's guess, not
  checked:"** in `bundle.md`. `bundle.json` keeps it. No finder re-run: `file-finder.md` is unchanged.
- **Root cause isn't Phase 3's job.** A developer reproduces, then traces. A cause found before anything runs has no checker
  (D18). The evidence comes in Phase 4, when a fix turns the failing test green.
Rejected: a root-cause step at the start (the same guess in more words); dropping `why` entirely (it's useful reading once
labelled); recording what a failing test ran as evidence (left for Phase 4).
**Why:** a test should check what the ticket reports; SONB-1's bad proof tested exactly this kind of guess.
**Cost:** probably small: SONB-1 and 11 tested the wrong thing with no finder at all, so the agent can still guess on its
own. It can't be measured: no finder-era proof ran with the reason visible. Only the input leaves it out: the reproducer may
Read any file, so it could open `bundle.md` (none of 46 saved transcripts read such a file).

## D108 · `ticket post` refuses a draft whose inputs changed — 2026-11-02 (built; no AI needed)
`post` never compared a draft with what it was made from, so a draft saying "not confirmed" could be posted after a developer
confirmed. It now recomputes the draft's input hash: changed (the ticket, the bundle, the proof, the writer's instructions), or
impossible to work out, gives `draft_stale`, unless `--stale`. It's the only step that writes to Jira. The error gives the exact
command to make it again (kind, tone, PR). A relative range such as `HEAD~1..HEAD` moves with a later commit, so pass fixed commits; the error says so when it repeats one.
Rejected: a warning only (the output is JSON, easily missed); a word filter for "don't call it a bug" (stays an instruction).
**Why:** a person reads the draft, but can't see that its facts moved.
**Cost:** posting loads the graph and pages to recompute, and an instructions change makes every saved draft stale.

## D109 · Phase 3 closes on what we have; the proof ships as advice only — 2026-11-02 (owner: "do whatever we have for now, and get ready for p4, ur call, just go ahead")
D79 already says what follows when the proof's bars fail, so they aren't a reason to stay in Phase 3, and more Phase 3 work can't
show whether the bundle helps anyone fix a bug. Only a fix agent can.
- **No proof re-run now:** the report uses the saved numbers and says they predate the finder, D95, D104, D106 and D107.
  The re-run (at most about $45: the $40 cap plus about $4 of drafts; Sonarr only) is owed after 2c, before Phase 4 is built, on the owner's OK.
- **Moved to Phase 4:** the graph gaps (`IExecute<T>` handlers, a hub made by test callers, code dispatched by interface or name,
  framework-run code). No Phase 3 bar depends on them. **After the re-run:** SONB-17 offered only Playwright; SONB-16 passing
  alone; setup problems counted against the AI (SONB-10, 18, 28, 46).
- **D99 comes first, before Phase 4 (owner: "before"), as its own phase, 2c, on the roadmap.** I recommended after Phase 4's
  first experiment; the owner chose before: it's core architecture, and Phase 4's agents should check their work against
  requirements from the start. It's still scored on the planted FSD's key and can't move the proof bars on Sonarr.
- **Dropped:** the finder naming more files (no gain on Sonarr; the 4 misses are 2 choices of where to fix, 1 that can't be hit
  and 1 real one). **Not run:** bitwarden's proof (D100) and a private repo: rates stay public-code only.
**Why:** the phase's job is a checked starting position; what's left either needs Phase 4 or doesn't change a bar.
**Cost:** Phase 3 closes with two bars failed and its proof numbers out of date; a person makes every bug-or-not call until D99,
and Phase 4 starts later.

## D110 · The core stays C#; no port to Python — 2026-11-03 (owner: "Stay on C#")
The owner asked whether the core should run from Python while the analysed code stays C#. Reconfirms D9 and D23.
- **Roslyn stays C# either way** (10 files, 1,884 lines, 6 of the 8 detector files), so a Python core means Python, C# and Node
  on every developer machine, not one language swapped for another.
- **Measured, the reasons to move don't hold:** real commands take about 150 ms (debug build); a self-contained macOS arm64 build
  ran `req sections` with no .NET on the machine (139 MB; building the graph still needs the SDK; Windows untested); the Word
  importer's gaps are elements it doesn't handle, not limits of C#.
- **No failing bar traces to the language.**
- **A new product may still use its natural language through the CLI (D9, D23).** Spec and Tickets are in-process libraries today,
  so moving either out would need a CLI boundary first.
- The untested bash drivers (752 lines) are a real weakness, but not a reason to change language. How to fix them isn't decided here.
Rejected: a full port; Python for everything but the Roslyn parts (three runtimes, and D23's "two copies drift" unless the
boundary is clean).
**Why:** a port costs re-verification, and input hashes are built from C#-generated text (`Bundle.cs`), so saved AI outputs would
need byte-identical text or be paid for again. Done during the overhaul, a score change couldn't be traced to the port or the redesign.
**Cost:** contributing to the core still needs .NET (D23); a non-developer install is a large self-contained build.
Reopen only if a measured failure traces to the language, and then as a port-only step with identical hashes as its bar.

## D111 · Stangent imports PDF and Excel as well as Word — 2026-11-03 (owner: "i want our stangent able to handle pdf and xlsx also"; PDF built 2026-11-04, uncommitted; Excel not built)
Changes "`.docx` only" (D15's deferral, 2b) and "a converter when a real FSD or ticket needs one" (D72).
- **The same rules as Word (D13, D16, D18):** code only, no AI; the same file gives the same text; the original is archived; what can't
  be brought in exactly is listed, never dropped silently.
- **A PDF's sections (owner, 2026-11-03, "okay"):** bookmarks first; otherwise the printed contents page, each entry checked by code
  against the page it points to; otherwise, or when entries fail, the import stops and explains, and `--flat` imports the whole
  document as one labelled section. Headings are never guessed from fonts. Measured with Python tools on `Documentation.pdf`: all 340
  bookmark titles are on their pages; all 163 printed entries with page numbers check out, but the printed list has 167 entries against
  340 bookmarks, so sections from it are coarser. A title on a page doesn't prove it's the heading there.
- **Tables and the library (owner, 2026-11-03, "build it"):** PdfPig reads the PDF; our own table reader builds cells from the drawn
  lines. A code check requires every cell word for word in the page's own text; a table that fails is kept as plain text and listed as
  "not brought in exactly". Implemented by an agent on Opus 5.5 at medium effort (owner).
- **PDF as built (2026-11-04), read directly with no conversion to Word; Stangent still writes its own text (`document.md`), which quotes are matched against:**
  - **Text:** a false sentence is worse than a broken one, and a listed problem is better than either. So it joins across a page only on strong
    evidence, keeps text in other directions apart, removes only text never painted (render mode 3, off-page; an OCR layer stays, listed), and
    keeps white text, listed. Running lines must be identical or follow the page number (Arabic or Roman, odd and even pages apart).
  - **Tables:** only ruled grids become tables. Fill-only rectangles are shading, not rules. A word crossing a cell boundary or sitting in an open cell fails the
    table, which then stays plain text and is listed. Order is checked by position against the content stream.
  - **Errors:** any encryption gives `pdf_locked`, even with no password needed. Also `invalid_pdf`, `invalid_docx`, `no_text`, and `no_structure` for a PDF or a `.docx`
    with no headings (`--flat` overrides); the `.docx` rule is the assistant's call, not asked of the owner. `source-document` stays v1.
  - **Measured on `Documentation.pdf`:** 340 sections, 321 tables, 0 fell back, 99.93% of words kept against `pdfplumber`, 77 problems; 718 tests pass; a Word document
    that already imported gives byte-identical text (Sonarr's FSD, run 2026-11-04). The "encryp" / "t" cause: PdfPig's glyph box is wrong for some letters, so positions come from baselines.
  - **Checked by:** four fresh agents with fixes between. A fifth, on the last four small fixes, was cut off by a usage limit; those rest on the tests and the re-import above.
  - **Known gaps (`docs/spec.md`):** all checks rest on PdfPig's own letters; borderless tables; a table row drawn apart loses its columns, its words stay in order (39 such rows,
    1.6% of the words); two columns drawn row by row; a capitalised sentence over a page; a body-size footnote can join a next page (joins are listed); near-white text; a 100,000-level
    page tree kills the process (hostile file, nothing written); images unread. An earlier check judged all 13 "possible table" listings on this file false: noise, not lost text.
- **To grill before building Excel:** hidden sheets, formulas and merged cells; whether ticket attachments (D72) use the same converters.
- **C# libraries, first measured on `Documentation.pdf` against Python's `pypdf` and `pdfplumber` (2026-11-03, scratch code, not fresh-checked):**
  PdfPig 0.1.14 (Apache-2.0) gave identical bookmarks (340), the same word counts but for one word in 111,971, every sentence of 6+ words on
  table-free pages word for word (2,024, headers and footers stripped), the same 379 images, identical output on two runs, about 3 s; it
  reports no bookmarks and refuses a locked file with its own exception. **Tables aren't solved:** Tabula 1.0.1 drops letters ("tweet"
  became "twet"); a grid built from the drawn lines found 330 tables of 2×2 or more against 329, but only 70% of cells of 4+ words match, a
  page border reads as one cell, and a letter at a line's end can land in the next column ("encryp" / "t"; traced, see below). The
  Python result is a comparison, not proven right.
- **Test files:** the owner's `Documentation.pdf` (D56) for PDF. No real Excel file yet.
**Why:** the owner's documents come in these formats.
**Cost:** a PDF holds no reliable structure, so its import is weaker than Word's and quote checks depend on extraction; at least one
new dependency; two more formats to keep byte-identical across versions.
**Note, 2026-11-04 (D121):** Word and PDF now fill in one set of import steps (`IImportFormat`), which Excel fills in next, and ticket attachments use it. What D72 left open about attachments is settled there.

**Note, 2026-11-04 (D123):** this answers what was left open here about Excel. Sections are sheets, values are what's stored, and hidden, merged, formula and comment cases are settled as D123 and `docs/spec.md` say, by the answers §8 gave and D120 took up. It's been run on hand-built files only: no real Excel FSD has been seen, so D56's bar for real documents still stands.

## D112 · The overhaul's order: foundation first, stopping before Phase 4 — 2026-11-04 (assistant's call, owner asleep; owner: "continue from the foundation again")
The order is in `roadmap.md`, "The overhaul: order of work", and it replaces D109's "next" list until the owner changes it.
- **Foundation before features,** because every later item in `improvements.md` needs the same evidence shape and the same rule for changing state; doing them per product means doing them twice.
- **Challenged from the handoff:** evidence as its own `evidence` contract, not a bump of `artifact-base`. Only `ArtifactStore` uses `artifact-base` today; the products write their own files against their own schemas, so a bump would mark every phase `needs review` (D20) and change nothing they do. Consumers move when each is rebuilt (2.x).
- **Also challenged:** "27 status writes across 14 files" counts result fields (`imported`, `found`, `retry`) as well as lifecycles. Only real lifecycles go on the state module; result fields stay. The agent building it lists which is which.
- **Driver tests stay xUnit,** with fake `claude` and `stangent` on the path: `bats` isn't installed, and D110 leaves the drivers in bash. Moving their logic into C# is a later choice.
**Why:** the owner handed over the roadmap's order; all of this is reversible.
**Cost:** Phase 3's proof re-run and 2c's scored run wait longer, so the numbers stay stale.

## D113 · The foundation's shapes: evidence, permanent ids, the client-data check — 2026-11-04 (assistant's call, owner asleep)
- **Evidence is its own contract, `evidence` v1** (D112): `id`, `citation` (`kind`: `code`, `document`, `page`, `test`, `url`; `ref`; optional `lines`, `symbol`, `quote`), `commit` (required for `code` and `test`), `contentHash` (required for `document`, in place of a commit), `strength`, `checkedBy` (each `{kind: code|agent|person, name, inputsHash?}`).
- **Strength has four values:** `ran` (a test or run shows it), `stated` (a person or the FSD said it), `read` (read in the code only), `guess`. They join the list's "backed by a test, by code only, or weak" with the open proposal to label context by source. Severity stays separate (2.4).
- **Rules code holds beyond the schema (D18):** `lines.start <= lines.end`; `checkedBy` is empty only for a `guess`; `ran` needs a checker of kind `code` or `person`, because an agent saying a test passed isn't a test passing.
- **Permanent ids: `PermanentIds`,** a ledger in a file the caller names. Per scope, one format holding one number (`R-{0:D4}`), a counter that only goes up, and a list of removed ids. Every call takes a lock, so parallel drivers can't get the same id. A damaged file stops the call rather than starting over. Nothing moves onto it yet; requirements' `ids.yaml` has the same counter shape, so it can move in 2.3.
- **The data check: `stangent data check`,** not the roadmap's `check client-data`, to match `phases check`. It flags tracked documents unless `.stangent-data-allow` (committed, exact paths, a reason per entry) lets them through, and lines holding a term from a file outside the repo. Its output holds positions only, never a term or the text around it; a path holding a term is hidden too. No terms file: `termsChecked: false`, and the documents part still decides. A file someone named must exist, so a typo can't pass having checked nothing.
**Why:** every later item needs one proof shape and one id rule; building them per product means doing them twice. Client data has to be stopped before the first real client document, and a check that prints what it found would be its own leak.
**Cost:** nothing uses evidence or the ledger until 2.3 and 2.4, so until then they're contracts with tests and no consumers. The data check reads the current tree, not git history, doesn't look inside documents, and in CI has no terms unless the owner adds a secret; a finding names only a position, so a person has to open the file to see what matched.

## D114 · One module for state changes: which things are lifecycles, and where history lives — 2026-11-04 (assistant's call, owner asleep)
- **A lifecycle** is a stored thing moving between states over time, with code or a person deciding. Eleven are on the module: `requirement`, `question`, `trace-decision`, `proof`, `proof-confirmation`, `draft-posted`, and the AI runs' stages (`page-run`, `link-run`, `req-run`, `ticket-match`, `ticket-step`). What one command returned (`imported`, `retry`, `recorded`, an attempt's outcome, the finder's answer) stays a plain field. The sorted table is in `phases/0-foundation.md`.
- **The table holds who may move:** a person approves, decides, confirms and posts; code starts items, drops back and runs steps; no table lets an agent move anything (D18, D60). `decided_by` still comes from git (D58). The history's `who` for `trace approve` is git's user name, and `decided_by` is unchanged by it.
- **History sits beside the state,** append-only JSONL. Committed state has committed history: `specs/requirements/history.jsonl`, `specs/trace-history.jsonl`, the latter in CI's bot commit. Run state's is in `.stangent/`, ignored. Nothing hashes it, and no artifact holds it. A move to the same state adds no line unless the table lists it.
- **A person's YAML edit** is checked and recorded when `trace attribute` sees it, because the file is the contract (D58) and the edit happens outside stangent.
- **Enforced by a test** that scans `src/**/*.cs` for writes of the lifecycle fields; each exception is listed with its reason and count.
- **Requirement ids moved onto the permanent-id ledger now,** not in 2.3 as D113 said: `NextId` had no lock, and numbering stays the same. `ids.yaml` becomes `ids.json`, read only by `RequirementFile`; an old file is taken over on first use.
**Why:** a lifecycle's rules were spread through the code that happened to write it, with nothing to read back. One table per lifecycle can be read in a minute, and a test keeps new writes on it. Keeping result fields off it stops the tables filling with values nothing moves between.
**Cost:** `proof` and `ticket-step` tables allow nearly everything, because real code does; they add a history and little refusal. History is written just before the file is saved, so a crash can leave a line for an unsaved move. Committed history files can conflict across branches. Four moves are allowed and questionable, listed in the phase file for the owner.

## D115 · Line citations and Playwright as evidence: the read paths, and lines kept out of the walk — 2026-11-04 (assistant's call, owner asleep)
- **Line citations (1.1):** every point already stored its file and lines, so nothing was rebuilt. `SymbolLines.Where` and `stangent graph where <symbol>` read them, and shape the answer to fill an `evidence` citation. Overloads all come back. A missing line stays null.
- **A dirty tree says so.** A build records the files on the map that differ from its commit. A symbol in one of them has `linesMatchCommit: false` and no citation; a symbol in an untouched file still cites, because its lines are right.
- **Playwright as evidence (1.2):** a `goto` whose whole URL is a string and matches a screen's route is a 🟡 `opens-screen` line from the `playwright-specs` detector, read by `stangent graph screen-tests`. Anything else is ❓, never a guess. Strength is `read`; only a real run makes it `ran`.
- **Kept out of the walk, and out of every other reader:** not edges with a kind the walk skips, but `CodeGraph.ScreenTests`, apart like 📜. Scoring, the splitter, reference pages and ticket starting points read `AllNodes` and `AllEdges` directly, so skipping a kind in the walk alone wouldn't have protected them. An impact answer doesn't list them yet.
- **`code-graph` went to v4; the file layout stayed 3.** Only optional `meta` values were added, so a clean repo with no specs exports the same bytes apart from build fingerprints. Checked against the code before these slices on the shop fixture and on Sonarr (228,617 export lines the same; its `global.json` removed in a scratch copy, for lack of the SDK it pins).
- **One finder.** `PlaywrightSpecs` holds it; bug proof calls it and keeps its looser read of `goto`, so its results don't change.
**Why:** 2.3 needs `repo/path:lines`, and a citation that doesn't match its commit is worse than none. Refusing per file keeps a stray edit from blocking every citation, and leaving off-map files out keeps a lock file the build's own restore wrote from marking the tree dirty, and so keeps a clean repo's export bytes the same.
An AI step can trust a line only if a test really opens that screen, and a guessed route would be worse than a blind spot. Measured on three public repos (`phases/1b-frontend-api.md`): 123 of 249 `goto`s resolved, 10 of 10 hand-checked links right.
*Changed 2026-11-04 (fix round after the fresh check):* an export always writes `dirtyFiles` and the new `notInCommit`, because an export that says nothing can't be told from a clean one, so a clean tree's export no longer has the old bytes; old exports still load, as don't know, and cite nothing. A file the commit doesn't contain cites nothing. A spec inside a frontend links only to it, and one outside links only when exactly one frontend matches. A percent-encoded path and an unreadable spec are ❓. Re-measured on the same three repos: the same 123 of 249.
**Cost:** a partial type answers with its first part, plus the files its other members are in, and a part with no members isn't seen. About half of `goto`s stay ❓ (mostly built URLs), and a link says where a test lands first, not what it goes on to click. Seven phases go to `needs review`, though none reads the new values.

## D116 · Word stops on tracked changes, and a code check compares every word — 2026-11-04 (assistant's call, owner asleep)
Follows D52 and D111's rule: what can't be brought in exactly is listed, never dropped silently.
- **Tracked changes stop the import** with `docx_tracked_changes` and a count, because the document isn't settled. `--accept-changes` imports the accepted view, inserted text kept, deleted text gone, moved text once, and lists how many were accepted. Format changes don't stop it: they change no words.
- **Hidden text is left out and listed, but a hidden paragraph mark alone hides no words.** Reconsidered from the brief: a mark with `w:vanish` hides only the line break, and Word's style-separator headings use exactly that, so leaving such text out would drop words the reader sees. It's kept and listed.
- **The completeness check (D18) runs on every import.** It reads every word apart from the converter, by element name in any namespace; per section, those words minus what a rule removed, plus what the converter adds on purpose, must equal the words rendered. A mismatch is listed, never fatal.
- **`source-document` stays v1.** Everything is a `problems` line. Comments have a fixed line shape and a parser, so 2.5 can turn them into questions without a new field.
- **An `mc:Fallback` is skipped only with an `mc:Choice` beside it (fix round after the fresh check).** With none it's the only copy, so the converter and the check both read it.
- **A ticket attachment never stops the pull (same round; D72).** No headings reads flat; one the converter can't take is listed with `notConverted` in `ticket.json`, left out of the bundle so its contract doesn't change.
- **Added beyond the brief because the same principle needs them:** struck-through text is listed, page headers and footers get a word count, a content control's prompt is left out, rows and cells inside a content control are read, and a row starting part-way across keeps its columns.
**Why:** a tracked document silently resolved is a false record of what was agreed, and moved text used to appear twice. A list of known cases can't cover the element nobody thought of; a word count can.
**Cost:** a document mid-review can't be imported without a flag or a trip to Word. The check adds a pass per block, and sees only text in the file's main text-bearing elements. It's proven on hand-built files and Sonarr's script-written FSD, with no file saved by Word itself in the repo, so a real one may flag something new, and that's a line to read rather than a failure. Footnotes lose their place in the sentence, and keep only the words before them in their label. A merge across columns still leaves blanks while one down rows repeats, kept so as not to change any imported text.

## D117 · Sections get a permanent id beside their number, and only exact signals match across versions — 2026-11-04 (assistant's call, owner asleep)
Follows D15, D58 and D99, and D20 for the contract.
- **The permanent id goes beside the old id, not instead of it.** `permanentId` (`S-0001`) comes from a ledger per document. `id` stays the number or heading path, because input hashes and link ids are built from it: changing it would make every saved paid answer look stale. Measured on Sonarr's FSD against the build before: `document.md` byte-identical, `document.json` the same once the ids are taken off, all 15 requirement hashes the same.
- **A document from before gets its ids on its next import**, the same file included, which stays `unchanged`. A read never writes.
- **A changed file is a candidate.** It waits beside the current version, with `SECTION_DIFF.md` and a JSON form. Only a person promotes, and `decided_by` comes from git.
- **Exact signals only, with no score or threshold:** the same body and heading path is unchanged; the same body, unique on both sides, is moved or renamed; the same heading path, unique on both sides, is modified. Whatever's left that shares a whole sentence of at least 4 words with any section on the other side is doubtful, and so is whatever's left with the same heading path as a section left there (fix round: a rewrite under a repeated heading otherwise set its id aside silently). The rest is added or removed.
- **Promotion carries work by code.** References follow a section that kept its id, in the stored files as they are. A run's hash is worked out again only when it was valid and the words are the same but the number. Items on a removed section stay on file under a mark and are listed. That includes trace entries (fix round): by number alone they read as the new section with that number and lost their decision. A marked entry keeps its decision and `decided_by` until a person deals with it, and `trace check` flags it. The mark is a value of the existing `section` string, so `trace-links` stays v2. `fsd discard` lets a person throw a candidate away.
- **Added beyond the brief because the design needs them:** `resolve --removed`, since in a merge a current section has no candidate section to settle it from; and a base-file lookup by link, since a moved entry's new id would otherwise read as a typed `decided_by`.
**Why:** a new chapter changes what `1` means, and a wrong match would carry an approval to the wrong rule, silently. A person reading one doubtful line costs far less, and rules with no numbers can't be tuned to the document in hand.
**Cost:** a split or merge always takes a person. So does a heavily edited section that kept a sentence, and a repeated sentence adds noise. A renamed section's runs go again, and a renumbered section's finder too. Links still cite numbers, so the two ids live side by side until a paid re-run is due. It's proven only on a planted edit, not a real version change (D56).
**Changed, 2026-11-04 (what the whole-flow test found, owner's go-ahead):** two things promotion got wrong, both fixed by the rule "a move isn't a change". **History follows a move:** an `X-` id was already kept across a move, as D118 says, and only the entry id changed, so the history under it looked empty. An `id-moved` line now connects old to new, with no old line rewritten, and `state history` reads through it and takes an `X-` id. **A typed chapter number stays a number across a gap,** since a removed chapter is what a new version does and the rule was there for `2023 Roadmap` and `24 hour clock`. It's read as words when more than 10 past the last chapter, followed by a lower-case word, at or above a later chapter's number, or first with no next number after it. Sonarr's FSD imports to the same bytes and ids. **Cost:** 10 is a number chosen by judgement, the only one in these rules: taking out more than ten chapters at once loses their numbers, and `12 Angry Men` after chapter 9 reads as a number. `1 Intro` then `3 Ways to pay` reads as a number, because no rule can tell it from `3 Checkout`; `problems` says one is skipped. A plain chapter beside it doesn't stop it.

## D118 · What counts as the same statement, where line citations live, and a severity scale a person sets — 2026-11-04 (assistant's call, owner asleep)
Follows D53, D54, D58, D62, D68 and D117's rule of exact signals, and D18 and D20. The hard bar: saved paid work stays valid.
- **The same statement** is the same words after normalising whitespace, in any section; or a rephrase with exactly the same cited symbols, in the same section, alone on both sides among what the first rule left. Anything else gets a new id, and the old one is retired for good. No score and no threshold.
- **Today's numbers stay.** A page's ids at adoption become permanent, so nothing cited moves. A writer's output still has none, and it's numbered by place while a run is under way: the prompts and the verifier's input don't change. Ids go on when a page is recorded.
- **Citations live beside the page,** in `<path>.lines.json`, not in its JSON. The link judge's input is built from page text, and the writer sees the previous page whole, so a file nothing reads for a hash is the safe place. `reference-page` keeps its shape and stays v1. `reference-lines` v1 is new.
- **A dirty file writes no citation, per file,** on `graph where`'s own answer. A clean file's lines at the commit are still right, so it keeps its citation. A dirty or untracked file says why it has none. A graph with no commit, or too old to say whether it was clean, still stops the whole page. A partial type's lines carry its note into the lines file, the evidence and the page, because they cover its first part only. *(Corrected after the fresh check: this first said any dirty file stopped all citations.)*
- **Severity has one scale,** `Severity` in Core: `blocker`, `major`, `minor`, `cosmetic`, starting `ungraded`. **A person sets it,** in the PR path decisions use, because the scale is the effect on users and code can't judge that. Its moves go on the state module, and no table lets code or an agent grade anything. Who set it is `severity_by`, filled from git like `decided_by`, not `decided_by` itself: a decision's drop-back clears that field, and a grade shouldn't go with it.
- **`trace check` issues a finding's id and severity, even without `--fix`,** because that's where they belong and a plain check is what runs first on an old file. They're additive, so `trace-links` v3 reads every old file. A build before this one can't read a v3 file.
- **An `evidence` record per thing cited,** because the contract holds one citation and a finding rests on two ends and the code behind them.
- **A decision rests on the words it was made on (corrected after the fresh check).** A rephrase can keep its id and even keep a quote while reversing what the statement says: "Orders over 100 are refused" is still inside "Orders over 100 are refused is never done; any order is accepted." So `decided_on`, beside the decision, holds the hash of the statement's normalised text. If the text changes, the decision drops back on the state module and `trace check` reports it in `changedStatements`. It's written when a decision is made by `trace approve`, at `trace attribute` after a merge, and otherwise at the first check, which is how an old file gets one, dropping nothing. A re-judged link carries it, and the drop-back clears it. A person changing it by hand is a problem against the base. No input hash reads it.
- **Evidence is code's, and it's derived again on every check (corrected after the fresh check).** It follows the page and the document, and it's byte-stable when nothing changed. A record that's neither what code derives now nor what code wrote before is refused by name, and `--fix` doesn't launder it: delete it, and code writes what's true. "Wrote before" means the base's copy when the check has a base, and otherwise `evidence_hash`, written beside the evidence. A person on a record comes only from `decided_by`. Nobody adds evidence by hand: no kind needs it, and `ran` waits for something that really runs, so no such route exists. `evidence` v1, `reference-lines` v1 and `trace-links` v3 grew optional fields in place, with no version bump, because none has reached `main`.
**Why:** links have to outlive a re-run, and every paid page and link must stay valid. Exact signals can't move a decision to the wrong statement, and a decision can't outlive the words it was made on, even when a quote still fits and the id stays. A number on a finding, its proof and a grade from a person let someone sort a long list and trust what's on it, with none of it reaching a prompt.
**Cost:** a rephrase that also changes a symbol, or has a rival in its section, gets a new id. A whitespace-only change keeps a decision, and any other wording change drops it, even one that means the same, so a person decides again. An old file's first hash is taken from the text at that moment, so a rephrase that landed before then isn't caught. Without a base, someone who works out `evidence_hash` for a typed record gets one pass, until the next check replaces it; CI's PR check has a base. Evidence written before `evidence_hash` that no longer matches what code derives is refused once, until deleted. Two more files per page. Every finding starts `ungraded`, and until a person grades it, or 2.5 suggests a grade, the scale sorts nothing. A plain `trace check` can change the file it checks; in CI that's thrown away, and only the bot's `--fix` keeps it. Two branches issuing ids can clash in the ledger, and git shows it.

## D119 · A second agent tries to disprove each finding; verdicts route them; the BA can say the page is wrong — 2026-11-04 (assistant's call, owner asleep)
Reverses D62's "rejected: a second check on every finding". Follows D18, D53, D58, D60, D61, D66, D68 and D118. 2c's links failed both bars, 24 of 30 and 44% false alarms, and all 12 hand-checked false alarms were refuted from a code line. D62 assumed a person going through findings one by one was the check; at this rate, that person mostly reads wrong ones.
**It isn't shown to help until the owner's paid re-score runs.** Nothing here is measured. It's off by default, and D68's finding stays: findings ship off.
- **The step:** `fsd disprove prepare|check <doc> <section>`, one call per finding, one turn, no tools, structured output, `check` by code. `fsd.disprove` turns it on, set like `findings`, default off. Not a flag, because a flag would have to be repeated on each run and a setting is what D68 chose. No link's input hash reads it.
- **The bundle is code's,** since an agent with no tools sees only what's put in: the claim in a fixed sentence, the section, the page's statements, up to 60,000 characters of whole symbols at numbered lines, what couldn't be shown and why, and the entry points the impact walk reaches. The finder, its note and the first check stay out (D61). The code goes beyond the cited symbols because a gap cites none, and across the API because a screen's slice stops there (D63).
- **The verdicts:** `refuted`, `stands`, `page-gap`, `unreachable`, `cant-tell`. All but the last need a code quote, and code finds it word for word at its `path:lines` in the bundle. One retry, then `cant-tell`: a verdict code can't check is no verdict (D18).
- **The routes:** `stands` stays with the BA, marked code-confirmed, `read` and never `ran`. `refuted` and `unreachable` leave the BA's queue, kept and listed with the quote. `page-gap` goes to developers in its own group. `cant-tell` stays with the BA, labelled. A person's decision outranks all of them, and is never tested again.
- **Only code moves a verdict,** on the state module (`finding-verdict`), and evidence derives from it as in D118. A changed bundle drops the verdict before the new call, so a closed finding reopens even if the call never comes. `disproof_hash` is there because a typed verdict could clear a finding out of a queue.
- **A severity suggestion stays a suggestion.** It's shown beside `ungraded`. D118's rule holds: a person sets it.
- **`page-wrong`** joins `decision` in `trace-links` v3, unshipped, so no bump, with `decided_by` from git. The page is stale for its owner until it's written again, and listed in `spec stale`. Writing it drops the decision back on the state module, so the BA looks again. That's the one case where a page runs with unchanged inputs, and what was reported goes after the hashed part of its bundle. The entry must have a page.
**Why:** an agent told to refute a claim from the code is a different question from a judge reading two texts, and every false alarm checked by hand fell to a code line. Sorting by who can act keeps a reference gap away from a BA who can't fix it. Saved work stays valid: every saved hash and prompt is unchanged, proved by test, and all 16 of Sonarr's real sections read `current`.
**Cost:** one call per testable finding, up to 100K characters each: on Sonarr 62 calls and an estimated $4 to $6 a run. **It can't raise what's found**, only remove, so it can't meet the 27-of-30 bar alone, and it can remove a real finding: a wrong `refuted` hides one from the BA, kept on file but out of the queue, and only the re-score's `plantedLost` will say how often. 23 of Sonarr's 90 findings, `undocumented` screens found by code, can't be tested. A verdict can be older than the code until the next run. With 44 of 62 bundles at the code limit, a refuting line can be out of sight. A regenerated page that comes out the same still sends the finding back to the BA.
**Changed, 2026-11-04 (the fresh check of 2.5 and 2.6):** `refuted` and `unreachable` no longer close a finding. The check showed why: a quote only proves the text exists, so `refuted` quoting `return false;` against "Orders over 50 need approval." took the finding out of the BA's queue, and code can't judge relevance. By D18, a person approves what code can't check, so both now go to `developer-confirm`, listed with the verdict, the quote and its `path:lines`, and close only on `stangent trace confirm`, with the name from git and the move on the state module (`finding-review`). `trace send-back` returns one to the BA. No word-overlap or minimum-length rule: a tuned number wouldn't solve this. `stands` and `page-gap` stay as they were, labelled "the AI's reading" rather than code-confirmed, with evidence `read`. A dropped decision now drops the verdict's effect too, so the finding goes to the BA with a label, with the step on or off. **Cost:** every refutation now needs a developer to read one quote, so the step moves that reading from the BA to someone who can judge code rather than removing it, and how many there are is unknown until the re-score. The scorer counts `developer-confirm` as out of the BA's queue, and as lost if it's on the key.
**Changed, 2026-11-04 (what the whole-flow test found, owner's go-ahead):** the bundle's hash no longer covers its whole text. It holds what a verdict rests on: the instructions, the document, the section's title and text, the claim, the page's statements, each symbol's path, name and code, what couldn't be shown and the entry points. The commit, the section's number and the line numbers are still shown, and left out. Before, a commit and a `graph build` with no code changed dropped every verdict, and a section that only moved lost its verdict and a person's confirmation, both at the price of a paid call. Line numbers go too, by the same rule: code two lines lower with the same words means the same. No disprove run has ever happened, so no paid answer is lost. No link, requirement or page hash or prompt moved. **Cost:** a verdict's `path:lines` can be older than today's code, so they're read with the commit they were given at, on the evidence and listed as `atCommit`. A renamed file or section title still costs a call. The bundle's text and the hash are now two things kept in step by hand in `DisproveRun.Build`: something new shown has to be added to both, or it won't reopen a finding.

## D120 · Build first, then test one component at a time — 2026-11-04 (owner: "for now, idw to check, i wanna build and improve as much as we can, and then test it one portion at a time, see how they can link together, and see which component as a whole is damaging the process")
- **While building, D61's fresh check and fix round are paused** (owner). `dotnet test`, the whole-flow test and `phases check` still run on every build, since they cost almost nothing. Each build lists what it hasn't had checked, under "Checks owed" in `roadmap.md`.
- **Every component gets a switch,** so the test phase can run the flow with each one off and see which helps and which does harm. History is the first example: it adds about 2.5 points (1c) and can be switched off later (owner).
- **Bars and decisions stay as they are.** Nothing is called proven until its test. No paid run during the build. Anything that changes a prompt is built switched off, so saved AI work stays valid.
- *Assistant's calls, within the owner's order:* §8's answers to D111's Excel questions are used as written. The order is switches and seams first, then import, then Excel and attachments, then what's built switched off.
**Why:** a component that damages the flow only shows up when the whole thing can be run with and without it. Testing each piece as it's built doesn't show that.
**Cost:** defects stay in longer and may be built on. Tonight's fresh checks caught serious ones (a decision surviving a flipped statement, ids reissued), so the test phase will find more at once, and some builds may need redoing. Also, 24 of 30 and 44% stay unimproved until the test phase.

## D121 · Switches, structured import problems and one set of import steps — 2026-11-04 (assistant's call, under the owner's D120)
Follows D18, D20, D72, D111, D116 and D117.
- **Structured problems.** `source-document` v3: `problems` are objects with `kind`, `detail` and whatever else the line says. Converters keep writing lines and one reader takes them apart, because old documents hold lines and have to come out the same as new ones. `detail` keeps each line word for word. Rejected: changing some sixty places in the converters to write objects, which gives old and new documents two readers.
- **One set of import steps.** `IImportFormat`: safety checks, convert, comments, extract files, completeness check. A format without a step says so, with why. Tickets call the same pipeline. Word and PDF do comments and completeness inside convert, since they share its walk, and their steps report what it found: splitting that walk risked the bytes of `document.md`, for no gain until a format needs it. Safety opens the file one more time for that reason.
- **Links store a section's permanent id (S.1, `trace-links` v4).** `fsd.sectionId` beside the number, filled by code at `trace check`, so nothing typed survives. Nothing reads it for a hash or an entry id yet; it's there for what comes next.
- **The switch shape.** `components: { <name>: false }` for all of them, in one place. Older keys stay where they are and keep working, and `components` settles onto them when the file is read, so no code that reads them moved. A switch's state goes into no input hash. It's reported in the envelope beside `result`, not in it, because some results (`ticket bundle`) have a contract of their own. An off component leaves a word, never an empty: `off`, a mark on the page, or a warning. Rejected: renaming old keys, which breaks old files. Five things have no clean switch, listed in `docs/foundation.md`.
**Why:** a report or a question for the BA has to read problems by kind and section, and a fixed text shape can't be relied on from outside. A format missing a step should say so, or an empty answer reads as "none found".
**Cost:** a kind is read from a line's words, so rewording a line in a converter can move it to `other`. The tests pin each kind's shape. For Word and PDF, three of the five steps only report, so the interface is a promise for Excel more than a change for them. `verification.off` grew `reference-page` v1 in place rather than bumping it, because a new version number would change every page's bytes and the hash bar comes first. Pages recorded with a component off keep that until their inputs change, so a test run has to start clean. A detector's switch is global, and `components` winning over `detectors` means two places can disagree. Switching off isn't the same as a component's cost going away: nothing here has been run with a real AI.

## D122 · Verdicts, labels, first-import approval, `recheck` and `gone` — 2026-11-04 (assistant's call, under the owner's D120)
Follows D16, D18, D58, D111, D117, D118, D119 and D121.
- **The verdict.** `ok`, `ok-with-warnings` or `failed`, from the structured problems alone: any problem at all is a warning, and no kind counts for more, because which matters is a person's reading. `failed` means the file stopped, so nothing was written; it's in the error envelope's `result`, with the error's code unchanged. `completeness` lines stay warnings, because D116 says a mismatch never fails an import. `import-report.md` is made from `document.json`, and nothing reads it back. Rejected: a score, which would need a tuned number.
- **Labels stop by default.** A sensitivity label means someone restricted this file, and its text would go to AI steps and into a repo. `--allow-label` is a person's word, recorded with their name from git. The same goes for a ticket attachment, except it never stops the pull: it's listed as not converted, with why.
- **Reading again keeps what was asked.** `fsd reread` reuses the first reading's flat, accepted-changes and label answers, because the question is only whether the converter changed. It compares text and sections, not the file's hash. The same text with new problems updates them, with no candidate, as problems are in no hash.
- **First-import approval is off by default.** With it on, a first import waits in `candidate/` with no `document.json`, so nothing can read it. Stangent opens no PR. Off by default because a project with no BA approving things would only have one more command to run.
- **Tables and pictures are worked out, not converted.** `T-` and `F-` come from the text the conversion already gave. Nothing in the converters moved, so `document.md` can't change. Rejected: having the converters report them, which risks those bytes for no gain.
- **`fsd export` is read by nothing.** It's for people and tools outside stangent, into `.stangent/` by default, and a test holds that nothing in `src/` reads it, so a hand edit there can never change a result. `document.json` stays the record. Its limit, 1500 lines, is a stated setting, `fsd.exportMaxLines`, not something measured.
- **`accepted` and `not-a-difference`** go into `trace-links` v4 in place, because v4 is on this branch only, not on `main`. They read as any decision: no reader branches on a decision's value but `page-wrong`.
- **How `recheck` and `gone` fit D58 and D119: merged, nothing replaced.** D58's and D118's rule still drops the decision to null, on the state module, so every reader that asks "is it decided?" keeps its answer. D119's send-back still fires, so a verdict can't take over. `recheck` adds a second field beside that, `standing`, with the old decision in a note, for people to read and nothing to count. `gone` changes one thing only: `ApplyEntries` keeps a decided finding's entry, instead of removing it and retiring its id. Rejected: `recheck` as a decision value, because code would then be writing a decision, which D58 forbids, and every reader would have to learn it. Rejected: a finding that comes back taking its old decision again: the same quotes don't prove the same page, and D118 exists because a quote can survive a flipped statement.
- **"Evidence changed" means what a decision is tied to:** the quotes and the statement's text. Not the records in `evidence`, which change their commit with every regenerated page and would mark every finding at every build.
- **Only a decided finding is kept as `gone`.** An undecided one is removed as before (D118), or each re-judge's slightly different quotes would pile up. **Coverage reports and doesn't fail,** because a document mid-way through linking is normal, and a mark holds for the words it was made on, as a decision does.
**Why:** a report and a question for the BA need problems read by kind; a label or a lock needs a stop that says what to do; the reread is what lets 2.1's fixes reach earlier imports without a person losing saved work; and a person's decision, once made, shouldn't vanish from view because the words under it moved.
**Cost:** every problem weighs the same, so one resolved comment and thirty missing words both read `ok-with-warnings`, and the report's ordering is all that helps. A label held in another product's own property isn't seen. A kept table id across an edit rests on its place in the section, so two tables swapping and being edited at once would swap ids. A first import waiting isn't seen by anything but `fsd status`, so a forgotten one just sits there. "Comes back" means the same link word for word, so a judge rewording a quote looks like one finding gone and a new one, and the old stays listed for good. A confirmed review with no decision isn't kept when its finding goes; only a decision is. Turning `findings` off and re-judging marks decided findings `gone`, since the build no longer records them. `trace.yaml` grows with each `gone`, with nothing to clear them yet. `was` is plain text a hand edit can't change without a problem, though nothing checks it against the history. Coverage counts a section with any entry that has a page as linked, even one whose only entry is a conflict, so "linked" doesn't mean "agrees".

## D123 · Excel as a main document, files inside a document, attachments in the checks and `contentHash` — 2026-11-04 (assistant's call, under the owner's D120)
Follows D13, D16, D18, D20, D72, D111, D116, D117, D121 and D122. Answers D111's open questions on Excel.
- **A sheet is a section.** Each visible worksheet is a level-1 section with a permanent id, in workbook order, and holds one table. Nothing inside a sheet is read as a heading, even a cell reading "1 Introduction", as the brief says never to guess. Rejected: treating a one-column sheet as prose, which would be guessing what's a heading by another name.
- **Stored values, stated formats.** A number shows as stored, so `1.10` stays `1.10`. Built-in date formats give ISO dates. A custom format shows the raw number and is listed, including a custom date format, which then reads as a serial number: reading format strings would be a second renderer to get wrong. A formula shows its cached value, listed once per shared range, and one with no cached value is listed, never computed.
- **Left out and listed:** hidden and `veryHidden` sheets, hidden rows and columns, text under a merge. **Named, not read:** notes and threaded comments (as `comment` problems), charts, pictures, shapes, pivot tables, page headers, external links and macros, whose modules come from a small OLE reader that's new here. Sheet protection is listed; a password stops (`xlsx_locked`).
- **Shared workbooks stop.** Recorded revisions stop like tracked changes (`xlsx_tracked_changes`), because the cells already hold them all and there's no earlier state to read. `--accept-changes` reads the cells as saved and says so. With a history that shows no change there's no accepted state to compare, so it's listed instead. Each says which.
- **Large is flagged by stated settings,** `fsd.excel.largeSheets` (50) and `fsd.excel.largeRows` (100000), and still imports. A sheet spanning more than 20,000,000 cells stops, because writing every row of it out is the attack, not the content.
- **A completeness check reads the table back.** Each stored cell has to be at its place in the rendered text, or have a listed reason.
- **Files inside a document are saved, not imported.** Under `specs/fsd/<doc>/attachments/<A-id>/`, as ordered, linked by `attachments` and a generated `attachmentLines` in `document.json`. `document.md` and `text` stay byte-identical. Excel gives a CSV per sheet, line N being row N, so a citation's row can be read off it. Word and PDF go through the same steps, and a zip is unpacked and each file read. PowerPoint is kept and listed, as it isn't decided. **An embedded file never stops the import.** One exception to "kept": a file with a sensitivity label nobody allowed isn't copied into the repo at all (`withheld`), since that's what the label is for and the archived original still holds it.
- **Stated limits, each refused and listed:** path tricks and symbolic links in zip names, 10,000 entries, 256 MB a file, 1 GB an attachment, 500 times the packed size past 10 MB, 3 files deep. Bytes are counted as they unpack. None is measured on a real document; a real one should come nowhere near them.
- **A PDF's attached file has no section.** Rejected: giving it to the first section, which would be a guess that then reaches a check. The diff says when one changes.
- **The switch.** `fsd.attachmentsInChecks`, `attachments-in-checks`, off by default. Off, every bundle and hash is byte for byte as it was, with a test holding it. On, the link judge's, requirement judge's, agree checker's and disprove bundles show the text, and code's quote check accepts it. Code finds the source, so no AI's output schema changed. The agree checker sees it too, or it would be checking a link against words it can't see. **`fsd.attachmentChars`,** 40000, stated: over it the file stays out whole and reads "not checked" in the bundle and report. Rejected: part of a file, which reads as though the rest had been checked.
- **`ticket-excel-text`, off by default, is an addition to the brief.** The brief says `.xlsx` converts on tickets and that nothing may change what an AI sees at defaults, or an existing hash. A ticket's whole `ticket.json` is hashed (`ticketHash`) and its text reaches the file finder, so both hold only if a workbook's conversion stays out of it until asked for. Off, it's saved under `converted/` beside the attachment and `ticket.json` is unchanged. Docx and PDF text is as it was.
- **`contentHash` beside `textHash`.** Over the text and its attachments' hashes, equal to `textHash` with none. The same words with other files read `modified`, naming the file. A moved or renamed section keeps its outcome and says the same. An older document has none, and falls back on the text. Promotion with the switch on treats a changed file as changed words, so its section runs again; with it off, no run moves.
- **`source-document` v3 and `trace-links` v4 grew in place,** since neither is on `main`. **`evidence` went v1 → v2** (D20), as it's on `main`: a `document` citation can name `attachment`, `file`, `sheet` and `row`. Every v1 record is a valid v2 record, and no hash moves unless a citation holds them.
**Why:** an FSD can be a workbook, and real ones hide their rate tables in embedded sheets, where a changed fee in a sheet read as `unchanged` and no check could see it. Keeping the text out of `document.md` and the checks behind a switch is what lets this be built without touching a saved hash or paying for a re-run.
**Cost:** no real Excel FSD or Word-embedded workbook has been seen, so all of this rests on hand-built files; Excel's own output will have things these don't. A custom date format reads as a serial number. A very wide table is hard for a person to read. One problem line per formula can be long on a sheet with many unshared formulas. **`stangent data check` flags a committed `original/`** (`.xlsx`, `.docx`, `.pdf`), which is right, as these are the client's files: a team has to allow each or keep them out of git. D16 sends a main document's original to the archive and this keeps embedded ones in the repo, as ordered, which is a tension worth the owner's look. A hostile PDF nested in a document can still end the process as it opens, as a main one already could. Plain `.csv` and `.txt` inside a document are kept and not read. The "not checked" flag in the report is from the setting at import, so changing `fsd.attachmentChars` later leaves it stale until `fsd reread`; the bundle is always current. With the switch on, the candidate finder still doesn't see attachments, so a section naming a page only in its sheet won't find it. Only the fake AI has seen any of this.

## D124 · Table facts, the mapping file, finer verdicts, grouping and asking, all switched off — 2026-11-04 (assistant's call, under the owner's D120)
Follows D18, D20, D53, D57, D66, D68, D118, D119, D120, D121 and D122. None of it has been shown to help, and none has been run on a real AI or a real document.
- **Table facts are a second route to code (C.2), off and labelled, pending the owner's D68 question.** Everything else reaches code through pages. This compares a table's names and values with facts from the code, by code with no AI, under `fsd.compareTables`. Each result names its route. D68's question stays open: building it switched off answers nothing. The enums are in `.stangent/graph-enums.json`, read only with the switch on, so `graph export` is the same bytes on or off and `code-graph` stays v4. Rejected: `meta.enums` in the graph, which would change every consumer's contract for something nothing uses yet.
- **The mapping file.** `specs/fsd/<doc>/table-map.yaml`, contract `table-map` v1, written or approved by a person. A column's meaning can't be read off a table, and guessing would make this an AI step with no checker. A name matches in any case and nothing else, and any other difference is declared as an alias there. Rejected: fuzzy matching, which would need a tuned number. A `table-fact` finding has no page, doesn't enter a link bundle and isn't sent to the disprove step. Its FSD side stays `stated`, as every document citation is (D118), and its code side is `read`.
- **Finer verdicts (C.4), off.** `fsd.judgeFinerVerdicts` gives the judge new instruction text and a new schema, so nothing changes with it off. `partial` is a kind of finding with what's covered and what isn't, and both quotes. `unclear` is a question: the FSD's quote and a reason, no page, in the BA's queue with its own label and out of what counts as a difference. It gets an `X-` id, because a person's answer has to sit on something. **`impact-report` v5 is on `main` and has no `partial`,** so impact reads it as `conflict`. Rejected: bumping that contract for a state nothing produces with the switch off, which would send seven phases to `needs review` for nothing.
- **Grouping (C.6), off.** `fsd.groupSections`, with `fsd.groupMaxChars` (4000, stated, not measured). A unit is a run of neighbouring sections under one parent. Each link still cites its own section, and code checks its quote against that section alone. A section judged by requirement stays out, as it has its own unit, and a unit code finds no page for falls back to each section's own road. The unit has its own hash, and `saved` in `trace-runs.json` holds each side's links, so on re-runs and off reuses. Rejected: storing the two sides' runs in `trace.yaml`, which people read and edit; and letting the leader's hash stand for the group, which off would then read as stale.
- **Asking (E.2), off.** `spec.ask`, `stangent spec ask`, `/stangent:ask`, a one-turn `stangent-page-asker` with no tools. Code picks the pages by words in titles, routes and statements, ranked with no weights and capped by `spec.askMaxPages` (5, stated). `spec ask-check` holds every id to a page in the bundle, and takes the lines from `reference-lines`. No citations means it says the pages don't cover this. Answers are labelled `as the pages describe it`, because pages are generated. Rejected: graph expansion, which the brief leaves out and which would need a size rule of its own.
**Why:** a field list or a status table is the one place an FSD and code can be compared exactly, with no AI to be wrong. A person's mapping is what makes "exact" honest.
**Cost:** the map is work a person has to do per table, and it goes stale when a table moves to another type. `differs` means an enum's value only, since the graph holds no value for an API field, so most differences read as one name on each side. A renamed field with no alias reads as two findings. API fields are one level deep, and only for a type an endpoint sends or receives. A table with a cell holding a bar may quote badly. Comparing case-insensitively hides a real case difference. There's no real FSD to pilot it on, so its noise is unknown; D68's bar hasn't been applied. A judge given `unclear` may use it to avoid a call, and an `unclear` where a gap belongs is a real difference that nobody counts: only the re-score would show that. An impact answer calling a `partial` a `conflict` says slightly more than the judge did. A unit's bundle is longer, so it may read worse, and a section's text moving its neighbour across the limit changes the units after it, re-running those. A decided finding the group didn't find goes `gone`, and comes back as `recheck` when the section's own links return, so a person is asked again for turning a switch; that's D122's rule, applied. `saved` makes `trace-runs.json` bigger once the switch has been used. Asking checks that a citation exists, not that it supports the sentence, so a wrong answer can pass with real ids. Picking by words misses a page that says the same thing in other words, and the honest answer is then "not covered" when it is. A refused answer that failed twice also says "not covered", with why, which may be a page the AI cited badly rather than one that isn't there. It's a paid call per new question or changed page.

## D125 · What we take from the workflow comparison, in bulk — 2026-11-04 (owner: "lets take most of them, even the we want wanna test first, lets mark them down first")
Listed item by item in `improvements.md` §9; on `roadmap.md`. Marked down, none built.
- **Improve, I.1 to I.12; take, T.1 to T.10.** Next to build.
- **Take, then test, X.1 to X.19.** Taken in, and stays off until its test (D120). An item with no test stays off and doesn't count as proven.
- **Not taking:** another tool's file layout (owner), Windows and GitLab-specific setup (D24), Claude merging clashes without asking (D18), similarity cut-offs deciding matches, a repo survey agent. Assistant's reading of "most of them": the owner hasn't named these, so they can change.
- **Costly page passes moved in (owner's challenge):** they're mostly one-time, and pages look like the root cause of false alarms, so cost alone isn't a reason to reject them.
- **pandoc as a second reader, not a replacement:** replacing the renderer would change every `document.md` and force paid re-runs, and pandoc doesn't read PDF as far as we know (not checked). Differences are listed. Reverse if it keeps finding what ours misses.
- **Not settled by this:** BAs editing through Claude (D52, D12), journey pages (D54, D63), table comparison as a second route (D68), the installer, `MaxChapterGap`, and whether `improvements.md` is the agreed list. Assistant's recommendation on each stands in the conversation, unconfirmed; see Open questions.
**Why:** the owner wants everything useful written down before building more, with tests deciding what stays.
**Cost:** a much longer list than can be proven soon: about 19 items wait on tests, and several need material we don't have.

## D126 · Batch 1 of §9: a hard cost stop, checks around a run, FSD import fixes — 2026-11-05 (assistant's call, under the owner's D120 and D125)
Follows D15, D16, D18, D20, D66, D75, D120, D121 and D125. Items T.2, T.4, T.5, T.6, T.8, I.5, I.7, I.8, I.9, I.10, I.11. Fakes only: no real AI, no Jira, nothing pushed.
- **A paid step won't start without a choice (T.2).** `--max-cost <dollars>`, `STANGENT_MAX_COST`, or `--max-cost none`. No choice stops at the first paid step, exit 2, nothing spent. A run with nothing paid to do never asks. `/stangent:ask` defaults to `none`: one question, one call. Rejected: a warning with no stop, since a stop you have to remember isn't one. `plugin/scripts/cost-guard.sh` holds it for `spec-run.sh`, `ticket-run.sh` and `benchmark/tickets/run.sh`; `agent_baseline.py` has the same rules; `benchmark/run.sh` has nothing to stop, as no `stangent` command starts Claude Code, and a test pins that.
- **The estimate is this repo's own history.** The least calls a run must make, times that step's average so far, from a new telemetry field, `gen_ai.usage.cost_usd` (`stangent cost estimate`). No price list, so no number to go stale. Rejected: guessing from characters, which needs prices and a tuned ratio. An estimate over the cap stops the run before any call.
- **Money is held while a call runs.** Parallel jobs share what's left, and each call gets its share as `--max-budget-usd`. A file finder or reproducer starts only if all of its own cap is left, because a capped attempt records "not reproduced" and a run cut short by our cap must not write that.
- **Limits are noticed three ways, none of them certain.** A `rate_limit_event` saying `rejected`; a failed call's message matching a list of phrases; and `--stop-at-usage <percent>`, a person's number, once a call has reported a figure. One-turn calls moved to `stream-json` to see those events; the result line read from it is the same. Saved runs show usage only above a threshold. The plan's usage card can't be read, so a limit can't be foretold. `docs/foundation.md` says what's seen and what isn't.
- **Exit codes (I.10).** 1 means a step was refused or failed and the rest went on. 5 means stopped on the cap or a limit, and the same command resumes. 125 means the batch stopped on anything else: `xargs`'s own code can be 1 on some systems, and would read as refused. `benchmark/tickets/run.sh` goes on after a 1 and stops on the rest, so a refused ticket doesn't end a long run. `cli-conventions` v3 and `telemetry-event` v2 grow in place, as neither is on `main`.
- **T.5, T.6, T.4, T.8 change no prompt and no hash,** so they have no switch and are listed under `noSwitch`. `run-check` leaves out `.stangent/`, `specs/` and `stangent.yaml`, and a `--prove` run isn't checked (D75). `content-check` counts a link to a page not written yet apart from a broken one, since pages are made as needed, and leaves retired statements and removed sections to `trace check`; the whole-flow test showed all three were false alarms at first. `spec read` is new, because until now a person read a page's Markdown with nothing to warn them; a file opened directly still can't warn. The hook goes on the steps that have tools, and from the plugin by `agent_type`, never on the reproducer.
- **`split` stays doubtful (I.5).** `renamed` was already its own outcome. A split is now shown apart, by a rule with no score, and still needs a person: only one part can keep the id and the links, and D15 and D18 leave that to a person. So `counts.doubtful` still includes it. Rejected: settling it by code. A merge stays plain doubtful.
- **Originals to the archive (I.7).** At any depth, under the hash, with `archived` added on the file's record and its `file` and `hash` as they were, so nothing that reads them moves. It reverses D123's "these sit in the repo", which D123 itself flagged. `source-document` v3 grows in place. Older documents keep theirs until `fsd reread`.
- **A home for attached files (I.8), off.** `attachment-home`, `fsd.attachmentHome`. On, code adds one section at the document's end. Off by default, since a document with such files gains text and a section.
- **The table of contents wasn't removed (I.11),** so that was fixed. Sonarr's FSD has none: `document.md` is byte-identical and section ids and text hashes are the same.
- **Prompt caching (I.9), read from saved runs, no call made.** It's on, and we don't control it. On one-turn calls it mostly costs: of 417 saved page calls, 7.62M tokens were written to the cache and 0.72M read, about 1.7K a call, most likely the shared system prompt. Each call's input is new, so the write is never read back. On multi-turn calls it pays: 40 saved finder runs read 1.50M and wrote 0.34M. **Not changed:** turning caching off for one-turn calls needs a paid test, and putting the bundle first so the verifier reuses the writer's changes a prompt. Details in `phases/2c-spec.md`.
**Why:** the disprove re-score hit a limit nobody saw coming, and a batch that refuses a step used to look like a success. Everything else here is what a person would otherwise have to notice by eye.
**Cost:** a run with no cap now stops, so CI and any script needs `STANGENT_MAX_COST` (`plugin/ci/spec.yml` reads a repo variable). `benchmark/tickets/run.sh` too. A fresh repo has no estimate, which is exactly the pilot; the cap still holds. Only calls made from now on count, as older telemetry has no cost. Claude Code checks `--max-budget-usd` between turns, so one long answer can pass the cap by that much. `total_cost_usd` on a plan is a measure at API prices, not a bill. The limit phrases are from memory, as no real message was saved; an unmatched one still stops the batch, as a plain failure, without the resume line. Stream-json on one-turn calls, the hook's `agent_type` and `--settings` hooks have met no real Claude Code. A new exit 1 could stop a script that took 0 to mean finished. Attached files in a section are still only as good as `attachments-in-checks`, which is off. Word's table of contents styles are matched on English names outside a field or control. Nothing here has had a fresh check (D120).

## D127 · Python joins the code graph, tested on the owner's own project — 2026-11-05 (owner: "cant we use my cyberbullying thing docx, and we cater our stangent to handle that codebase also"; "Yes, Python next")
- **A Python extractor and a Flask route detector are next, ahead of §9's batch 2.** Parsing only: the app's code is never imported or run. Switched by a component, and C# and TypeScript results stay as they are.
- **The links get tested on `cyberbullying-detection-system`,** a chapter at a time from its `Documentation.pdf`, with the owner ruling on each finding. Starts at Chapter 7's use-case sections, not Chapter 6 as first said: Chapter 6 is survey and interview analysis. Each AI step has its cost stated and the owner's OK.
- **The clone comes fresh from GitHub** to `~/stangent-bench/cyberbullying`. The local copy's untracked `docs/` never reaches the AI steps.
- **Privacy:** the PDF holds a name and student id and this repo is public. Results stay outside the repo; the phase file holds counts only, no quotes.
- **Reverses D56's note** that links can't run on this project. **Widens D4 and D17,** which named C# and TypeScript. It isn't a general pivot: another language still needs a real need, as the proposed cap says.
- *First real run, 2026-11-05 (owner OK'd the first 5 use cases on Opus, caps $9 and $6):* refused before any work: `claude -p` said "weekly limit" with `seven_day` at 100%, resetting Nov 7 10:00 UTC, on Opus and on Sonnet alike, 2 calls, $0.00. The app's usage card shows 65% and Nov 10 for the same machine, reason unknown. So **the limit noted earlier as Sonnet's isn't Sonnet's,** and the card can't be used to plan headless runs. The cost stop met real Claude Code for the first time and worked: it named the window and reset, stopped with exit 5, kept the resume command.
**Why:** the 24 of 30 and 44% come from a 1,136-word document we wrote ourselves. This is 110,496 words nobody wrote for Stangent, on code the AI can't know from memory, with an author who can say who's right.
**Cost:** roughly 200-350K tokens for the extractor. Python's calls can't be resolved as exactly as C#'s, so weak links may come from the extractor, not the judge. It tests no C# and no tickets, and part of the document is thesis text, not rules.
- **Built (2026-11-05).** `tools/py-extractor` (parsing only, `python3 -I -B`), the `python` component, the `flask-routes` detector, plain React apps, and the fixes the flow needed: a `python` fence and `#` comments in bundles, `render_template` as a screen, `py|` in the drafts' tone check. `docs/code-graph.md` has the rules; `phases/1b-frontend-api.md` has what was run. `code-graph` stays v4: new id forms and one line kind in the same shape, not yet on `main`, so nothing is marked `needs review`. No fresh check (D120). **Assistant's calls, reversible:**
  (a) ✅ for one binding in scope in the same module or through a relative import; 🟡 for anything resting on an absolute import, a type hint or a returned value. **Cost:** most calls between modules are 🟡, though they're usually right.
  (b) An import that fits a project file off the importer's path is ❓, and `sys.path` changes aren't read. **Cost:** a blueprint imported that way is real but shows 🟡 and ❓ as not seen registered, which is most of the test project's blueprints. Guessing by file name would have hidden that.
  (c) A method on an unknown value is ❓ only if project code has that name; otherwise it's counted apart as `callsNotInProject`, with no line. **Cost:** a method attached from outside the class at runtime is missed. Listing every `.get()` would bury the ❓ that matter.
  (d) `imports` is a new line kind that isn't walked. **Cost:** an import's side effects don't reach impact; walking them would reach most of an app.
  (e) A plain React app with no tsconfig is read only in a repo with Python, because a repo with no Python must keep its graph. **Cost:** a JavaScript-only React repo stays off the map, and the rule looks odd. Reversible when that need shows.
  (f) `frontend-http` no longer lists `detector-aspnetcore-routes` as a need, since Flask's routes serve it too. **Cost:** with no routes detector on at all, `stangent components` won't warn.
  **Numbers that aren't measured:** a 10-minute timeout, as frontends have; a recursion limit of 10,000 for deep files; 3.9 as the oldest Python, because the extractor uses `ast.unparse`. None was tuned on the project.
  **Left for bug proof, not built:** see `phases/1b-frontend-api.md`.

## D128 · The parsed spec is the clean source of truth; new Word files come in as proposals a BA or PM checks — 2026-11-05 (owner: "i want it to be always be able to import new word docs, and our stangent able to identify new requirements and update the spec, which our BA or PM have to check, but the spec should be a clean source of truth"; replaces the same day's "edit using commands" after go-live)
- **`specs/fsd/<doc>/` is what's true.** A Word or PDF file is never the truth. It can come in at any time, with no go-live gate.
- **Stangent works out what's new or changed and proposes it; a BA or PM checks each change; only what's approved lands.** Until then the spec keeps serving as it was, so it stays clean: no unapproved text, no conversion leftovers.
- **Reverses part of D52,** where the document outside Stangent was the agreed one, and D15's latest file wins. **Returns to D11 and D12:** a living spec in the repo. D18 stands. D125's "Claude merging clashing edits without asking" stays rejected: a clash goes to a person.
- **Assistant's reading of what's built and what isn't.** Built: the candidate, the section diff, doubtful sections going to a person, links dropping back. Missing: approving change by change, since `fsd promote` takes the whole file; comparing against a spec that has moved on since the last import, so a BA's earlier approval isn't undone; a file holding only the changes, which today would read as everything else removed.
- **Whole documents or changes only: either (owner, 2026-11-05).** The person importing says which, with an option such as `--partial`; code never guesses. Without it, a missing section is proposed for removal, which a BA or PM still checks. With it, nothing is proposed for removal.
- **Any format (owner, 2026-11-05: "it could be in pdf or excel also").** Word, PDF and Excel all come in the same way, since the comparison runs on sections after conversion. **Not tried:** a real Excel FSD; two real versions of any format; a version in a different format from the last one, where differences in conversion could read as many changes.
- **Not settled, asked one at a time:** section level or requirement level, which leans on D99's `req` layer, off until X.9's test; who approves and how, since BAs don't use a CLI and the dashboard is Phase 8; whether direct edits stay, through the same queue.
- **Assistant's call, reversible:** built after the links have run on the owner's project. Section level first, because code can do it with no AI.
**Why:** a spec only people's approvals can change is one anyone can trust, and clients will keep sending Word files whatever Stangent says.
**Cost:** more work for a BA or PM, and a light rewrite can look like many changes. The repo can differ from what a client signed unless Stangent can export it. Roughly 150-250K tokens, not measured.

## Open questions

- **The spec in its own git repo? (owner asked, 2026-11-05, so a PM or BA sees an easier diff; owner: "okay we can keep it for now"):** the graph is settled, it isn't committed anywhere (D25). For the spec, recommended: stay in the app's repo (D7, D12) and fix the diff itself, with spec-only PRs and the generated files collapsed. Kept in the app's repo for now. Move when a real project needs what only a split gives: BAs without access to the code, client text kept away from developers, or one spec over several code repos. **A split would cost:** one PR no longer holds both spec and code (D7's reason), each link has to record two commits, a code branch and a spec branch can disagree, and paths are written in about 23 C# files.

- **Editing the parsed FSD directly (owner asked, 2026-11-05):** not supported, by D52: the FSD is the file a person changed, converted by code. Tried on a scratch copy: a hand edit to `document.md` changes nothing the steps read (`fsd show` still gives the original, since sections come from `document.json`), and `fsd status`, `spec content-check` and `trace check` say nothing; only `fsd reread` notices, by starting a candidate. Editing `document.json` wasn't tried. Recommended: a warning where the document is read, small, added to batch 2 because a test showed the need. Direct editing is now D128.

- **A first real pilot, a cut rule, a cap and an earlier bundle test (proposed by the assistant, 2026-11-04; the owner: "ok what we gonna do next, and lets do it in a new sessin"; not numbered until confirmed; order on `roadmap.md`):**
  (a) **The next milestone is a first real pilot** on the owner's own project, even with no FSD: pages, impact, one real ticket. Nothing added lately has met a real FSD or a real project, and the core result is still 24 of 30 found with 44% false alarms. Free steps first; AI steps on the owner's OK.
  (b) **Cut rule:** a component that fails a bar gets one fix attempt, then it's removed. Stops a failing piece collecting fixes forever.
  (c) **Cap on new items:** nothing joins the list unless a test or real use showed the need. `improvements.md` has about 40 items waiting, 19 of them on tests.
  (d) **"Does the bundle help a fix?" runs earlier:** in the test phase, before Phase 4 is grilled, not with it. Every Phase 3 bar checks a piece; none shows the bundle helps. Paid, on the owner's OK.
  **Order changed by the owner (2026-11-05: "wy not just build more first and we measure each stage later"):** I and T are built before the pilot, as D120 already says. Assistant's call, reversible: T.9, T.10 and I.12 wait for a repo or document that needs them; X items stay off. **Cost:** none of I or T can move 24 of 30 or 44%, so the core result stays unmeasured on a real spec for another 500-700K tokens, and defects stay in longer (D120's cost).
  **Still to settle, from writing this down:** the cut rule read literally removes 2c's links, which already failed both bars and are what the FSD side is for. Recommended: for a piece the pipeline can't run without, "removed" means its output stops counting as a finding until a person reads it, and the disprove re-score counts as its one fix attempt. D120's "bars and decisions stay as they are" holds either way: the rule cuts components, it doesn't move bars.

- **Table facts compared by code, a second route to code (proposed 2026-11-04, `improvements.md` §8 C.2; built switched off and labelled, D124, so this is still open):** everything else reaches code through pages, and D68 kept direct FSD-to-code links out as noisy. Recommended: allow exact comparison as its own labelled route, kept out of the impact walk like 📜 lines, after a pilot on field lists with a real document.
- **Which project the pilot runs on (2026-11-04):** the owner has no project of their own to use ("honestly i dont have my own project sooo") and asked for a reputable public one that has what we need. Sonarr is the tuning set and bitwarden the held-out set (D100), so neither fits. Recommended, not yet confirmed: **umbraco/Umbraco-CMS** (MIT, C# with a TypeScript frontend in one repo, .NET 10, Playwright with its own helper package, about 1,700 closed issues with a linked fix). Runner-up: Squidex/squidex, whose Angular frontend is closer to D4 but which has about 12 such issues. Differs from D4: its frontend is neither Angular nor React. **What a public repo can't give:** no real FSD, so the gap in the handoff stays; Umbraco's user docs are a separate repo and aren't an agreed spec. A public repo may be known to the model, so results may look better than on a private one.
- **Serving other stacks (owner, 2026-11-04: "maybe we can try pivot our project to also cater to other tech stack ... the fundamentals is the same right"; open):** partly the same. By lines of C#, `Stangent.Spec` (11.7K) doesn't use Roslyn and one of its files mentions a stack's file shapes. What's tied to a stack: `Stangent.Graph` (4.3K lines), the TypeScript extractor (1.1K), six `Stangent.Tickets` files (mostly the proof, which builds and runs the app). What doesn't carry over: quality. Roslyn resolves calls exactly; a typed language such as Java should come close, a dynamic one such as Python won't, and impact and page slices rest on those calls. 24 of 30 and every other score were measured on C#. Recommended: keep the goal, don't pivot now. Finish a pilot on what's supported first (D1's reason: abstracting over many stacks before one works end to end is how platforms stall), and add a language when a test needs it, as the proposed cap says. Rough estimate, from past batches: 500-900K tokens for one language's extractor and detectors. D4 and D17 stay until the owner decides.
- **A public repo with a real FSD (searched 2026-11-04):** none found that's reputable, an application, and C# or TypeScript. About 300 repos have a file named like a functional spec; those in our languages are mostly libraries, student projects or files written for AI agents. GitHub's search can't read Word or PDF files or outside wikis, where real FSDs usually live, so this doesn't show there are none. Nearest: Apache Fineract (Java, 2.6K stars, functional specs on Apache's wiki, its 1.0 page kept for historical reasons); openIMIS (functional specs on its wiki, its own project team says; the page couldn't be read). HydroServer's README names `specs/functional`, which isn't in its public tree.
  **The owner then found RealWorld (2026-11-04):** `realworld-apps/aspnetcore-realworld-example-app` (C#, 2.1K stars, MIT, 106 `.cs` files) implements the written spec in `realworld-apps/realworld` (84K stars), which also holds about 160 Bruno API tests, 13 Hurl files and Playwright tests. **What it gives:** a real spec nobody wrote for Stangent, and an answer key made by code: run the spec's tests on commits from before the repo's spec fixes (for example #134, "adapt to current realworld spec", and `wrong email format as 422`), and each failure is a real gap between spec and code. The same spec has Go, Angular and Vue implementations, so it can also test other stacks later. **What it can't give:** it isn't an FSD in shape (short API and template notes, no business rules or numbered chapters); it's Markdown, which `fsd import` doesn't take, so it needs converting and tests links, not Word import; the app is small, so impact isn't stressed; it has 52 closed issues; and the model very likely knows this spec, so it may answer from memory, which could flatter the links. It also pins .NET SDK 10.0.403 and this machine has 10.0.302. Recommended, not confirmed: RealWorld first for the FSD side, Umbraco after for size, tickets and helpers.
  **A bigger spec (owner, 2026-11-05: "feel like i want something with bigger spec"):** the planted Sonarr FSD is 1,136 words, about 2-3 pages, so 24 of 30 and 44% say little about a real one. Recommended, not confirmed: `umbraco/UmbracoDocs` beside Umbraco itself (MIT, written by people, 7,180 merged PRs). Its `18/umbraco-cms` is 562 Markdown files, 3.37 MB, about 500 times the planted FSD, and a chapter can be taken at a time. Runner-up: the FHIR R4 spec with `microsoft/fhir-server` (normative SHALL rules, closer to an agreed spec, but no screens, and 48 closed issues with a linked PR). **What it can't give:** product docs describe, they aren't an agreed FSD, so a difference may mean the docs are stale; no answer key exists yet (one search for issues quoting the docs found 4, so one would be built from docs-fix PRs); `fsd import` doesn't take Markdown, a need real use has now shown under the proposed cap; and running it whole isn't affordable, so it depends on I.1.
  **Decided as D127 (owner, 2026-11-05).** The notes below stay for the reasons. **The owner's own project (owner, 2026-11-05: "cant we use my cyberbullying thing docx, and we cater our stangent to handle that codebase also"):** `cyberbullying-detection-system` on GitHub. It's a PDF, not Word, and Python (Flask, 35 route lines) with React, not PHP. `Documentation.pdf` imports with 110,496 words and 340 sections, about 100 times the planted FSD, verdict `ok-with-warnings`, 77 problems (not yet read). Recommended, not confirmed: use it for the links, reversing D56's note that links can't run on it. **For it:** real text not written for Stangent, little known so the AI can't answer from memory, code small enough that AI steps are cheap, and the owner wrote it, so a person can rule on each finding (D18). The need for a Python extractor is shown by real use. **Against it:** it's a thesis, so only part is spec (chapters 6, 7, 9 and 10 hold about 48,000 words, how much is rules isn't measured) and it may describe what was already built, so disagreements may be few; 0 issues and 2 commits, so no tickets or history; the detection model is in notebooks the graph won't read; nothing here tests C#. **Privacy:** the PDF holds a name and student id, and this repo is public, so results stay outside the repo and the phase file holds counts only. The clone comes fresh from GitHub, because the local copy has an untracked `docs/` with earlier notes and an issue list that must not reach the AI steps. Rough estimate for a Python extractor and Flask detector: 200-350K tokens.
  **Also looked at, not recommended (2026-11-04):** `suragms/warehouse-assistant-erp`. The closest match to D4's stack (.NET 10, React, Playwright, 11 `.csproj` in one repo), but 0 stars, one author, 42 commits in two weeks, no issues or PRs, so it has no tickets and isn't reputable. Building it runs an unknown author's restore and install steps on this machine, so it stays unbuilt.
- **For the Playwright adapter (T.9, 2026-11-04):** no project of the owner's to ask about. On Umbraco the tests sit in the app's repo and use a helper package, so helpers get a real test. **Specs in another repo do have a known case after all:** RealWorld's Playwright tests sit in its spec repo, apart from every implementation. D4 leaves separate repos out for now, so taking this up is the owner's call.
- **Recommended, not confirmed (2026-11-04):** BAs editing through Claude, not yet; journey pages, not yet; table comparison as a labelled second route, yes; installer, when BAs become users; keep `MaxChapterGap = 10`; `improvements.md` as the agreed list, yes.
- **Waiting on the owner after the overhaul (2026-11-04):**
  (a) Confirm `improvements.md`, and the order after 2.x.
  (b) The fresh check on 2.5 and 2.6 is done, with its fix round (`phases/2b-fsd.md`, D119's note).
  (c) The disprove step's re-score, about $4–6 (`phases/2c-spec.md`).
  (d) 2c's found bar: the disprove step can only remove findings, so it can't lift 24 of 30 to 27. Keep the bar and look for what's missing, or change it. Recommended: decide after the re-score.
  (e) BAs editing the FSD through Claude (reversing D52 or D12), dropping the requirements layer (D99), journey-based pages (D54, D63): left alone, as agreed.
  (f) The drivers' refused steps still exit 0 (pinned by a test, Phase 0). Recommended: exit nonzero.
  (g) A client-terms file outside the repo, so `data check` can look for the names it must not find.

- **Evidence beyond the FSD for "looks intended" (owner's outside review, 2026-11-01):** API docs, existing tests that
  assert the current behaviour. Not decided. (The review's other new idea, right place reported per stage, is built:
  `reported.rightPlaceStages`, D98's re-score.)
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
- **Order of the next work (owner, 2026-11-02: agreed; (1) done, (2) under way, held-out set: D100):** clean up the
  foundation only where a measured loss traces to it: (1) the free checks first (the bundle-order fix and the nearby-page walk check; no weights, so nothing is tuned
  on the 40 bugs), whose dead ends aim the graph work; (2) the benchmark: a tuning/held-out split, a second repo, the
  strong agent baseline; (3) the graph: the missing entry-point links the walk check found, and a search over files and
  names; (4) Phase 3's retrieval redesign on top, re-measured on the held-out set; (5) D99 once a project with an FSD
  makes it measurable. Reference pages are
  not rewritten. Before starting: commit or set aside the Phase 3 worktree's uncommitted work (owner's OK), and apply
  D20 to any contract change.
- **Does the bundle help a fix, or skew it? (owner's questions, 2026-11-02, for Phase 4):** every Phase 3 bar checks a piece (a
  right file, a real quote, a failing test); none shows the bundle helps anyone fix a bug. On the finder's 4 Sonarr misses it
  covers none of the fix's files. The owner's worry: a wrong lead skews every later step, and a scoped input invites a less clean
  fix (SONB-18: a null check on the screen turns a test green and leaves the bad data). Recommended: the fix agent on the same bugs three ways
  (the ticket alone; plus the failing test; plus the whole bundle), scored on the tests passing and on a read of each diff against the developer's fix, wrong leads
  counted apart; if there's no difference, cut it to the finder's files and the failing test. Recommended rules: named files are
  leads, never a limit on what an agent reads or edits; it traces from the failing test before it edits. A green test can't show
  a fix is clean, so that stays a person's review (Phase 6). To grill with Phase 4.
  **Proposed on top (recorded on the owner's "ok yes", 2026-11-02; not decided):**
  (a) **Start without the bundle:** the fix agent gets the ticket and the code until a test shows the bundle helps. Reference pages
  are generated from code, so they can describe a bug as how the feature works; the ticket and the agent can skew a fix too (SONB-18's
  title invites the null check), so this is measured against them, not assumed.
  (b) **Label context by source:** something that ran; something a person or the FSD said; something read in the code; a guess. No fix
  rests on a guess until a run or a person backs it up.
  (c) **Plant wrong leads:** the fix agent on the 17 Sonarr bugs whose saved test passes on the developer's fix, with and without a deliberately
  wrong file or cause. Code runs the tests; a person reads only the fixes that go green, since a green test can still be a wrong fix. About $8–34
  for 34 runs, estimated from the proof's cost (no fix agent has run), on the owner's OK. The saved tests predate the finder and may need rechecking.
  Sonarr is public, so the result may look better than on a private repo.
- **Where does security review go? (2026-11-02):** the owner's brief lists it in the pipeline; no decision or phase holds it.
  Recommended: a checked step in Phase 6's review package, grilled with that phase.
- **Flakiness of the proof (2026-11-02):** only one full run, and a unit test runs once. Running a test more than once per proof
  needs no AI. Not decided. The Sonarr re-run gives a second result to compare.
- **The not-bug bar on Sonarr (proposed 2026-11-02, not decided):** report D79's "catches wrong reports" as "can't be
  measured: no FSD covers these tickets" rather than failed, unless the planted FSD covers them (a free check: which of
  the 10 not-bug tickets fall inside its 15 screens). **Checked 2026-11-02: it covers none.** The 10 are about an
  indexer, a required password, library import, anime parsing, table sort memory, bulk moves, memory, CPU and disk space;
  the closest, SONB-42 (a Trakt link on series details), lands in §1.2, which never mentions links. So D99 can't be
  measured on Sonarr's not-bugs; it needs a project with a real FSD, or FSD text written for those areas.
- **Generality batch (2026-10-31):** fixed as D94 (2026-11-01), except Karma/Jasmine and Mocha test targets; still to
  check on a third repo of a different shape (CleanArchitecture).
- Dashboard sign-in for BA/PM: company SSO or GitHub accounts? (Phase 8)
- "ministack" vs LocalStack vs Testcontainers for integration tests: was it a deliberate choice? (Phase 5)
- Parallel subagents: should the orchestrator fix the contract first (API shapes, shared types), then run
  the sides in parallel against it? And if a subagent finds mid-task that the contract must change, does it
  stop and hand back, or change it and tell the others? Leading answer: contract first; stop and hand back.
  Grilled with Phase 4, after the 1d verdict. (Phase 4)
