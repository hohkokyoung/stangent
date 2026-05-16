# Stangent

AI-assisted feature development inside Claude Code. Describe a feature — Stangent plans it, implements it, reviews it, and documents it. You confirm each step.

No servers. No accounts. Just markdown files and Claude Code.

---

## How it works

```
/feature add login screen
    │
    ▼  ADR BOOTSTRAP (first feature only — detects existing patterns)
    ▼  PLANNING      — reads codebase, checks ADRs, writes spec
    ▼  (you confirm)
    ▼  IMPLEMENTING  — writes code → linter → tests → query analysis
    ▼  REVIEWING     — spec compliance + 4-pass security scan
    ▼  SRS UPDATE    — extracts requirements into SRS.md
    ▼
  COMPLETE
```

Everything is tracked in `.stangent/features/FEAT-XXX-slug.md`. Each agent owns its own sections — they never overwrite each other.

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
| `/resume FEAT-XXX` | Resume a paused or interrupted feature |
| `/review FEAT-XXX` | Re-run review independently |
| `/srs [FEAT-XXX]` | Update SRS.md for completed features |
| `/adr <title>` | Record an architectural decision |
| `/status [FEAT-XXX]` | Feature dashboard |
| `/abandon FEAT-XXX` | Cleanly cancel a feature |
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
| `flutter` | `pubspec.yaml` | dart analyze | flutter test |

Add a new language: see [`templates/profile_guide.md`](templates/profile_guide.md).

---

## Key config (`stangent/config.json`)

```json
{
  "pipeline": {
    "max_retries": 3,
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

**Wrong provider detected?** `python init.py --provider <name>`

**Using Cursor?** See [`adapters/cursor/AGENT_INSTRUCTIONS.md`](adapters/cursor/AGENT_INSTRUCTIONS.md) for gateway soft-enforcement setup.

---

## Design principles

- **Agents own sections, not files** — conflicts are structurally impossible
- **The spec is the contract** — reviewer checks against spec only, no gold-plating
- **ADRs are binding** — record a decision once, every agent enforces it forever
- **Retries are surgical** — failures come with exact `file:line` fix instructions
