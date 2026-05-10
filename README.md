# Stangent

An AI-assisted feature development framework that runs entirely inside Claude Code. You describe a feature in plain English — Stangent plans it, implements it, reviews it, and documents it, with you confirming each step.

No servers. No accounts. No framework lock-in. Just markdown files and your existing Claude Code setup.

---

## How it works

Every feature goes through a fixed pipeline:

```
/feature add login screen
    │
    ▼ (first feature only)
ADR BOOTSTRAP  — Scans codebase for established patterns (frameworks,
                 ORMs, state management). You confirm which become binding
                 architectural decisions. Skipped on subsequent features.
    │
    ▼
PLANNING       — Planner reads your codebase and all ADRs. Flags any
                 conflicts between your request and existing decisions
                 before writing a single line of spec. Asks ≤5 questions.
    │
    ▼ (you confirm the spec)
    │
IMPLEMENTING   — Implementer writes code, then automatically runs:
                 linter → unit tests → query safety analysis
    │
    ▼
REVIEWING      — Reviewer checks every AC against the implementation,
                 runs a 4-pass security scan, issues PASS or FAIL
    │
    ▼ (auto-retry up to 3× on FAIL, with exact fix instructions)
    │
SRS UPDATE     — SRS agent documents the feature in your SRS.md,
                 extracts API contracts and data models
    │
    ▼
COMPLETE       — branch ready to PR
```

Everything is tracked in `.stangent/features/FEAT-XXX-slug.md` — one file per feature, with strict section ownership per agent so they never overwrite each other.

---

## Requirements

- **Claude Code** (desktop app or CLI)
- **Python 3.10+** (for `init.py` only — agents run inside Claude Code, not Python)
- **Git** (required — Stangent creates a branch per feature)

---

## Installation

### Step 1 — Clone the repo (once per machine)

Clone Stangent somewhere permanent on your machine. It needs to stay there — your projects will reference it.

```bash
# Good locations: home directory, a dedicated tools folder
git clone https://github.com/yourname/stangent.git ~/stangent

# Windows
git clone https://github.com/yourname/stangent.git C:/tools/stangent
```

> **Don't move or delete this folder later.** Your project configs store the path to it.
> If you do move it, re-run `init.py` in each project and the path will update automatically.

### Step 2 — Set up your API key

Stangent auto-detects which LLM provider to use based on which API key is in your environment.

Set one of these in your shell profile (`.zshrc`, `.bashrc`, `$PROFILE` on Windows), or in a `.env` file at your project root:

| Provider | Environment variable | Notes |
|----------|---------------------|-------|
| Anthropic | `ANTHROPIC_API_KEY` | Default. Best quality. |
| Groq | `GROQ_API_KEY` | Free tier. Very fast. |
| OpenRouter | `OPENROUTER_API_KEY` | Has free models. |
| OpenAI | `OPENAI_API_KEY` | GPT-4o family. |
| AWS Bedrock | `AWS_ACCESS_KEY_ID` + `AWS_SECRET_ACCESS_KEY` | |
| Google Vertex | `GOOGLE_CLOUD_PROJECT` | |
| Ollama | *(no key needed)* | Fully local. Use `--provider ollama`. |

**macOS / Linux** — add to `~/.zshrc` or `~/.bashrc`:
```bash
export ANTHROPIC_API_KEY=sk-ant-...
```

**Windows** — add to PowerShell profile or set as a system environment variable:
```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
# Or permanently via: System Properties → Environment Variables
```

### Step 3 — Install globally into Claude Code (once per machine)

This makes all Stangent agents and commands available in every project automatically:

```bash
# macOS / Linux
python ~/stangent/init.py --global

# Windows
python C:/tools/stangent/init.py --global
```

You should see:
```
✓ Global agents installed   → ~/.claude/agents/
✓ Global commands installed → ~/.claude/commands/
```

Restart Claude Code after this step.

### Step 4 — Initialise each project (once per project)

```bash
cd your-project

# macOS / Linux
python ~/stangent/init.py

# Windows
python C:/tools/stangent/init.py
```

This creates:
```
your-project/
├── .stangent/
│   ├── config.json          ← project config (committed)
│   ├── features/            ← one file per feature (committed)
│   ├── archive/             ← completed/abandoned features
│   ├── logs/                ← JSON Lines run logs (gitignored)
│   ├── SRS.md               ← system requirements spec (committed)
│   ├── decisions.md         ← architectural decision records (committed)
│   └── HOW_THIS_WORKS.md    ← quick reference
└── .claude/
    ├── commands/            ← slash commands for this project
    └── agents/              ← dropdown agents for this project
```

**Override auto-detection if needed:**
```bash
# Specify provider explicitly
python ~/stangent/init.py --provider groq

# Specify profile (language) explicitly
python ~/stangent/init.py --profile flutter

# Multi-language project (monorepo)
python ~/stangent/init.py --profile flutter,python

# Dry run — see what would be created without writing anything
python ~/stangent/init.py --dry-run
```

> Replace `~/stangent` with wherever you cloned the repo (e.g. `C:/tools/stangent` on Windows).

---

## Supported languages (profiles)

| Profile | Detects | Linter | Tests | Notes |
|---------|---------|--------|-------|-------|
| `python` | `pyproject.toml`, `requirements.txt`, `setup.py` | ruff | pytest | bandit for SAST, pip-audit for CVEs |
| `flutter` | `pubspec.yaml` | dart analyze | flutter test | detect-secrets optional |

To add a new language, see [`templates/profile_guide.md`](templates/profile_guide.md).

---

## Commands

Open Claude Code in your project and use these slash commands:

### `/feature <description>`
Run the full pipeline for a new feature. The most common command.

```
/feature add a login screen with email and password
/feature allow users to export their data as CSV
/feature send push notifications when an order ships
```

The pipeline runs automatically through all stages. It pauses at two points:
1. After planning — to confirm the spec before any code is written
2. After each implementation attempt — shows the full diff for your approval

---

### `/plan <description>`
Write a spec only. No code is written.

```
/plan add a login screen with email and password
```

Useful when you want to plan ahead, share the spec with teammates, or review it before committing to implementation.

After `/plan`, the feature is in `AWAITING_CONFIRMATION` state. Start implementation later with:
```
/implement FEAT-001
```

---

### `/implement <FEAT-ID>`
Run the implementation stage for a planned feature.

```
/implement FEAT-001
```

The feature must be in `CONFIRMED` or `AWAITING_CONFIRMATION` state.

What it does:
1. Sets up the IMPLEMENTING pipeline state
2. Reads the spec and your codebase
3. Implements every acceptance criterion
4. Runs linter → tests → query analysis (in order, each must pass before the next)
5. Shows you the full diff and asks for confirmation before committing
6. Automatically continues to the review stage

---

### `/review <FEAT-ID>`
Re-run the review stage for an already-implemented feature.

```
/review FEAT-001
```

Use this after manually fixing issues, or when you want to review independently from the main pipeline.

The reviewer:
- Checks every acceptance criterion is implemented and tested
- Checks for scope creep (code outside the spec)
- Checks all architectural decisions are honoured
- Runs a 4-pass security scan (secrets, SAST, dependency CVEs, hardcoded config)
- Issues `PASS` or `FAIL` with exact `file:line` references

---

### `/status`
Dashboard of all features in the project.

```
/status              ← full dashboard
/status FEAT-001     ← one feature in detail
```

Dashboard groups features by state: Active, Awaiting Your Input, Blocked, Escalated, Complete, Abandoned.

---

### `/srs <FEAT-ID>`
Update the System Requirements Specification after a feature completes.

```
/srs FEAT-001        ← update SRS for one feature
/srs                 ← process all completed features since last SRS update
```

The SRS agent automatically extracts:
- Functional requirements (from accepted criteria)
- API contracts (Python projects)
- Data models (new classes/schemas)
- Environment variables
- Security notes from the security report

---

### `/adr <title>`
Record an architectural decision.

```
/adr use Riverpod for state management
/adr store sessions in Redis, not in-memory
/adr list            ← show all ADRs
/adr show ADR-003    ← show one ADR in full
```

The ADR agent asks targeted questions (context, alternatives considered, rationale), drafts the ADR, and you confirm before it's written to `decisions.md`.

All future agents automatically read and honour every ADR in `decisions.md`.

---

### `/abandon <FEAT-ID>`
Cleanly abandon a feature.

```
/abandon FEAT-003
```

This:
- Sets status to ABANDONED
- Moves the feature file to `.stangent/archive/`
- Deletes the git branch if it has no commits
- Preserves the branch if it has commits (for manual cleanup)

---

## Feature pipeline states

| State | Meaning | Next action |
|-------|---------|-------------|
| `CREATED` | Feature ID assigned, no spec yet | Run `/plan` |
| `PLANNING` | Planner is running | Wait |
| `AWAITING_CONFIRMATION` | Spec written, waiting for you | Review spec, run `/implement` |
| `CONFIRMED` | Spec approved | Runs automatically |
| `IMPLEMENTING` | Implementer is running | Wait |
| `REVIEWING` | Reviewer is running | Wait |
| `REVIEW_PASS` | Passed review | Run `/srs` or done |
| `COMPLETE` | SRS updated, all done | Create PR |
| `PAUSED` | Pipeline paused waiting for input | Resume with the relevant command |
| `ESCALATED` | Hit max retries (default 3) | Fix manually, then run `/implement` |
| `BLOCKED` | A dependency feature isn't complete | Complete the blocking feature first |
| `ABANDONED` | Abandoned by developer | — |

---

## Configuration

`.stangent/config.json` is created by `init.py` and committed to git. You can edit it directly.

Key settings:

```json
{
  "stangent_path": "/path/to/stangent",
  "profile": "python",
  "profiles": ["python"],
  "profile_roots": { "python": "src/" },

  "pipeline": {
    "max_retries": 3,
    "branch_prefix": "stangent/",
    "remind_pr_on_complete": false,
    "pr_target_branch": "dev"
  },

  "models": {
    "orchestrator": "claude-sonnet-4-6",
    "planner":      "claude-sonnet-4-6",
    "implementer":  "claude-sonnet-4-6",
    "reviewer":     "claude-sonnet-4-6",
    "srs_agent":    "claude-sonnet-4-6",
    "linter":       "claude-haiku-4-5-20251001",
    "unit_tester":  "claude-haiku-4-5-20251001",
    "query_analyzer": "claude-haiku-4-5-20251001",
    "security_scanner": "claude-sonnet-4-6"
  },

  "integrations": {
    "dbhub": {
      "enabled": false,
      "mcp_server": "dbhub"
    }
  }
}
```

**Models:** Strong agents (planner, implementer, reviewer) use your `strong` model. Cheap agents (linter, tester, query analysis) use `fast`. Mix and match as needed — the fast ones run many times, the strong ones run once.

**`max_retries`:** How many times the pipeline retries before escalating to you. Default: 3.

**`remind_pr_on_complete`:** Set to `true` to get a reminder to create a PR when a feature reaches COMPLETE.

---

## What gets committed to git

Stangent creates a branch per feature (`stangent/FEAT-001-slug`). Everything in `.stangent/` except logs is committed:

| File/dir | Committed | Why |
|----------|-----------|-----|
| `.stangent/config.json` | ✓ | Team needs the same config |
| `.stangent/features/` | ✓ | Feature specs are documentation |
| `.stangent/archive/` | ✓ | History of completed work |
| `.stangent/SRS.md` | ✓ | Living requirements document |
| `.stangent/decisions.md` | ✓ | ADRs govern future agents |
| `.stangent/logs/` | ✗ | Machine-generated, gitignored |
| `.stangent/context_cache.md` | ✗ | Machine-generated, gitignored |
| `.stangent/coverage_baseline.json` | ✗ | Machine-generated, gitignored |

---

## Architectural Decision Records (ADRs)

ADRs live in `.stangent/decisions.md`. Every agent reads this file before doing anything. Once an ADR is `Accepted`, all agents automatically apply it.

### Auto-bootstrap on first feature

When you run `/feature` for the first time, Stangent scans your codebase before planning and detects patterns already in use — frameworks, ORMs, HTTP clients, state management libraries. It presents candidates and you pick which ones become binding decisions:

```
I scanned your codebase before starting the first feature.
These architectural patterns are already established in your code.
Which should become binding decisions?

1. State Management: Riverpod
   Detected in: pubspec.yaml (flutter_riverpod: ^2.4.0)
   Would enforce: All new screens must use ConsumerWidget, not StatefulWidget

2. HTTP Client: Dio
   Detected in: pubspec.yaml, lib/api/client.dart
   Would enforce: All HTTP calls must go through the Dio client

Reply with numbers to accept (e.g. "1, 2"), "all", or "none".
```

Confirmed ones are written as ADRs immediately. From that point forward, every feature automatically follows them.

### Contradiction detection

Before writing any spec, the planner checks your request against existing ADRs. If something conflicts, it surfaces it before a single line of code is planned:

```
⚠️ Before writing the spec, I found conflicts with existing decisions:

ADR-001 — State Management: Riverpod
Rule: All new screens must use ConsumerWidget, not StatefulWidget
Conflict: Your request describes a login screen using StatefulWidget

Options:
  A — Adjust the feature approach to comply with ADR-001
  B — Override ADR-001 for this feature (reason required)
  C — Cancel this feature
```

### Manual ADRs

Record explicit decisions any time with `/adr`:

```
/adr use SQLAlchemy ORM, never raw SQL
/adr store sessions in Redis, not in-memory
```

This is the highest-leverage thing you can do: record decisions once, enforce them forever.

---

## Multi-language projects (monorepo)

Yes — initialise from the project root. Stangent auto-detects each profile's location by searching one level deep for detection files (`pubspec.yaml`, `pyproject.toml`, etc.), so a structure like this works automatically:

```
your-project/
├── mobile/               ← Flutter app
│   ├── pubspec.yaml      ← detected here
│   └── lib/
├── backend/              ← Python API
│   ├── pyproject.toml    ← detected here
│   └── src/
└── .stangent/            ← created here, at root
```

```bash
cd your-project
python ~/stangent/init.py --profile flutter,python
```

Stangent will detect both apps and generate the correct roots automatically:

```json
{
  "profiles": ["flutter", "python"],
  "profile_roots": {
    "flutter": "mobile/lib/",
    "python":  "backend/src/"
  }
}
```

Agents use `profile_roots` to decide which rules apply to which file — Flutter lint rules for `.dart` files under `mobile/`, Python rules for `.py` files under `backend/`. You never have to think about it.

If auto-detection gets the roots wrong (unusual structure), just edit `profile_roots` in `.stangent/config.json` directly.

---

## Integrations

### DBHub (database schema queries)

[DBHub](https://github.com/bytebase/dbhub) is an MCP server by Bytebase that connects agents to your actual database. When enabled, Stangent uses it in two places:

- **Planner** — queries the real schema via `search_objects` instead of inferring it from migration files. Gets accurate column names, types, indexes, and foreign keys before writing the spec.
- **Query Analyzer** — verifies that indexes exist on every column being filtered or joined, and runs `EXPLAIN` on flagged queries to confirm if a full table scan actually occurs.

**Setup:**

```bash
# 1. Install
npm install -g @bytebase/dbhub

# 2. Register with Claude Code (use the CLI — edits ~/.claude.json correctly)
claude mcp add --scope user dbhub -- npx @bytebase/dbhub --transport stdio --dsn "YOUR_DSN"
```

DSN format by database:
| Database | DSN |
|----------|-----|
| PostgreSQL | `postgres://user:pass@host:5432/dbname` |
| MySQL | `mysql://user:pass@host:3306/dbname` |
| SQLite | `sqlite:///absolute/path/to/db.sqlite` |
| SQL Server | `sqlserver://user:pass@host:1433?database=dbname` |

**Then enable in `.stangent/config.json`** (or let `init.py` do it for you):
```json
"integrations": {
  "dbhub": {
    "enabled": true,
    "mcp_server": "dbhub"
  }
}
```

Restart Claude Code after registering the MCP.

> **Common gotchas**
> - **Don't edit `settings.json` manually** — Claude Code reads MCPs from `~/.claude.json`. Use `claude mcp add` above.
> - **Special chars in password** must be URL-encoded: `!→%21` `@→%40` `#→%23` `$→%24` `%→%25`
> - **Supabase** — direct connections are IPv6-only and fail in Node. Use the Session Pooler URL instead:
>   `postgres://postgres.[ref]:[pass]@aws-0-[region].pooler.supabase.com:5432/postgres?sslmode=require`
> - **ESM/CJS crash** on Windows (`SyntaxError: Cannot use import statement outside a module` in `ssh-config.js`):
>   Add `"type": "module"` to `<npm-global>/node_modules/@bytebase/dbhub/node_modules/ssh-config/package.json`

---

## Evals

Test that agents behave correctly:

```bash
# Run all evals
python evals/eval_runner.py

# Run evals for one agent only
python evals/eval_runner.py --agent planner

# Use a different provider
python evals/eval_runner.py --provider groq

# Save results to JSON
python evals/eval_runner.py --output results.json
```

Eval cases live in `evals/planner/`, `evals/implementer/`, etc. Each case is three files:
- `case_01_input.md` — the input to the agent
- `case_01_expect.md` — phrases the response must/must not contain
- `case_01_assert.py` — Python assertions against the response

---

## Adding a new language profile

1. Copy `profiles/_base.md` → `profiles/<language>.md`
2. Fill in all required fields (commands, anchor files, review checklist, query patterns)
3. Add detection logic to `init.py` PROFILES dict
4. Add eval cases under `evals/planner/`
5. Run evals: `python evals/eval_runner.py --agent planner`

Full guide: [`templates/profile_guide.md`](templates/profile_guide.md)

---

## Switching providers

You can switch LLM provider at any time — one command:

```bash
cd your-project
python ~/stangent/init.py --provider anthropic
```

What changes:
- `config.json` `provider` block → updated to the new provider
- `config.json` `models` → **reset to the new provider's defaults** (because model names are provider-specific — Groq model IDs don't work on Anthropic and vice versa)
- Everything else (pipeline settings, profile, paths) → unchanged

You'll see:
```
✓ .stangent/config.json — provider switched (groq → anthropic), models reset to defaults
```

If you had custom model names set (e.g. a specific Claude version), re-edit `config.json` after switching. The model section is the only thing that resets.

Make sure the new provider's API key is in your environment before switching:

```bash
export ANTHROPIC_API_KEY=sk-ant-...   # then re-run init.py --provider anthropic
```

---

## Upgrading

When a new version is released, pull the latest changes and re-run `init.py` in each project:

```bash
# 1. Pull updates from GitHub
cd ~/stangent        # or wherever you cloned it
git pull

# 2. Re-run in each of your projects
cd ~/your-project
python ~/stangent/init.py
```

Or just re-run `init.py` in each project:

```bash
cd your-project
python C:/path/to/stangent/init.py
```

What happens on re-run:

| What | Behaviour |
|------|-----------|
| Commands (`.claude/commands/`) | Replaced with latest versions. Stale commands removed. |
| Agents (`.claude/agents/`) | Replaced with latest versions. Stale agents removed. |
| `HOW_THIS_WORKS.md` | Regenerated. |
| `.stangent/config.json` | **Smart merge** — your customised values are kept. New fields from the template are added. Renamed keys are migrated automatically. |
| `SRS.md`, `decisions.md` | Never touched if they already exist. |
| `.gitignore` | New entries appended. Existing entries left alone. |

The config merge means you never lose settings you've changed (models, retry count, PR branch, etc.), but you automatically get any new config fields added in the upgrade.

You can preview what would change without writing anything:

```bash
python init.py --dry-run
```

---

## Troubleshooting

**"Stangent is not initialised in this project"**
Run `python path/to/stangent/init.py` in your project root.

**"Profile 'X' not found"**
The profile `.md` file is missing from the stangent installation.
Run `python init.py --profile <valid-profile>` — valid options: `python`, `flutter`.

**Provider not detected / wrong provider**
Set the explicit flag: `python init.py --provider groq`

**Pipeline ESCALATED after 3 retries**
The reviewer found issues the implementer couldn't fix automatically. Read the `## Review Verdict` in the feature file. Fix the issues manually, set `status = CONFIRMED` in the frontmatter, then run `/implement FEAT-XXX`.

**Branch already exists**
Stangent creates one branch per feature. If the branch already exists from a previous run, it will reuse it.

**`/implement` says "Already in progress"**
The feature is stuck in `IMPLEMENTING` state. Check the feature file — if the implementer is genuinely done, set `status = CONFIRMED` manually and re-run `/implement`.

---

## Project structure (stangent installation)

```
stangent/
├── init.py                    ← run this to set up / re-run to upgrade
├── config.template.json       ← reference for config.json structure
│
├── agents/                    ← agent instruction files (markdown)
│   ├── orchestrator.md        ← pipeline state machine
│   ├── planner.md             ← spec writer
│   ├── implementer.md         ← code writer
│   ├── reviewer.md            ← code reviewer
│   ├── srs_agent.md           ← SRS updater
│   ├── adr_agent.md           ← ADR recorder
│   └── subagents/
│       ├── linter.md
│       ├── unit_tester.md
│       ├── query_analyzer.md
│       └── security_scanner.md
│
├── commands/                  ← slash command definitions
│   ├── feature.md             ← /feature
│   ├── plan.md                ← /plan
│   ├── implement.md           ← /implement
│   ├── review.md              ← /review
│   ├── status.md              ← /status
│   ├── srs.md                 ← /srs
│   ├── adr.md                 ← /adr
│   └── abandon.md             ← /abandon
│
├── profiles/
│   ├── _base.md               ← profile contract (all required fields)
│   ├── python.md
│   └── flutter.md
│
├── templates/
│   ├── feature_spec.md        ← feature file template
│   ├── srs.md                 ← SRS template
│   ├── decisions.md           ← decisions.md template
│   ├── profile_guide.md       ← how to add a new language
│   └── agent_template.md      ← how to write a new agent
│
└── evals/
    ├── eval_runner.py
    └── planner/
        ├── case_01_input.md
        ├── case_01_expect.md
        └── case_01_assert.py
```

---

## Design principles

**Agents own sections, not files.** Every section in a feature file has exactly one owner. Agents never overwrite each other's sections. Conflicts are structurally impossible.

**The spec is the contract.** The reviewer checks the implementation against the spec — nothing else. No gold-plating, no scope creep. If it's not in the spec, it doesn't get implemented.

**ADRs are binding.** Once recorded, architectural decisions are automatically enforced by every agent. You write a rule once; it applies forever.

**Retries are surgical.** When review fails, the implementer gets exact `file:line` references and required fixes. Vague failure reasons are rejected by the reviewer's own constraints.

**Portable by design.** Agents read `.stangent/config.json` at runtime. No paths are baked into agent files. The stangent installation can live anywhere on your machine.
