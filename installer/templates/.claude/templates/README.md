# Templates

Canonical shapes for the artifacts the system produces and consumes.

| File | What it shapes |
|---|---|
| `adr.md` | New ADRs (`/agentic-adr new` copies from this) |
| `task.md` | A single task file written by the planner |
| `overview.md` | The per-run `_overview.md` written by the planner |
| `feature-dossier.md` | Committed handoff doc for a parked run (`/agentic-defer` copies from this into `docs/features/`) |
| `skill.md` | A new `SKILL.md` when you add a stack |
| `test-case.md` | A registered regression case (`.claude/tests/cases/TC-NNN-slug.md`) |
| `screenshot-web.md` | `/agentic-screenshot` capture procedure — Playwright |
| `screenshot-mobile.md` | `/agentic-screenshot` capture procedure — Maestro (non-Flutter mobile) |
| `screenshot-flutter.md` | `/agentic-screenshot` capture procedure — flutter-skill |
| `agent.md` | A new role agent prompt — captures the shape every current agent follows |
| `eval-case/` | A directory template for adding a new eval case (`input.md`, `expect.md`, `assert.py`) |
| `blocker-reference.md` | Every valid `blocker:` value, by role |
| `evidence-policy.md` | The rules any report must follow before it clears or covers an item |
| `design-spec.md` | `docs/design/DESIGN-SPEC.md`, authored by `/agentic-design` |
| `design-tokens.md` | `docs/design/tokens.md`, the token table the spec references |
| `review-enumerations.md` | The declared search behind each site-based review checklist item |
| `ui-critique.md` | The design-critic's drift report |

## Rules

- Templates are **system-owned**: the installer mirror-replaces this directory on every install. Don't edit a template in an installed project and expect it to survive a re-install — edit the source in the stangent repo under `installer/templates/.claude/templates/` instead.
- Templates are **referenced**, not duplicated. Agent prompts and slash commands tell the agent to read the template at runtime, rather than inlining its content. This keeps prompts short and templates editable.
- When you add a new template here, register it in this README's table.
