# CLAUDE.md — stangent

A set of independent products that together run an agentic, as-deterministic-as-possible
software-engineering pipeline: requirements → code → tests → PR → deploy → tickets.

## Read first

**Every session:** this file and `roadmap.md`. Everything below it, look up when the work needs it.

| File | What it holds |
|---|---|
| `roadmap.md` | **Read this first.** Where we are, what's next, the phases, and everything decided but not built. |
| `questions.md` | Questions still open, sorted by when they need answering. |
| `decisions.md` | One line per decision (D1, D2, …). Full entries, with reason and cost, are in `decisions/`: `grep -n "^## D120 " decisions/*.md`. |
| `improvements.md` | Ideas not decided yet. Built or rejected ones are in `improvements-done.md`. |
| `spec.md` | The owner's original brief. Don't edit it except to add items the owner asks for. |
| `phases/` | One file per started phase: its plan and end-of-phase report. |
| `docs/phases.md` | How a phase is recorded, reported and changed. |
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
- **Record decisions as they're made.** In the same turn: the full entry at the end of the newest file in
  `decisions/`, one line in `decisions.md`, and an answered question moved out of `questions.md`.
- **An item's file is its status (D134).** Not decided: `improvements.md`. Decided, not built: `roadmap.md`.
  Built or rejected: `improvements-done.md`. When its state changes, move it. Never add a second copy or a mark.
- **Never assume across projects.** Nothing from other repos on this machine applies here.

## Rules for all work (D135)

Short forms. If one differs from its decision's full entry, the entry wins.

1. **The AI proposes and code checks (D18).** If code can't check it, a person approves.
2. **Phase changes ripple (D20).** When a finished phase changes a contract, bump its version and mark
   consumers `needs review` in `roadmap.md`; never change downstream phases automatically.
3. **Grill each phase when it starts (D22),** not all upfront.
4. **Git holds what people decide (D7, D25).** Anything code can regenerate stays out.
5. **One `stangent` CLI (D23, D110).** Products go through it, and its core stays C#.
6. **AI steps have three parts (D60).** The CLI prepares the input, an agent writes the output, the CLI checks it.
7. **A separate agent with a fresh context judges whether work is right (D61).** Paused while building (D120).
8. **Build first, test later (D120).** Every component gets a switch; anything that changes a prompt starts off.
9. **No paid step without a choice (D126).** A cost cap, or an explicit `none`.
10. **Nothing is dropped silently (D111, D116).** What can't be brought in exactly is listed.
11. **Permanent ids are never renumbered or reused (D117, D118).**
12. **No client data in this repo (D113).**

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
plugin/scripts/spec-run.sh --jobs 4 --max-cost 5 --for-fsd <doc>   # pages a document needs (I.1); --for-ticket <key>, --stale, --new, --all
plugin/scripts/spec-run.sh --jobs 4 --max-cost 5 --all    # --estimate shows the cost first; Phase 2 AI steps through headless Claude Code (STANGENT= to point at a build)
plugin/scripts/spec-run.sh --disprove <doc>               # D119: only the disprove stage (needs fsd.disprove; `fsd disprove estimate <doc>` is free)
plugin/scripts/ticket-run.sh --max-cost 5 --prove --questions <KEY>... # Phase 3: pull → points → bundle → proof → drafts, headless
stangent jira login                                       # browser sign-in to Atlassian's MCP v2; token in the OS keychain
```

Python analysis needs `python3` (3.9 or newer), as frontends need Node: without it, `python_missing` in `loadErrors` and the rest still builds. The extractor is `tools/py-extractor/stangent_py_extract.py`, one standard-library file, copied beside the stangent assemblies (`py-extractor/`); `STANGENT_PY_EXTRACTOR` points elsewhere. It only parses: an app's code is never imported or run.

Frontend analysis needs Node. The extractor bundle and TypeScript's lib files are copied beside the
stangent assemblies (`ts-extractor/`); `STANGENT_TS_EXTRACTOR` points elsewhere for development.
