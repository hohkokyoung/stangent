---
phase: 0
name: foundation
status: planned
consumes: {}
provides: { store: 1, artifact-base: 1, telemetry-event: 1, cli-conventions: 1, benchmark: 1 }
---

# Phase 0 · Foundation

**Draft plan, awaiting the owner's approval.**

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
- [ ] `dotnet tool install` from a local build gives a working `stangent`.
- [ ] In each benchmark repo: `stangent init`, put and get a sample artifact (rejected when it
      breaks the schema), delete the index, `stangent index rebuild` reproduces it exactly.
- [ ] Every command writes a valid telemetry event; nothing contains code or prompt text.
- [ ] `stangent phases check` flags a version mismatch in a test fixture.
- [ ] `/stangent:status` in Claude Code reports the store's contents through the CLI.
- [ ] CI green; tests (xUnit) cover the store, validation, index rebuild and phase check.
- [ ] End-of-phase report written below.

## Not in this phase
Adapters for Jira, GitHub PRs, GitLab and the archive: only their place in `stangent.yaml` is
reserved. Each is built in the phase that first needs it (D22).

## End-of-phase report
_Not started._
