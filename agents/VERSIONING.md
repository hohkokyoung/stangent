# Agent Versioning Policy

Every agent file in `agents/` and `agents/subagents/` has a `version` field in
its frontmatter (semver: `MAJOR.MINOR.PATCH`).

## When to bump

**MAJOR** — incompatible change to the agent contract:
- Input field added without a default, removed, or renamed
- Output value semantics change (e.g. `SUCCESS` → `OK`)
- A previously-required section in the feature file is no longer written
- A previously-optional section becomes required

**MINOR** — new capability, backward compatible:
- New optional input field with a sensible default
- New process phase added
- New section written to the feature file (alongside existing ones)
- New sub-agent spawned (additive)
- Direct/lightweight mode added (existing behaviour preserved as default)

**PATCH** — bug fix or wording cleanup:
- Typo fix in the prompt
- Phase renumbering with no behavioural change
- Clarification of an existing instruction
- Updated example or output format that's still parseable the same way

## Where the version shows up

- `version` field in agent frontmatter
- `*_agent_version` field in feature file frontmatter (recorded by orchestrator
  at each stage start)
- Run Log entries (`agent_version` field)

## Current versions (as of 2026-05-24)

| Agent | Version | Last bumped because |
|---|---|---|
| orchestrator | 1.1.0 | STEP 1g tier classification (additive) |
| planner | 1.2.0 | Direct Mode added (additive) |
| implementer | 1.1.0 | Targeted fix mode + Codebase Context reading |
| reviewer | 1.2.0 | Parallel specialist subagents (additive) |
| srs_agent | 1.0.0 | Initial |
| adr_agent | 1.1.0 | Bootstrap mode added |
| debug | 1.0.0 | Initial |
| subagents/linter | 1.0.0 | Initial |
| subagents/unit_tester | 1.1.0 | Three-outcome AC model |
| subagents/query_analyzer | 1.1.0 | DBHub MCP integration |
| subagents/security_scanner | 1.0.0 | Initial |
| subagents/performance_reviewer | 1.0.0 | Initial |
| subagents/quality_reviewer | 1.0.0 | Initial |

## How to keep this current

Whenever you edit an agent file, decide the bump level and update both:
1. The `version` field in the file
2. The row in this table

A prompt linter (`scripts/prompt_lint.py`) enforces that the version was bumped
when the body of an agent file changed (excluding pure whitespace/comment edits).
