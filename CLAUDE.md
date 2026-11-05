# CLAUDE.md — stangent

A set of independent products that together run an agentic, as-deterministic-as-possible
software-engineering pipeline: requirements → code → tests → PR → deploy → tickets.

## Read first

| File | What it holds |
|---|---|
| `spec.md` | The owner's original brief. Don't edit it except to add items the owner asks for. |
| `decisions.md` | Every settled decision (D1, D2, …) with its reason and cost, plus open questions. |
| `roadmap.md` | Phases, their dependencies, contract versions, status, and the end-of-phase report format. |
| `phases/` | One file per started phase: its plan and end-of-phase report. |
| `docs/cli-conventions.md` | What every `stangent` command prints and returns. |
| `docs/foundation.md` | The evidence contract, the permanent-id ledger, the client-data check, coverage, driver tests. |
| `docs/code-graph.md` | The code graph: points, lines, labels, detectors, Python, impact answers. |
| `docs/spec.md` | The spec: reference pages, the FSD import, links, findings and decisions (Phase 2). |
| `docs/tickets.md` | Tickets: Jira through the MCP, pull, starting points, the bundle, bug proof, drafts (Phase 3). |
| `benchmark/README.md` | Running the benchmark: full-history clones, per-repo configs, scoring commands. |

## How to work here

- **Grill before building.** The owner wants to be challenged: ask one question at a time,
  give a recommended answer, doubt assumptions, and say plainly when something won't work.
  Use plain language and concrete examples.
- **Record decisions as they're made.** Add or edit the entry in `decisions.md` in the same turn;
  move answered items out of "Open questions".
- **Never assume across projects.** Nothing from other repos on this machine applies here.
- **The AI proposes, code checks (D18).** Prefer code; every AI step needs a checker or a person's approval.
- **Phase changes ripple (D20).** When a finished phase changes a contract, bump its version and mark
  consumers `needs review` in `roadmap.md`; never change downstream phases automatically.

## Commands

```bash
dotnet build                     # everything (warnings are errors)
dotnet test                      # xUnit v3 on Microsoft.Testing.Platform
dotnet run --project src/Stangent.Cli -- phases check     # D20 dependency check
dotnet pack src/Stangent.Cli -c Release                   # → artifacts/packages/stangent.*.nupkg
benchmark/run.sh <label> <stangent args...>               # D6 benchmark; results/ is committed
dotnet run --project benchmark/Stangent.Benchmark -- <clone>   # D28 scoring (needs full history + a built graph)
npm --prefix tools/ts-extractor ci && npm --prefix tools/ts-extractor test   # the TypeScript extractor (D30)
npm --prefix tools/ts-extractor run build                 # → dist/, shipped by the dotnet build (built automatically if missing)
plugin/scripts/spec-run.sh --jobs 4 --max-cost 5 --all    # --estimate shows the cost first; Phase 2 AI steps through headless Claude Code (STANGENT= to point at a build)
plugin/scripts/spec-run.sh --disprove <doc>               # D119: only the disprove stage (needs fsd.disprove; `fsd disprove estimate <doc>` is free)
plugin/scripts/ticket-run.sh --max-cost 5 --prove --questions <KEY>... # Phase 3: pull → points → bundle → proof → drafts, headless
stangent jira login                                       # browser sign-in to Atlassian's MCP v2; token in the OS keychain
```

Python analysis needs `python3` (3.9 or newer), as frontends need Node: without it, `python_missing` in `loadErrors` and the rest still builds. The extractor is `tools/py-extractor/stangent_py_extract.py`, one standard-library file, copied beside the stangent assemblies (`py-extractor/`); `STANGENT_PY_EXTRACTOR` points elsewhere. It only parses: an app's code is never imported or run.

Frontend analysis needs Node. The extractor bundle and TypeScript's lib files are copied beside the
stangent assemblies (`ts-extractor/`); `STANGENT_TS_EXTRACTOR` points elsewhere for development.
