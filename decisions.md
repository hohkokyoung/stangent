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
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | Angular and React frontends calling the API, MediatR, EF Core, Minimal APIs, Playwright tests |

Metric for impact analysis: for past issues with merged PRs (nopCommerce, eShop), compare
predicted files/symbols against the files the PR actually changed (precision and recall).
CleanArchitecture is a small template: it tests detector correctness, not scale or history.
**Gaps:** no Jira (needs a sandbox Jira project); no large real-world Angular/React codebase.

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

## Open questions
- Dashboard sign-in for BA/PM: company SSO or GitHub accounts? (Phase 8)
- "ministack" vs LocalStack vs Testcontainers for integration tests: was it a deliberate choice? (Phase 5)
- Confirm the company's Claude plan excludes training on its data (D24), before running on client code.
