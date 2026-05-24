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
| orchestrator | 1.3.0 | Inline Direct-tier planning (STEP 3a.1 — skips planner spawn) |
| planner | 1.3.0 | Efficiency rules + mode/phase compression (token-trim) |
| implementer | 1.2.0 | Efficiency rules + Edit-not-Write for spec sections |
| reviewer | 1.3.0 | Efficiency rules + Phase 3 spawn-template compression |
| srs_agent | 1.0.0 | Initial |
| adr_agent | 1.1.0 | Bootstrap mode added |
| debug | 1.0.0 | Initial |
| subagents/linter | 1.1.0 | Efficiency-rules link + light trim |
| subagents/unit_tester | 1.2.0 | Three-outcome model as table, efficiency-rules link |
| subagents/query_analyzer | 1.2.0 | Step-prose compression, efficiency-rules link |
| subagents/security_scanner | 1.1.0 | Shared pass-result rules, Edit-not-Write for report |
| subagents/performance_reviewer | 1.1.0 | Efficiency-rules link |
| subagents/quality_reviewer | 1.1.0 | Efficiency-rules link |

## How to keep this current

Whenever you edit an agent file, decide the bump level and update both:
1. The `version` field in the file
2. The row in this table

A prompt linter (`scripts/prompt_lint.py`) enforces that the version was bumped
when the body of an agent file changed (excluding pure whitespace/comment edits).
