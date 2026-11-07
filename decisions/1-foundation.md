Short entries for what's still in force. Full text, and everything archived: [archive/1-foundation.md](archive/1-foundation.md).

# Decisions D1–D24: foundation and the brief

## D1 · Built for the owner's own use first — 2026-10-18
Stangent targets the owner's real workflow and stack first. "Own use" covers company and personal projects alike, so nothing may assume one company, its tools or its documents. Making it installable for others (spec item 11) comes later.
**Why:** abstracting over many ticket systems, hosts and stacks before one works end to end is how platforms stall. **Cost:** other set-ups wait.

## D2 · Tickets: Jira — 2026-10-18
Tickets live in Jira, reached through Atlassian's MCP. It can download attachments, and it takes an API token for unattended runs once an organization admin turns that on (D73).
**Why:** it's the owner's ticket system. **Cost:** unattended runs depend on an admin setting.

## D3 · PRs on GitHub — 2026-10-18
PRs go on GitHub; CI/CD defaults to GitHub Actions. Both sit behind adapters (D24).
**Why:** it's the owner's code host. **Cost:** none yet.

## D4 · Target codebase shape — 2026-10-18
One monorepo, many `.sln` and `.csproj`, about 500k lines, the frontend in the same repo, monolith and microservices mixed. So: index by `.csproj` and de-duplicate; Roslyn needs a working `dotnet restore`; per-PR use is incremental; calls between services need detectors by convention. Separate repos and submodules are out of scope.
**Why:** that's the shape the owner works in. **Cost:** a broken build means no graph. Widened by D127 (Python) and D154 (any stack).

## D5 · Build from the ground up, contracts first — 2026-10-18
The foundation is the contracts between products (artifact formats), not any one product. With no single pain point to break ties, the benchmark (D6) is the measure.
**Why:** every product reads and writes the same artifacts. **Cost:** no pain point to settle trade-offs.

## D6 · Benchmark: public repos — 2026-10-18
Public repos stand in for real code: nopCommerce (size, plugins, string routes, in-house events), dotnet/eShop (microservices, events), CleanArchitecture (generated frontend clients; detector correctness, not scale), Kavita (Angular), bitwarden/server (squash-merged history), Sonarr (React with an in-house wrapper). Impact is scored against the files past PRs really changed. How to run it: `benchmark/README.md`.
**Why:** a shared, repeatable measure. **Cost:** public repos aren't the owner's code.

## D7 · Source of truth: files in the repo — 2026-10-18
Artifacts are Markdown plus YAML/JSON, versioned by git. A local SQLite index is rebuilt from them for fast queries and the dashboard. Telemetry is the exception (D21).
**Why:** git gives history and revert, spec changes are reviewed with the code, and there's no server. **Cost:** links go stale when edited outside the tool, so CI checks them.

## D8 · Portability through versioned schemas and one store interface — 2026-10-18
Every artifact type has a versioned JSON Schema, and the schema is the contract. Products never read each other's files directly; everything goes through one store interface (read, write, query, link). One backend now (files + SQLite); a second only when something forces it.
**Why:** a backend can change without touching products. **Cost:** a layer between every product and its files.

## D9 · Each product is a CLI — 2026-10-18
Each product is a CLI that reads and writes the versioned artifacts. The Claude Code plugin is a thin layer of commands, skills, agents and hooks over them. MCP only where it clearly helps.
**Why:** a CLI costs no context until called, runs the same in CI, is testable without an LLM, and each product can use its natural language. **Cost:** users need the .NET SDK and Node.

## D10 · Requirement ↔ code links live in a link file — 2026-10-18
Links live beside the specs (`trace.yaml`); production code isn't annotated. They target symbol ids, never file:line. Tests carry spec ids, so coverage comes free. CI checks every linked symbol still exists and repairs renames from the graph diff, or flags them. A guess with no checked quote is never a link. The chain is FSD → reference → code, every hop checked (D52). Links go coarsely to entry points (endpoints, handlers, components); the graph supplies what those reach.
**Why:** links survive edits and stay checkable. **Cost:** a refactor outside stangent breaks links until CI catches it. AI-written requirements as the first hop replaced by D156 P4.

## D11 · A living spec of the whole system, owned by BA/PM — 2026-10-18
The spec says what the system does now, with ids stable for life. A ticket is a change request that produces a spec change, in its own spec-only PR, normally merged before development. States: proposed → verified → implemented, or implemented but provisional. Changes found by developers start as proposed. Revert works per ticket. Verifying stores a hash of the text, so any edit drops it back to unverified. BA/PM never review code PRs; code PRs name the ticket and the spec ids they implement.
**Why:** the BA/PM own the business logic. **Cost:** more review work. AI-written requirements replaced by D156 P4; as-built drafts held (D155).

## D12 · Specs are written in the codebase, BA/PM edit them through the dashboard — 2026-10-18
The spec lives in the same repo as the code, never in Confluence or the ticket system. BA/PM will work in a dashboard view whose writes go back to git as spec-only PRs; it's a screen over the repo, not a second store. Until it exists, spec changes are plain spec-only PRs.
**Why:** the spec must outlive any tool change. **Cost:** sign-in is needed early, since a verification records who approved. The requirement tick-boxes (D99) went with D156 P4.

## D15 · Source documents: many formats, every original kept — 2026-10-18
Word, PDF and Excel come in (D111), converted to text by code. Every original is archived (D16); identical files (same hash) come in once. Stangent never settles a clash itself: it points it out and a person decides.
**Why:** clients send documents in any format, and every citation must trace to the exact file. **Cost:** a converter per format. "Latest file wins" reversed by D128 (the parsed spec is the truth). Still in force: across several documents, a clash is settled per item, not per document, so a newer document covering part of a feature supersedes only that part.

## D16 · Original documents live in a separate archive repo — 2026-10-18
Originals (`.docx`, `.xlsx`, `.pdf`) go in their own repo, with Git LFS for large files; the main repo holds only the converted text. Each citation stores the archive's name, the path and the content hash, and an original is fetched only when needed. The archive sits behind the store interface (D8), so it can move.
**Why:** separate permissions for client documents and a light main repo. **Cost:** a second repo to keep.

## D17 · Our own code-graph extractor, with pluggable link detectors — 2026-10-18
Our own extractor builds the graph: Roslyn for C#, the TypeScript compiler for the frontend, a parser for Python (D127). The links that break things (routes, DI, events, database mapping, calls between services, frontend calls to backend URLs) come from detectors, one per pattern, switched on per repo. A new framework or in-house pattern costs one detector. Blind spots are recorded as unresolved and reported, never hidden.
**Why:** plain-reference indexers miss the links that break production. **Cost:** we keep up with new language features. Rules: `docs/code-graph.md`.

## D18 · The AI proposes, code checks; if code can't check it, a person approves — 2026-10-18
Code is the default; an AI step must justify why code can't do it, and becomes code once it's predictable. Code only: the graph, link detection, impact, test selection, document conversion, deploying. Code: AI code must compile, pass tests and change only expected areas, and a senior reviews the PR; a test must fail without the change and pass with it; a PR summary's claims must link to real files or results. A person approves what goes to a client. The AI never deploys. Accepted AI output is stored with its inputs' hashes and model, and reused unless an input changes.
**Why:** a hallucination is caught at the step that made it. **Cost:** every AI step needs a checker or a person. Strengthened by D155. Requirement rows replaced by D156 P4; client-update wording by D156 P7.

## D19 · Build order — 2026-10-18
0 Foundation → 1 Code graph → 2 Spec → 3 Tickets → 4 Agents → 5 Testing → 6 PR → 7 CI/CD → 8 Dashboard; a demo can slot in anywhere. Phase 1d's pilot sits between 1c and 2 (D45). The telemetry format is part of Phase 0. Full table: `roadmap.md`. The current rebuild order inside 0–3 is D156's.
**Why:** each phase uses the one before. **Cost:** the dashboard BA/PM need (D12) comes late.

## D20 · Every phase ends with a report; changes ripple to the phases that depend on it — 2026-10-18
Each phase file (`phases/<n>-<name>.md`) declares the contract versions it consumes and provides, and ends with a report (format: `docs/phases.md`). A change to a contract bumps its version; `phases check` fails for a phase on an old version until it's marked `needs review`, with a follow-up. Dependents are never changed automatically. Docs change in the same commit as the behaviour.
**Why:** a change upstream can't silently break what's built on it. **Cost:** follow-up work after each contract change.

## D21 · Telemetry: cost, quality, speed and failures for every run — 2026-10-18
Each run records cost (tokens, money), quality (check failures, rejections, scores), speed and failures. Events are written locally and kept with CI runs, not in git (an exception to D7), in OpenTelemetry format, with no content by default: counts, durations, hashes, ids. A per-run debug switch records content locally. The format is the `telemetry-event` contract.
**Why:** cost and quality prove stangent works. **Cost:** debugging without prompts means re-running the step.

## D22 · Grill each phase when it starts, not all upfront — 2026-10-18
Each phase is grilled when it starts, and the answers go in its phase file and here.
**Why:** building the early phases changes the answers for the later ones; designing them now means designing them twice. **Cost:** later phases have open questions until they start.

## D23 · One `stangent` CLI is the store's only door; its core is C# — 2026-10-18
Products read and write artifacts through one `stangent` CLI, JSON in and out, so store logic and checks exist once. The core is C#, installed as a `dotnet tool`; the code graph uses it in-process, everything else calls the CLI, with batches to save start-up time. Reconfirmed by D110.
**Why:** one copy of the store logic. **Cost:** Node products and hooks pay start-up time; working on the core needs .NET.

## D24 · Company constraints, and CI / code host behind adapters — 2026-10-18
Developers can install `dotnet tool`s and npm packages. CI workflow files stay thin and only call `stangent`, so moving off GitHub Actions means rewriting thin YAML; the code host sits behind an adapter, like tickets and the archive. Sending code to Claude is approved, and the account keeps its data out of training. Everyone has Claude Code. A per-repo switch turns off every AI step, for a client whose contract forbids external AI.
**Why:** no lock-in, and clients' rules are met. **Cost:** an adapter per host.
