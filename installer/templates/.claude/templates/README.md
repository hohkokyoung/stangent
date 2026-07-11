# Templates

Canonical shapes for the artifacts the system produces and consumes.

| File | What it shapes |
|---|---|
| `adr.md` | New ADRs (`/agentic-adr new` copies from this) |
| `task.md` | A single task file written by the planner |
| `overview.md` | The per-run `_overview.md` written by the planner |
| `feature-dossier.md` | Committed handoff doc for a parked run (`/agentic-defer` copies from this into `docs/features/`) |
| `skill.md` | A new `SKILL.md` when you add a stack |
| `agent.md` | A new role agent prompt — captures the shape every current agent follows |
| `eval-case/` | A directory template for adding a new eval case (`input.md`, `expect.md`, `assert.py`) |

## Rules

- Templates are **system-owned**: the installer mirror-replaces this directory on every install. Don't edit a template in an installed project and expect it to survive a re-install — edit the source in the stangent repo under `installer/templates/.claude/templates/` instead.
- Templates are **referenced**, not duplicated. Agent prompts and slash commands tell the agent to read the template at runtime, rather than inlining its content. This keeps prompts short and templates editable.
- When you add a new template here, register it in this README's table.
