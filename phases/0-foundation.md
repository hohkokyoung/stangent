---
phase: 0
name: foundation
status: done
consumes: {}
provides: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 2, benchmark: 1 }
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

