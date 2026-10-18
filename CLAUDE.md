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
| `docs/code-graph.md` | The code graph: points, lines, labels, detectors, impact answers. |

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
```
