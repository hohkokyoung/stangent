---
phase: 0
name: foundation
status: done
consumes: {}
provides: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1, evidence: 1 }
---

# Phase 0 · Foundation

**Approved 2026-10-18. Done 2026-10-19.**

The skeleton every product plugs into, proven end to end with one trivial artifact.
No product logic: no graph, no spec import, no AI calls.

## What gets built

### 1. The `stangent` core and CLI (D8, D23)
- `src/Stangent.Core/`: C#, .NET 10.
  - Store interface: read, write, query, link, batch.
  - First backend: files in the repo + SQLite index rebuilt from them (D7).
  - Schema validation, contract versions.
  - Telemetry writer.
  - The per-repo AI on/off switch (D24).
- `src/Stangent.Cli/`: the `stangent` dotnet tool, JSON in and out. First commands:
  - `stangent init`: write `stangent.yaml` and the `.stangent/` folder.
  - `stangent store get | put | query | link | batch`
  - `stangent index rebuild`
  - `stangent phases check`: the D20 dependency check.

### 2. Contracts
- `schemas/`: JSON Schemas, one file per contract and version:
  - `artifact-base.v1.json`: id, type, schema version, provenance, citations, created/updated.
    Every other artifact builds on this.
  - `telemetry-event.v1.json`: OpenTelemetry-shaped run events (D21).

### 3. What stangent adds to a target repo
```
stangent.yaml          # config: enabled detectors, AI on/off, adapters (tickets, code host, CI, archive)
specs/                 # spec + trace.yaml (Phase 2 fills it)
.stangent/
  index.sqlite         # rebuilt from files; git-ignored
  telemetry/*.jsonl    # run events; git-ignored (D21)
  runs/                # recorded AI outputs with input hashes (D18)
```

### 4. Claude Code plugin skeleton (D9)
- `plugin/`: manifest, plus one command (`/stangent:status`) that calls the CLI.
  This proves the plugin → CLI → store path.

### 5. Benchmark harness (D6)
- `benchmark/`: pins nopCommerce, eShop and CleanArchitecture at fixed commits.
- Runs a given `stangent` command against each and records results (time, tokens, scores) for
  end-of-phase reports.

### 6. Stangent's own CI
- A thin GitHub Actions workflow that runs build + tests by calling scripts and `stangent` (D24).

## Done when
- [x] `dotnet tool install` from a local build gives a working `stangent`.
- [x] In each benchmark repo: `stangent init`, put and get a sample artifact (rejected when it
      breaks the schema), delete the index, `stangent index rebuild` reproduces it exactly.
- [x] Every command writes a valid telemetry event; nothing contains code or prompt text.
- [x] `stangent phases check` flags a version mismatch in a test fixture.
- [x] `/stangent:status` in Claude Code reports the store's contents through the CLI.
- [x] CI green ([run 39146680729](https://github.com/hohkokyoung/stangent/actions/runs/39146680729), ubuntu-latest).
      Tests (xUnit) cover the store, validation, index rebuild and phase check.
- [x] End-of-phase report written below.

## Not in this phase
Adapters for Jira, GitHub PRs, GitLab and the archive: only their place in `stangent.yaml` is
reserved. Each is built in the phase that first needs it (D22).

## End-of-phase report

*2026-10-19 · first report; updated the same day when CI ran green on GitHub. Phase 0 is **done**.*

### 1. What was built
- **Core** (`src/Stangent.Core`): the store interface (`IArtifactStore`) and its first backend,
  `FileArtifactStore`. One canonical JSON file per artifact (sorted keys, LF, so the same content
  is always the same bytes and hash). The SQLite index is a pure cache: it records a fingerprint of
  the store files and is rebuilt whenever they change, including changes from `git pull`. Also:
  batch ops checked in order before any write, the schema registry (contracts embedded in the
  tool), `stangent.yaml` loading, the AI switch (`AiPolicy`), the telemetry writer, and the phase check.
- **CLI** (`src/Stangent.Cli`, dotnet tool `stangent`): `init`, `status`, `store get|put|link|query|batch`,
  `index rebuild|dump`, `phases check`. Conventions in `docs/cli-conventions.md`.
- **Contracts, all v1:** `store`, `artifact-base` (`schemas/artifact-base.v1.json`), `telemetry-event`
  (`schemas/telemetry-event.v1.json`), `cli-conventions` (`docs/cli-conventions.md`),
  `benchmark` (`benchmark/repos.tsv` + `benchmark/run.sh`).
- **Plugin** (`plugin/`, marketplace at `.claude-plugin/marketplace.json`): `/stangent:status`.
  Both pass `claude plugin validate --strict`.
- **CI** (`.github/workflows/ci.yml`): build, test, `phases check`.
- **Tests:** 25, all passing.

### 2. Benchmark results (`benchmark/results/2026-10-18-phase0.json`)
| Repo (pinned commit) | `stangent status` | Telemetry events | AI tokens |
|---|---|---|---|
| nopCommerce `77d112f` | exit 0, 147 ms | 2 (init + status) | 0 |
| eShop `913acea` | exit 0, 145 ms | 2 | 0 |
| CleanArchitecture `1bc2b07` | exit 0, 145 ms | 2 | 0 |

Each CLI call costs about 145 ms, almost all of it .NET start-up. That's why batching exists (D23).
In each repo, put/get/reject/rebuild all behaved as specified. After init, git sees only
`stangent.yaml`, `.gitignore` and the store files; the index and telemetry are ignored. 21
telemetry events were recorded across the checks, and none fell outside the contract.

### 3. Decisions made or changed
- D20 refined: a phase on an old contract version *fails* the check until it is marked
  `needs review`. Once marked, it passes, so an upstream change can merge while its follow-ups are open.
- JSON Schema draft-07 validated with **NJsonSchema** (MIT). JsonSchema.Net was rejected: its
  current license is the OSMF EULA, not MIT.
- Tests run on Microsoft.Testing.Platform (`global.json`), which .NET 10's `dotnet test` requires for xUnit v3.
- Telemetry outside any repo goes to `~/.stangent/telemetry`; `STANGENT_TELEMETRY_DIR` overrides it (CI, benchmark).

### 4. Known gaps
- The index rebuilds in full after any change. That's fine now, but the Phase 2 audit will write
  thousands of artifacts, so measure it then and add incremental updates if needed.
- Index freshness uses file size + modification time. An edit that keeps both unchanged would go unnoticed (rare).
- No `store delete` yet. Per-ticket revert (D11) will need one.
- Type schemas (`schemas/types/<type>.v<N>.json`) are supported, but none exist yet. Repo-local custom types aren't supported.
- Tested on macOS (local) and Linux (CI); not on Windows.

### 5. Impact on other phases
None yet. All contracts are new at v1 and nothing consumes them. Phase 1 will be the first consumer.

### 6. Follow-ups
- ~~Add a GitHub remote, push, confirm CI is green~~: done. The repo is now `hohkokyoung/stangent`; the earlier
  repo of that name is archived as `hohkokyoung/stangent-archive`.
- Phase 1 (code graph) starts with its own grilling (D22).

### Reopened 2026-10-19 (during Phase 1a)
- **`cli-conventions` v1 → v2:** commands with `--format md` may print Markdown instead of the
  JSON envelope on success (needed for `graph impact` in PRs). It's opt-in, so callers that never pass
  `--format md` see no change. Consumer flagged: 1a (updated in the same change, since it's the phase making it).
- **`benchmark` stays v1, extended:** per-repo `configs/<name>.yaml` (detector settings) and
  `configs/<name>.global.json` (SDK override; nopCommerce pins SDK 10.0.401, which neither this
  machine nor CI has) are applied after `stangent init`.

## Overhaul (2026-11-04)

Slices 0.1 to 0.5 of `roadmap.md`'s overhaul order (D112, D113, D114).

### What was built
- **0.1 Evidence shape:** a new contract, `schemas/evidence.v1.json`, with `EvidenceRecord` and `EvidenceValidator` in
  `src/Stangent.Core/Evidence/`. `PermanentIds` is the id ledger. See `docs/foundation.md`.
- **0.3 Client-data check:** `stangent data check`, in CI. On this repo it flagged one file,
  `benchmark/fsd/sonarr/sonarr-fsd.docx`, the planted FSD, now in `.stangent-data-allow` with its reason.
- **0.4 Coverage measured:** `scripts/coverage.sh`, with no floor and no gate (D28). First numbers, by line:

  | Assembly | Line coverage | Lines |
  |---|---|---|
  | `Stangent.Benchmark` | 25.8% | 1167/4532 |
  | `Stangent.Cli` | not loaded by any test | |
  | `Stangent.Core` | 94.3% | 895/949 |
  | `Stangent.Graph` | 82.3% | 2226/2706 |
  | `Stangent.Spec` | 89.7% | 5128/5718 |
  | `Stangent.Tickets` | 71.6% | 10528/14713 |
  | **Total** | **69.7%** | 19944/28618 |
- **0.5 Driver tests:** `spec-run.sh`, `ticket-run.sh`, `benchmark/run.sh` and `benchmark/tickets/run.sh` run from xUnit with
  fake `claude` and `stangent` first on the path. The environment is built from nothing, so no test can reach the real `claude`.
- **0.2 State changes:** one module, `src/Stangent.Core/State/`: a table of legal moves per lifecycle, one entry point that refuses the rest, and an append-only history (`docs/foundation.md`, D114). Eleven lifecycles moved onto it. `stangent state history <kind> <id>` shows one item's moves. A test fails on a new direct write. Requirement ids moved onto the permanent-id ledger, so parallel runs can't get the same id; numbering is unchanged, and the first 858 tests passed unchanged.
- **Bugs the driver tests found, each fixed with the smallest change:**
  - `spec-run.sh`, `ticket-run.sh`: a stangent crash (no envelope) read as "there's a next step", so they called Claude in an endless loop. This could have spent real money. They now stop and name the page or step.
  - `spec-run.sh`: `--all`, `--stale`, `--fsd` and `--req` took a failed list for an empty one and exited 0. They now fail and say which list.
  - `spec-run.sh`, `ticket-run.sh`: an unknown flag was taken for an id, and no arguments exited 0. Both now give usage, as does a bad `--jobs`.
  - `spec-run.sh`, `ticket-run.sh`: when Claude failed with no JSON, the reason came out blank.
  - `ticket-run.sh`: a failed bundle printed its code, went on to the paid steps and exited 0. It now fails, as a failed pull does.
  - `benchmark/tickets/run.sh`: a typo in a phase name surfaced only after the phases before it had run and been paid for. Phases are now checked first. It also has a usage message now.
  - `benchmark/tickets/run.sh`: `pages` died silently, exit 2, when the last ticket had no bundle yet.
  - `benchmark/run.sh`: a failed step stopped the run with no word of where. It now names the line and command.

- **S.0 Whole-flow test** (`tests/Stangent.Core.Tests/WholeFlowTests.cs`, `docs/foundation.md`). All ten stages are reachable from the real CLI. What it found:
  - **Fixed:** `init` didn't ignore `.stangent/fsd-disprove/` or `.stangent/graph.sqlite` (`StangentRepo.cs`, `GitIgnoreLines`), so a `git add -A` committed disprove bundles holding FSD text and source, and the graph that `docs/code-graph.md` calls git-ignored. A repo set up earlier gets the lines on its next `init`.
  - **Fixed after the first run,** each fact's `Skip` taken off, with no expectation changed. The run's own step that answered a new verdict on section 3 went, because code two lines lower no longer asks for one; a new fact states that:
    - **A new commit reopened every finding:** fixed, the commit is shown and not hashed (D119's note).
    - **A section that only moved lost its verdict:** fixed, the number is shown and not hashed (D119's note).
    - **History didn't follow a moved finding:** fixed. The `X-` id was already stable and the entry id moved, so a move line connects them, and `state history` reads it and takes an `X-` id (D117's note).
    - **A gap in typed heading numbers:** fixed, a typed number stays a number across a gap, with `2023 Roadmap` and `24 hour clock` still plain (D117's note).
    - **An odd line for a gap:** fixed, the note says the page has no statement on it, with no hash moved.
  - **Worked first time:** set-up, pages, both imports, links and findings, routes and reviews, decisions, impact and the ticket bundle, promotion, including CI on a promotion and a section number used again, regeneration, line citations, and input hashes.
  - **Worth knowing:** a removed section's entry fails `trace check` until a person deletes it by hand, though the file's header says to edit `decision` only. CI accepts the deletion. The test does this at the end.

### 0.2: what's a lifecycle and what isn't
Lines are as of the slice's commit. A field holds a lifecycle if it stays on disk and its next value depends on where it is now.

| Field | Sorted as | Where | Why |
|---|---|---|---|
| requirement `status` | lifecycle `requirement` | `RequirementRun.cs:528,560` | Approval survives a rebuild or drops back; a retired id never returns |
| question `status` | lifecycle `question` | `RequirementRun.cs:592,600` | Closes, opens again with its answer |
| `decision` | lifecycle `trace-decision` | `TraceCheck.cs:93,135,156`, `LinkRun.cs:338` | D58 |
| proof `outcome` | lifecycle `proof` | `BugProof.cs:440` | The current state, replaced by each attempt |
| `confirmedBy` | lifecycle `proof-confirmation` | `BugProof.cs:442,466` | D96: a person's call, cleared by a new attempt |
| draft `posted` | lifecycle `draft-posted` | `Drafts.cs:208,538` | D74: a person's act; not on the expected list, found by the sort |
| page run `stage` | lifecycle `page-run` | `PageRun.cs:98,137,170,288,299` | `check` branches on it |
| link run `stage` | lifecycle `link-run` | `LinkRun.cs:81,127,198,275,303` | Same |
| requirement run `stage` | lifecycle `req-run` | `RequirementRun.cs:52,120,146,180,209` | Same |
| ticket run `stage` | lifecycles `ticket-match`, `ticket-step` | `Match.cs:113,150,246`, `AiStep.cs:93` | Same |
| `RunStep.Status` (`retry`, `recorded`, `failed`, …) | result | `PageRun.cs:24` and its callers | What this command returned |
| fsd import `status` | result | `FsdImport.cs:47,92` | What this import did |
| attempt `outcome` | result | `BugProof.cs:50,64,417` | One attempt's result, appended and never changed |
| proof `verification` | result | `BugProof.cs:513` | What one `--verify` run found; replaced whole, cleared with the attempt |
| finder `status` in `points.json` | result, the closest call | `Points.cs:478,479,491,498,608` | Worked out again from inputs on each pull; its run's `stage` is the lifecycle |
| match `outcome` | result | `Match.cs:256` | A new match writes a new record |
| link `state` | result | `LinkRun.cs:325` | Part of the entry's id: it never moves, a change is a new entry |
| link and item `check` | result | `LinkRun.cs:327`, `RequirementRun.cs:476` | The checker's answer in that build |
| Jira `status` | result | `Ticket.cs:38,49`, `Bundle.cs:196` | Jira owns it |
| views' `status`, bundle `decision` and others | result | `RequirementFile.cs:130,144,170`, `Bundle.cs:234`, `Match.cs:87,209` | Copies |
| reports' `status` | result | `ImpactAnalyzer.cs:26,264`, `FsdImpact.cs:33`, `GraphStore.cs:220`, `TelemetryWriter.cs:51` | One run's field |
| phase `status` | lifecycle, not moved | `PhaseCheck.cs:26,85` | People edit it in Markdown; code only reads and compares it |
| `Staleness` | nothing kept | `Staleness.cs:14` | Worked out on each call |
| `failed.json` | result | `PageRun.cs:316` | A list of what failed, already on the page run's stage |

**Illegal moves real code was making:** none refused, and no existing test pinned one. Four legal-looking ones are allowed and questionable:
- `ticket-step` `failed -> done` and `done -> failed`: `check` on points, prove and drafts has no guard on the stage, as page, link, requirement and match runs do. A second `check` can record again.
- `proof-confirmation` `confirmed -> confirmed`: a second `--confirm` replaces the first person's name. The history keeps both; the file only the last.
- `trace-decision` by hand to `undecided`: `trace attribute` now writes the line, but still leaves the old `decided_by` in the file, as it did.
- A rebuild that doesn't find a decided link again loses the decision silently (its id is the quotes' hash, D58). It now leaves a line; the decision is still gone.

**Fixed while here.** `.stangent/fsd/` and `.stangent/req/` weren't in what `stangent init` ignores, so their run files could be committed. History lives there, so they're added now. `specs/trace-history.jsonl` is added to the CI bot's commit.

### Fresh check of 0.1 to 0.5
Found, and fixed in this round:
- A deleted `ids.json` gave out live ids again. `NextId` now raises the ledger to the highest id in the feature and question files.
- `stangent data check` passed with `trackedFiles: 0` when `git ls-files` failed (a corrupt index). It now stops with `git_list_failed`, exit 1. `InSameRepo` fails the same way when git can't answer for this repo.
- A term in NFC missed a path or line in NFD, so the finding printed the file name. Terms, paths and lines are now compared in NFC.
- The write scan missed `["status"] ??= x` and `.Add("status", x)`. It now sees both; it found no such write in `src/`, so nothing moved.

### Known gaps
- Write scan: it still misses a key held in a variable or `Field`, and `=` on the next line, and it matches excepted files by count only, so a new write can replace an old one unseen.
- Ids: `NextId` reads the feature files as code writes them, one `id:` per line, so an id hand-edited into a flow mapping isn't seen. A removed id can't be recovered once the ledger is deleted.
- 0.2: the line is written just before the file is saved; a person's YAML edit is seen only at `trace attribute`; requirement approval has no code path until D99, so a hand edit leaves no line; two branches can conflict on a committed history (no `merge=union` yet); agents may edit `.stangent/**`, history included (D60). `ApproveChapter`, `Attribute` and `trace check --fix` still save `trace.yaml` without the trace lock.
- 0.2: a retired requirement's id isn't marked `Remove`d on the ledger yet (2.3). `.stangent/graph.sqlite` isn't in what `init` ignores.
- `spec-run.sh` and `ticket-run.sh`: a page or step stangent *refuses* (an error envelope) is a status line holding its code, and the batch still exits 0. Tests pin this. Changing it could stop a paid benchmark on one ticket, so it's the owner's call.
- Not tested: `benchmark/sonarr-run-batch.sh` (a one-off with paths to a scratch folder that no longer exists; a candidate for deleting) and `benchmark/tickets/sonarr/app/{build,setup,start}.sh` (a few commands against Sonarr, proved by the benchmark's own runs).
- No test project loads `Stangent.Cli`, so its wiring is outside the coverage number.
- The data check reads the current tree, not git history. It doesn't look inside documents, and CI has no terms file.

### Impact on other phases
`evidence` is a new contract at v1 and nothing consumes it yet, so no version bump and no `needs review` (D20).
0.2 changes no artifact's shape. `specs/requirements/ids.yaml` became `ids.json`, read by nothing but `RequirementFile`, and an old file is taken over on first use.
Consumers move onto it when each is rebuilt (2.3, 2.4).
