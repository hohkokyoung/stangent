# Stangent

AI-assisted feature development inside Claude Code. Describe a feature — Stangent plans it, implements it, reviews it, and documents it. You confirm each step.

No servers. No accounts. Just markdown files and Claude Code.

---

## How it works

```
/feature add login screen
    │
    ▼  ADR BOOTSTRAP (first feature only — detects existing patterns)
    ▼  PLANNING      — reads codebase, checks ADRs, surfaces risks, writes spec
    ▼  (you confirm)
    ▼  IMPLEMENTING  — writes code → linter → tests → query analysis
    ▼  REVIEWING     — spec compliance + 4-pass security scan
    ▼  SRS UPDATE    — extracts requirements into SRS.md
    ▼
  COMPLETE
    │
    └─ tested it and something's wrong?
         /refine FEAT-XXX <what's wrong>
              ▼  REFINING — planner revises spec from test feedback
              ▼  (you confirm revised spec)
              ▼  IMPLEMENTING  (clean retry, spec_version bumped)
              ▼  REVIEWING
              ▼
            COMPLETE
```

Everything is tracked in `.stangent/features/FEAT-XXX-slug.md`. Each agent owns its own sections — they never overwrite each other.

---

## Features

**Hard enforcement — not just instructions**
- A `PreToolUse` gateway hook intercepts every file write and bash command. Agents physically cannot write outside the spec's `## Files to Touch` list or run destructive commands (`git push --force`, `rm -rf`, `DROP TABLE`). This runs at the OS hook level, not as a prompt suggestion.
- Six enforcement layers: hard bash blocks → agent/state check → blocked paths → allowed paths whitelist → feature bash blocklist → per-agent capability caps.
- Section ownership in the feature file is structural — each section has a named owner and the gateway enforces it. The planner cannot overwrite the review verdict. The implementer cannot alter the spec after confirmation.

**Quality gates on every feature**
- **Complexity tiers** — every request is auto-classified `direct` or `standard`. A visual fix, copy change, or single-file bug runs a lightweight planner (no full codebase scan, no risk analysis) and a lighter reviewer. Small fixes don't pay for big-feature overhead — token cost on tiny changes drops 60–70%.
- **Spec-driven review** — reviewer checks only against what was specified. No unsolicited refactors, no gold-plating. CRITICAL and MAJOR findings come with exact `file:line` references and required fixes.
- **Parallel specialist reviews** — security, performance, and quality reviewers spawn in parallel after spec compliance, then findings consolidate into one verdict.
- **4-pass security scan** — secrets detection, SAST (bandit / dart_code_metrics), dependency CVE audit, and hardcoded config detection. Any CRITICAL finding blocks the commit.
- **Surgical retries** — when a feature fails review, the implementer receives the exact findings and enters targeted fix mode. It touches only the flagged lines, not the whole feature.
- **Sub-agent pipeline** — linter → unit tester → query analyzer must each pass before the implementer can commit. Each has a configurable retry ceiling before escalating to the developer.
- **Impact & risk analysis** — before writing a standard-tier spec, the planner reasons across six dimensions: breaking changes, state/data migration, backward compatibility, fallback/degradation, feature flag need, and rollback complexity. Risks that need a decision are surfaced to the developer outside the clarifying-question budget — they are never silently skipped.
- **Spec-first refinement** — if you test a completed feature and it's not right, `/refine FEAT-XXX <what's wrong>` triggers a lightweight planner revision pass. The planner reads the test feedback, identifies which spec sections caused the gap, revises only those sections, and reimplements from the updated spec. Avoids the drift that comes from ad-hoc conversational corrections on a stale plan.

**Observability and ad-hoc debugging**
- **Token & file tracking** — a PostToolUse hook logs every file read, write, edit, bash run, and search to `.stangent/logs/{feature_id}.jsonl` with char counts. Run `/stats` for a per-agent breakdown of where the tokens went.
- **`/debug` for bugs** — conversational investigation (understand → narrow → root cause → fix → wrap up) for problems that don't need a full feature spec. Escalates to `/plan` if the fix turns out to be a real feature.
- **Gateway audit log** — every blocked tool call recorded with reason; `/skill gateway-audit` summarises patterns.

**Architectural decisions that stick**
- `/adr` bootstraps existing patterns automatically on first run (detects your ORM, state manager, HTTP client, test framework from the codebase).
- Every ADR is binding — planner flags contradictions before writing a spec, reviewer blocks any implementation that violates a decision, SRS logs every override with its reason.

**Cross-feature memory**
- After each completed feature the orchestrator writes to `.stangent/memory.md`: failure patterns (which files keep breaking), developer preferences (things you've corrected agents on), and project insights.
- The planner reads memory before asking questions — it silently applies known preferences and proactively flags known risk areas. You don't get asked the same thing twice.
- DBHub MCP integration: if enabled, the query analyzer runs real `EXPLAIN` queries and checks for missing indexes rather than just reading code.

**Cross-stack coordination (Flutter + FastAPI + Supabase)**
- The planner detects double-stack projects and automatically cascades spec scope: touching a FastAPI schema pulls the Flutter Dart model into scope; touching a route pulls the Flutter service method. Defined in `.stangent/meta.md`.
- The reviewer runs a cross-stack drift check — field-by-field Pydantic→Dart type parity, nullable mismatch detection, JSON key casing verification, and new-endpoint→service method coverage. A missing Dart field for an `Optional[X]` Pydantic field is a MAJOR finding (runtime crash).
- Supabase integration: architecture detection, RLS enforcement (new table without `ENABLE ROW LEVEL SECURITY` = MAJOR), service_role key in Flutter code = CRITICAL, JWT middleware verification in FastAPI, realtime subscription cleanup. Rules are enforced by both the implementer (as hard constraints) and the reviewer (as a dedicated security phase).

**Flexible by default**
- 7 providers out of the box: Anthropic, OpenAI, Groq, OpenRouter, Bedrock, Vertex, Ollama. Per-agent model assignment — use a fast model for linting, strong model for planning.
- Python, FastAPI, and Flutter profiles included. Each profile defines its own lint commands, test patterns, security toolchain, review checklist, and query danger patterns. Adding a new language is one markdown file.
- No servers, no accounts, no cloud state. Everything lives in your repo as plain markdown and JSON.

---

## Requirements

- **Claude Code** (desktop or CLI)
- **Python 3.10+** (for `init.py` only)
- **Git**

---

## Install

**1. Clone somewhere permanent:**
```bash
git clone https://github.com/yourname/stangent.git ~/stangent
```

**2. Set your API key** (Stangent auto-detects from env):

| Provider | Env var |
|----------|---------|
| Anthropic | `ANTHROPIC_API_KEY` |
| Groq | `GROQ_API_KEY` |
| OpenRouter | `OPENROUTER_API_KEY` |
| OpenAI | `OPENAI_API_KEY` |
| Bedrock | `AWS_ACCESS_KEY_ID` + `AWS_SECRET_ACCESS_KEY` |
| Vertex | `GOOGLE_CLOUD_PROJECT` |
| Ollama | *(none — use `--provider ollama`)* |

**3. Global install** (once per machine — puts agents + commands in every project):
```bash
python ~/stangent/init.py --global
```

**4. Init each project:**
```bash
cd your-project
python ~/stangent/init.py
```

Flags: `--provider <name>` · `--profile python,flutter` · `--dry-run` · `--verify`

To remove Stangent from a project:
```bash
python ~/stangent/init.py --uninit         # keep .stangent/ data
python ~/stangent/init.py --uninit --hard  # delete everything
```

---

## Commands

| Command | What it does |
|---------|-------------|
| `/feature <desc>` | Full pipeline — plan → implement → review → SRS |
| `/plan <desc>` | Write spec only, stop before implementation |
| `/implement FEAT-XXX` | Implement a confirmed spec |
| `/refine FEAT-XXX <feedback>` | Revise spec from test feedback and reimplement |
| `/resume FEAT-XXX` | Resume a paused or interrupted feature |
| `/review FEAT-XXX` | Re-run review independently |
| `/srs [FEAT-XXX]` | Update SRS.md for completed features |
| `/adr <title>` | Record an architectural decision |
| `/status [FEAT-XXX]` | Feature dashboard |
| `/abandon FEAT-XXX` | Cancel a feature — reverts code, archives spec, deletes branch |
| `/doctor` | Validate config, agents, gateway wiring |
| `/cleanup` | Remove stale branches and contracts |
| `/gateway <status\|unblock\|pause\|resume>` | Manage enforcement |
| `/uninit [--hard]` | Remove Stangent from this project |

Full docs for each command: `.stangent/HOW_THIS_WORKS.md` (generated in your project after init).

---

## Profiles (supported languages)

| Profile | Detects | Linter | Tests |
|---------|---------|--------|-------|
| `python` | `pyproject.toml`, `requirements.txt` | ruff | pytest + bandit + pip-audit |
| `fastapi` | `fastapi` in requirements / pyproject | ruff + ASYNC rules | pytest-asyncio + httpx + bandit + pip-audit |
| `flutter` | `pubspec.yaml` | dart analyze | flutter test |

`fastapi` auto-detects and takes precedence over `python` when FastAPI is found as a dependency. Use `--profile fastapi` to select explicitly.

**Flutter + FastAPI projects:** copy [`templates/meta_flutter_fastapi.md`](templates/meta_flutter_fastapi.md) to `.stangent/meta.md` and fill in your route-to-service mappings. The planner will automatically cascade spec scope across both stacks. Type mappings live in [`prompts/cross-stack-types.md`](prompts/cross-stack-types.md).

**Supabase projects:** set `integrations.supabase.enabled = true` in config and add your project URL. Agents load [`prompts/supabase.md`](prompts/supabase.md) automatically — security rules, RLS enforcement, and architecture detection are applied to every feature.

Add a new language: see [`templates/profile_guide.md`](templates/profile_guide.md).

---

## Key config (`stangent/config.json`)

```json
{
  "pipeline": {
    "max_retries": 3,
    "max_replans": 2,
    "sub_agent_max_retries": 3,
    "branch_prefix": "stangent/",
    "pr_target_branch": "dev"
  },
  "models": {
    "orchestrator": "claude-sonnet-4-6",
    "planner":      "claude-sonnet-4-6",
    "implementer":  "claude-sonnet-4-6",
    "reviewer":     "claude-sonnet-4-6",
    "linter":       "claude-haiku-4-5-20251001",
    "unit_tester":  "claude-haiku-4-5-20251001"
  },
  "integrations": {
    "dbhub":     { "enabled": false, "mcp_server": "dbhub" },
    "supabase":  { "enabled": false, "project_url": null, "direct_connection": null }
  }
}
```

Smart merge on re-run — your edits are preserved, new fields are added automatically.

---

## Upgrading

```bash
cd ~/stangent && git pull
cd your-project && python ~/stangent/init.py
```

---

## Troubleshooting

**Something broken?** Run `/doctor` — it checks config, agents, gateway hook, and registry.

**Feature paused or stuck?** Run `/resume FEAT-XXX` — it reads Pipeline History and routes to the right stage automatically.

**ESCALATED after 3 retries?** Read `## Review Verdict` in the feature file, fix manually, set `status = CONFIRMED`, then `/resume FEAT-XXX`.

**Implemented but not what you wanted?** Use `/refine FEAT-XXX <description of what's wrong>`. Don't try to correct it through conversation — that produces drift. `/refine` updates the spec first, then reimplements cleanly.

**Wrong provider detected?** `python init.py --provider <name>`

**Using Cursor?** Gateway hook enforcement requires Claude Code. In Cursor, copy the agent instructions to `.cursorrules` or your system prompt — the gateway will operate in advisory mode (logs violations, does not block).

---

## Roadmap

**Language profiles**
- [x] FastAPI — async route handler checklist, Pydantic v2 patterns, SQLAlchemy 2.x async query patterns, pytest-asyncio, auto-detects from dependencies
- [ ] TypeScript / Node.js — the most-requested missing profile (ESLint, Jest, npm audit, Semgrep)
- [ ] Go — golangci-lint, go test, govulncheck
- [ ] Ruby — RuboCop, RSpec, bundler-audit
- [ ] Flutter web / desktop — current profile targets mobile only; web and desktop have different test patterns and no platform channel concerns to skip

**Cross-stack (Flutter + FastAPI + Supabase)**
- [x] `meta_flutter_fastapi.md` starter template — copy to `.stangent/meta.md` to get cascade rules: changing a FastAPI schema automatically pulls the Flutter model into spec scope
- [x] API contract drift check — reviewer Phase 6 checks field-by-field Pydantic→Dart type parity, nullable mismatch, JSON key casing, and new-endpoint→service method coverage; field mismatch or nullable divergence = MAJOR finding
- [x] Cross-stack type mapping table — [`prompts/cross-stack-types.md`](prompts/cross-stack-types.md) maps every Pydantic type to its Dart equivalent; used by planner, implementer, and reviewer
- [x] Supabase integration — architecture detection, RLS/policy enforcement on migrations, service_role security rules, JWT middleware verification, realtime subscription cleanup; cascade rules in `meta_flutter_fastapi.md`
- [ ] Cross-stack feature mode — `/feature --stack flutter+fastapi` runs both profiles' sub-agents and gates the commit on both passing; single spec covers both codebases

**Integrations**
- [ ] GitHub MCP — `/ci` command: after `/pr`, monitor CI run status and auto-fix failures (targeted implementer retry using the failed step log), then re-push. Loops until CI passes or `ci_fix_max_retries` is reached.
- [ ] Linear MCP — `/feature` accepts a Linear ticket ID and imports title + ACs directly; marks the ticket Done when the feature reaches COMPLETE
- [ ] Jira MCP — same as Linear for teams on Atlassian
- [ ] Slack / Teams MCP — notify a channel when a feature needs confirmation, escalates, or completes

**Pipeline improvements**
- [ ] Automated profile validation in `init.py` — check that every field in `_base.md` is present in a new profile before installing it, not just at authoring time
- [ ] SRS and ADR eval coverage — both agents currently have zero eval cases; adding them would let you catch regressions when changing those agents
- [ ] Dependency change guard — if the implementer touches `pyproject.toml`, `pubspec.yaml`, or `package.json`, require explicit developer confirmation before the commit (currently a WARN, not a gate)
- [ ] Parallel sub-agents — linter and query analyzer have no ordering dependency; running them in parallel would cut implementing stage time on large features

**Memory & retrieval**
- [ ] SQLite memory store — move `memory.md` entries into `.stangent/memory.db`; planner queries by file path overlap with `## Files to Touch` instead of loading the entire history; queryable via DBHub MCP if enabled
- [ ] Hot memory layer — keep a condensed `memory_hot.md` (≤30 lines: last 5 features + top recurring preferences) as the always-loaded layer; full history goes to SQLite
- [ ] Vector RAG memory — optional embedding-based retrieval via memory MCP for semantic search across failure patterns and project insights; falls back to SQLite if not configured

**Developer experience**
- [ ] `/pr` CI status check — after creating the PR, poll GitHub Actions once and surface pass/fail inline rather than requiring the developer to check GitHub separately
- [ ] `init.py --upgrade` — diff installed agent files against the latest stangent source and show what changed before overwriting, so developers know what a re-init will affect
- [ ] VS Code extension — wrap `/feature`, `/status`, and `/doctor` as sidebar buttons rather than slash commands typed in the Claude Code chat

---

## Design principles

- **Agents own sections, not files** — conflicts are structurally impossible
- **The spec is the contract** — reviewer checks against spec only, no gold-plating
- **ADRs are binding** — record a decision once, every agent enforces it forever
- **Retries are surgical** — failures come with exact `file:line` fix instructions
