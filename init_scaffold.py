"""
Stangent scaffolding: create dirs, init registry, copy agents/commands/profiles,
write settings.json, create SRS/decisions/gitignore, configure DBHub.
"""

import json
from datetime import datetime, timezone
from pathlib import Path

from init_constants import (
    STANGENT_PATH, VERSION, STANGENT_DIRS,
    CLAUDE_COMMANDS_DIR, CLAUDE_AGENTS_DIR, CLAUDE_SUBAGENTS_DIR,
    GLOBAL_COMMANDS_DIR, GLOBAL_AGENTS_DIR,
    DROPDOWN_AGENTS, SUBAGENTS,
    ok, fail, warn, info, header,
)
from init_config import convert_for_claude_code, parse_frontmatter


def create_stangent_dirs(project_root: Path, dry_run: bool):
    for d in STANGENT_DIRS:
        target = project_root / d
        if not target.exists():
            if not dry_run:
                target.mkdir(parents=True, exist_ok=True)
            info(f"Created {d}")
        else:
            ok(f"{d} already exists")


def init_registry(project_root: Path, config: dict, dry_run: bool):
    registry_path = project_root / config["paths"]["registry_path"]
    if not registry_path.exists():
        registry = {
            "next_id": 1,
            "prefix": config["feature_id"]["prefix"],
            "padding": config["feature_id"]["padding"],
            "features": {},
        }
        if not dry_run:
            registry_path.write_text(json.dumps(registry, indent=2))
        info("Created features_registry.json")
    else:
        ok("features_registry.json already exists")


def install_global(dry_run: bool) -> bool:
    """
    Install agents and commands to ~/.claude/ so they appear in every project.
    This is a one-time setup — re-run to update when stangent is upgraded.
    Returns True if successful.
    """
    header("Global Install → ~/.claude/")

    all_ok = True

    # ── agents ────────────────────────────────────────────────────────────
    if not GLOBAL_AGENTS_DIR.exists():
        if not dry_run:
            GLOBAL_AGENTS_DIR.mkdir(parents=True, exist_ok=True)
        info(f"Created {GLOBAL_AGENTS_DIR}")

    for agent_key, agent_cfg in DROPDOWN_AGENTS.items():
        src_file = STANGENT_PATH / "agents" / f"{agent_key}.md"
        if not src_file.exists():
            warn(f"agents/{agent_key}.md — not found, skipping")
            all_ok = False
            continue

        raw     = src_file.read_text(encoding="utf-8")
        content = convert_for_claude_code(raw, agent_cfg["display_name"], agent_cfg["color"])
        dst_file = GLOBAL_AGENTS_DIR / agent_cfg["filename"]

        if dst_file.exists() and dst_file.read_text(encoding="utf-8") == content:
            ok(f"~/.claude/agents/{agent_cfg['filename']} — up to date")
        else:
            label = "updated" if dst_file.exists() else "installed"
            if not dry_run:
                dst_file.write_text(content, encoding="utf-8")
            info(f"~/.claude/agents/{agent_cfg['filename']} — {label}")

    # ── commands ──────────────────────────────────────────────────────────
    commands_src = STANGENT_PATH / "commands"
    if not GLOBAL_COMMANDS_DIR.exists():
        if not dry_run:
            GLOBAL_COMMANDS_DIR.mkdir(parents=True, exist_ok=True)
        info(f"Created {GLOBAL_COMMANDS_DIR}")

    for src_file in sorted(commands_src.glob("*.md")):
        dst_file = GLOBAL_COMMANDS_DIR / src_file.name
        content = src_file.read_text(encoding="utf-8")
        if dst_file.exists() and dst_file.read_text(encoding="utf-8") == content:
            ok(f"~/.claude/commands/{src_file.name} — up to date")
        else:
            label = "updated" if dst_file.exists() else "installed"
            if not dry_run:
                dst_file.write_text(content, encoding="utf-8")
            info(f"~/.claude/commands/{src_file.name} — {label}")

    print(f"""
  ✓ Global install complete.

  Agents now appear in the Claude Code mode selector in EVERY project:
    • Stangent
    • Stangent Planner
    • Stangent Implementer
    • Stangent Reviewer
    • Stangent SRS
    • Stangent ADR

  Slash commands now available in EVERY project:
    /feature  /plan  /implement  /resume  /review  /srs
    /status   /abandon  /adr  /doctor  /uninit  /gateway  /cleanup

  Next: scaffold each project with:
    cd your-project && python <path-to-stangent>/init.py --profile <name>
  (creates .stangent/config.json + .stangent/ scaffolding)
""")
    return all_ok


def copy_commands(project_root: Path, config: dict, dry_run: bool):
    """
    Copy command files verbatim — no path substitution.
    Commands read config.json at runtime to resolve all paths dynamically.
    This makes command files portable across machines and team members.
    Re-running init is only needed to pick up new commands added to stangent.
    """
    commands_src = STANGENT_PATH / "commands"
    commands_dst = project_root / CLAUDE_COMMANDS_DIR

    if not commands_dst.exists():
        if not dry_run:
            commands_dst.mkdir(parents=True, exist_ok=True)
        info(f"Created {CLAUDE_COMMANDS_DIR}")

    current_names = {f.name for f in commands_src.glob("*.md")}

    # Remove stale files from previous stangent versions
    for dst_file in sorted(commands_dst.glob("*.md")):
        if dst_file.name not in current_names:
            if not dry_run:
                dst_file.unlink()
            warn(f"commands/{dst_file.name} — removed (no longer in stangent)")

    for src_file in sorted(commands_src.glob("*.md")):
        dst_file = commands_dst / src_file.name
        content = src_file.read_text(encoding="utf-8")

        if dst_file.exists():
            existing = dst_file.read_text(encoding="utf-8")
            if existing == content:
                ok(f"commands/{src_file.name} — up to date")
                continue
            warn(f"commands/{src_file.name} — updated (stangent version changed)")
        else:
            info(f"commands/{src_file.name} — installed")

        if not dry_run:
            dst_file.write_text(content, encoding="utf-8")


def copy_claude_agents(project_root: Path, dry_run: bool):
    """
    Convert agents/*.md → .claude/agents/ in Claude Code dropdown format.

    Only agents listed in DROPDOWN_AGENTS are installed (sub-agents and
    internal agents are intentionally excluded — they are spawned by the
    main agents, not selected directly by the user).

    Conversion applied per file:
      - tools YAML list → comma-separated string (Claude Code requirement)
      - Internal frontmatter fields stripped (version, type, inputs, etc.)
      - display_name and color injected
      - Agent body preserved verbatim (constraints remain in effect)
    """
    agents_dst = project_root / CLAUDE_AGENTS_DIR

    if not agents_dst.exists():
        if not dry_run:
            agents_dst.mkdir(parents=True, exist_ok=True)
        info(f"Created {CLAUDE_AGENTS_DIR}")

    current_agent_files = (
        {cfg["filename"] for cfg in DROPDOWN_AGENTS.values()} |
        set(SUBAGENTS.values())
    )

    # Remove stale agent files from previous stangent versions
    for dst_file in sorted(agents_dst.glob("stangent*.md")):
        if dst_file.name not in current_agent_files:
            if not dry_run:
                dst_file.unlink()
            warn(f"agents/{dst_file.name} — removed (no longer in stangent)")

    # ── Dropdown agents (user-visible) ────────────────────────────────────
    for agent_key, agent_cfg in DROPDOWN_AGENTS.items():
        src_file = STANGENT_PATH / "agents" / f"{agent_key}.md"
        if not src_file.exists():
            warn(f"agents/{agent_key}.md — not found, skipping")
            continue

        raw     = src_file.read_text(encoding="utf-8")
        content = convert_for_claude_code(raw, agent_cfg["display_name"], agent_cfg["color"])
        dst_file = agents_dst / agent_cfg["filename"]

        if dst_file.exists():
            existing = dst_file.read_text(encoding="utf-8")
            if existing == content:
                ok(f"agents/{agent_cfg['filename']} — up to date")
                continue
            warn(f"agents/{agent_cfg['filename']} — updated (stangent version changed)")
        else:
            info(f"agents/{agent_cfg['filename']} — installed")

        if not dry_run:
            dst_file.write_text(content, encoding="utf-8")

    # ── Sub-agents (hidden — deployed to a subfolder, never shown in dropdown)
    subagents_dst = project_root / CLAUDE_SUBAGENTS_DIR
    if not subagents_dst.exists():
        if not dry_run:
            subagents_dst.mkdir(parents=True, exist_ok=True)
        info(f"Created {CLAUDE_SUBAGENTS_DIR}")

    for src_rel, dst_name in SUBAGENTS.items():
        src_file = STANGENT_PATH / "agents" / f"{src_rel}.md"
        if not src_file.exists():
            warn(f"agents/{src_rel}.md — not found, skipping")
            continue

        # Strip internal frontmatter, deploy body only.
        _, body = parse_frontmatter(src_file.read_text(encoding="utf-8"))
        content  = body.lstrip("\n")
        dst_file = subagents_dst / dst_name

        if dst_file.exists():
            existing = dst_file.read_text(encoding="utf-8")
            if existing == content:
                ok(f"agents/subagents/{dst_name} — up to date")
                continue
            warn(f"agents/subagents/{dst_name} — updated (stangent version changed)")
        else:
            info(f"agents/subagents/{dst_name} — installed")

        if not dry_run:
            dst_file.write_text(content, encoding="utf-8")


def _sync_dir(src: Path, dst: Path, label: str, dry_run: bool):
    """Copy all .md files from src to dst, removing stale files."""
    if not dst.exists():
        if not dry_run:
            dst.mkdir(parents=True, exist_ok=True)

    current = {f.name for f in src.glob("*.md")}

    for dst_file in sorted(dst.glob("*.md")):
        if dst_file.name not in current:
            if not dry_run:
                dst_file.unlink()
            warn(f"{label}/{dst_file.name} — removed (no longer in stangent)")

    for src_file in sorted(src.glob("*.md")):
        dst_file = dst / src_file.name
        content = src_file.read_text(encoding="utf-8")
        if dst_file.exists() and dst_file.read_text(encoding="utf-8") == content:
            ok(f"{label}/{src_file.name} — up to date")
        else:
            label_action = "updated" if dst_file.exists() else "installed"
            if not dry_run:
                dst_file.write_text(content, encoding="utf-8")
            info(f"{label}/{src_file.name} — {label_action}")


def copy_profiles(project_root: Path, dry_run: bool):
    _sync_dir(
        STANGENT_PATH / "profiles",
        project_root / ".stangent" / "profiles",
        ".stangent/profiles",
        dry_run,
    )


def copy_templates(project_root: Path, dry_run: bool):
    _sync_dir(
        STANGENT_PATH / "templates",
        project_root / ".stangent" / "templates",
        ".stangent/templates",
        dry_run,
    )


def copy_prompts(project_root: Path, dry_run: bool):
    _sync_dir(
        STANGENT_PATH / "prompts",
        project_root / ".stangent" / "prompts",
        ".stangent/prompts",
        dry_run,
    )


def copy_gateway(project_root: Path, dry_run: bool):
    """Copy gateway.py to .stangent/gateway/ so the project is self-contained."""
    src = STANGENT_PATH / "gateway" / "gateway.py"
    if not src.exists():
        warn("gateway/gateway.py — not found in stangent source, skipping")
        return

    dst_dir = project_root / ".stangent" / "gateway"
    if not dst_dir.exists():
        if not dry_run:
            dst_dir.mkdir(parents=True, exist_ok=True)

    dst = dst_dir / "gateway.py"
    content = src.read_text(encoding="utf-8")
    if dst.exists() and dst.read_text(encoding="utf-8") == content:
        ok(".stangent/gateway/gateway.py — up to date")
    else:
        label = "updated" if dst.exists() else "installed"
        if not dry_run:
            dst.write_text(content, encoding="utf-8")
        info(f".stangent/gateway/gateway.py — {label}")


def write_settings_json(project_root: Path, dry_run: bool):
    """
    Deploy .claude/settings.json with a PreToolUse hook that runs gateway.py.
    Merges with any existing settings rather than overwriting.
    """
    settings_path = project_root / ".claude" / "settings.json"

    existing: dict = {}
    if settings_path.exists():
        try:
            existing = json.loads(settings_path.read_text(encoding="utf-8"))
        except Exception:
            warn(".claude/settings.json — could not parse existing file, skipping")
            return

    gateway_hook = {
        "matcher": "Write|Edit|Bash",
        "hooks": [
            {
                "type": "command",
                "command": "python .stangent/gateway/gateway.py",
            }
        ],
    }

    hooks = existing.setdefault("hooks", {})
    pre_tool_use: list = hooks.setdefault("PreToolUse", [])

    # Check if our hook is already present (match by command string)
    already_present = any(
        any(
            h.get("command") == gateway_hook["hooks"][0]["command"]
            for h in entry.get("hooks", [])
        )
        for entry in pre_tool_use
    )

    if already_present:
        ok(".claude/settings.json — gateway hook already present")
        return

    pre_tool_use.append(gateway_hook)

    label = "updated" if settings_path.exists() else "created"
    if not dry_run:
        settings_path.parent.mkdir(parents=True, exist_ok=True)
        settings_path.write_text(json.dumps(existing, indent=2), encoding="utf-8")
    info(f".claude/settings.json — {label} (PreToolUse gateway hook added)")


def create_memory(project_root: Path, config: dict, dry_run: bool):
    memory_path = project_root / config["paths"].get("memory_path", ".stangent/memory.md")
    if memory_path.exists():
        ok("memory.md already exists")
        return
    template = (STANGENT_PATH / "templates" / "memory.md").read_text(encoding="utf-8")
    if not dry_run:
        memory_path.write_text(template, encoding="utf-8")
    info("Created .stangent/memory.md")


def create_srs(project_root: Path, config: dict, dry_run: bool):
    srs_path = project_root / config["paths"]["srs_path"]
    if srs_path.exists():
        ok("SRS.md already exists")
        return

    template = (STANGENT_PATH / "templates" / "srs.md").read_text(encoding="utf-8")
    project_name = project_root.name
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    template = template.replace("{{PROJECT_NAME}}", project_name)
    template = template.replace("{{PROFILE}}", config["profiles"][0])
    template = template.replace("{{VERSION}}", VERSION)
    template = template.replace("{{ISO_DATE}}", now)

    if not dry_run:
        srs_path.write_text(template, encoding="utf-8")
    info("Created .stangent/SRS.md")


def create_decisions(project_root: Path, config: dict, dry_run: bool):
    decisions_path = project_root / config["paths"]["decisions_path"]
    if decisions_path.exists():
        ok("decisions.md already exists")
        return

    template = (STANGENT_PATH / "templates" / "decisions.md").read_text(encoding="utf-8")
    if not dry_run:
        decisions_path.write_text(template, encoding="utf-8")
    info("Created .stangent/decisions.md")


def create_env_example(project_root: Path, dry_run: bool):
    env_example = project_root / ".env.example"
    if not env_example.exists():
        content = (
            "# Environment variables for this project\n"
            "# Copy to .env and fill in your values\n"
            "# This file is committed to git. .env is gitignored.\n\n"
            "ANTHROPIC_API_KEY=your-key-here\n"
        )
        if not dry_run:
            env_example.write_text(content)
        info("Created .env.example")
    else:
        ok(".env.example already exists")


def update_gitignore(project_root: Path, dry_run: bool):
    gitignore = project_root / ".gitignore"
    entries_to_add = [
        ".env",
        ".claude/settings.local.json",
        ".stangent/logs/",
        ".stangent/context_cache.md",
        ".stangent/features_registry.json",
        ".stangent/coverage_baseline.json",
        ".stangent/gateway/active.json",
    ]
    entries_to_keep = [
        "# Keep these stangent files in git:",
        "!.stangent/config.json",
        "!.stangent/features/",
        "!.stangent/archive/",
        "!.stangent/SRS.md",
        "!.stangent/decisions.md",
        "!.stangent/profiles/",
        "!.stangent/templates/",
        "!.stangent/prompts/",
        "!.stangent/contracts/",
        "!.stangent/gateway/",
    ]

    existing = gitignore.read_text(encoding="utf-8") if gitignore.exists() else ""
    additions = []
    for entry in entries_to_add + entries_to_keep:
        if entry not in existing:
            additions.append(entry)

    if additions:
        block = "\n# Stangent\n" + "\n".join(additions) + "\n"
        if not dry_run:
            with open(gitignore, "a", encoding="utf-8") as f:
                f.write(block)
        info(f"Updated .gitignore ({len(additions)} entries added)")
    else:
        ok(".gitignore already has stangent entries")


def create_onboarding_doc(project_root: Path, config: dict, dry_run: bool):
    doc_path      = project_root / ".stangent" / "HOW_THIS_WORKS.md"
    provider_name = config.get("provider", {}).get("name", "anthropic") \
                    if isinstance(config.get("provider"), dict) \
                    else config.get("provider", "anthropic")

    content = f"""# Stangent — How This Works

This project uses **Stangent** for AI-assisted feature development.
Stangent runs as a set of agents inside your Claude Code chat panel.

## Quick Start

Open Claude Code in VS Code. Type in the chat panel:

```
/feature add a login screen with email and password
```

That's it. The agent will ask a few questions, write a spec, confirm with you,
then implement, test, review, and document the feature.

## Using Stangent

### Dropdown agents (mode selector — click the agent picker)

| Agent | What it does |
|-------|--------------|
| **Stangent** | General-purpose — describe any request conversationally |
| **Stangent Planner** | Planning only — write a spec, stop before implementing |
| **Stangent Implementer** | Implementation only — code a planned feature |
| **Stangent Reviewer** | Review only — review an implemented feature |
| **Stangent SRS** | Update the living system requirements document |
| **Stangent ADR** | Record an architectural decision |

### Slash commands (type `/` in chat)

| Command | What it does |
|---------|--------------|
| `/feature <description>` | Full pipeline: plan → implement → review → SRS |
| `/plan <description>` | Plan only, get a spec, confirm before implementing |
| `/implement FEAT-XXX` | Implement a planned feature |
| `/review FEAT-XXX` | Review an implemented feature |
| `/srs` | Update the SRS for all completed features |
| `/srs FEAT-XXX` | Update SRS for one feature |
| `/status` | Show all features and their states |
| `/status FEAT-XXX` | Show detailed status for one feature |
| `/resume FEAT-XXX` | Resume a paused or interrupted feature |
| `/abandon FEAT-XXX` | Cleanly abandon a feature |
| `/adr <title>` | Record an architectural decision |
| `/doctor` | Validate stangent config and wiring |
| `/uninit` | Remove Stangent tooling from this project |

## Where Files Live

```
.stangent/
├── features/        ← one file per feature (the source of truth)
├── archive/         ← completed and abandoned features
├── logs/            ← JSON Lines run logs per feature
├── SRS.md           ← auto-maintained System Requirements Specification
└── decisions.md     ← Architecture Decision Records (add yours here)
```

## Pipeline

```
/feature → [ADR BOOTSTRAP — first feature only]
         → PLANNING → AWAITING_CONFIRMATION → IMPLEMENTING
         → REVIEWING → SRS_UPDATE → COMPLETE
```

The pipeline pauses at AWAITING_CONFIRMATION. You must confirm the spec
before implementation begins. Review the spec at:
`.stangent/features/FEAT-XXX-<slug>.md`

On the very first `/feature` call the planner scans your codebase and
asks which detected patterns become binding ADRs. On every subsequent
feature, it checks the request against existing ADRs and flags conflicts
before writing the spec.

## Setup

| Setting | Value |
|---------|-------|
| Profiles | {", ".join(config.get("profiles", []))} |
| Provider | {provider_name} |

## Decisions Log

Architectural decisions are stored in `.stangent/decisions.md`.

## Meta Files (optional)

If your project has documentation that cascades from code changes, create
`.stangent/meta.md`. The planner reads it automatically and adds dependent
doc files to `## Files to Touch` in every feature spec.

Example — if changing a model also means updating API docs:
```
| When you touch      | Also review         |
|---------------------|---------------------|
| src/models/*.py     | docs/api.md         |
| src/routes/*.py     | README.md           |
```

- **Auto-bootstrap:** On the first `/feature`, Stangent scans your codebase and
  proposes candidate ADRs (detected frameworks, patterns, conventions). Confirm
  which ones become binding — done automatically, no manual writing needed.
- **Manual ADRs:** Use `/adr <title>` at any time to record an explicit decision.
- **Contradiction detection:** Before every spec, the planner checks whether the
  feature conflicts with existing ADRs and asks you to comply, override (with
  reason), or cancel. Overrides are recorded per-feature — the ADR stays active
  for all other features.
- **Enforcement:** All agents read decisions.md and apply every Accepted ADR
  automatically. No need to remind them.

## Customising Agents

Agent files live in `.claude/agents/` and `.claude/agents/subagents/`.
You can edit them to adjust behaviour for your project.

> **Always bump the `version` field in the frontmatter when you edit an agent.**
> Use semver: patch (x.x.N) for small fixes, minor (x.N.0) for new behaviour.
> The version is recorded in every Run Log entry — it's how you know which
> behaviour was active when a feature was built.

Re-run `python {STANGENT_PATH}/init.py` after a Stangent update to
sync the latest agent files. Your edits will be shown as conflicts so you can
merge them manually.

---
Generated by stangent v{VERSION} on {datetime.now(timezone.utc).strftime("%Y-%m-%d")}
"""

    action = "Updated" if doc_path.exists() else "Created"
    if not dry_run:
        doc_path.write_text(content, encoding="utf-8")
    info(f"{action} .stangent/HOW_THIS_WORKS.md")


def configure_dbhub(config: dict, config_path: Path, project_root: Path, dry_run: bool):
    """
    Optionally configure DBHub MCP integration for real schema queries.
    Only prompts if not already enabled. Skipped silently on dry-run.
    """
    dbhub = config.get("integrations", {}).get("dbhub", {})
    if dbhub.get("enabled"):
        server = dbhub.get("mcp_server", "dbhub")
        settings_local = project_root / ".claude" / "settings.local.json"
        if settings_local.exists():
            ok(f"DBHub — already configured (mcp_server: {server})")
        else:
            warn(f"DBHub — enabled in config but .claude/settings.local.json is missing")
            info("Re-run init and choose 'reconfigure' to recreate it, or add it manually.")
        return

    if dry_run:
        info("DBHub — skipped (dry-run)")
        return

    header("DBHub Integration (optional)")
    print("  Connect Stangent to your database via DBHub MCP.")
    print("  Enables real schema queries in the planner and index verification")
    print("  in the query analyzer — instead of guessing from migration files.")
    print("  Supports: PostgreSQL, MySQL, MariaDB, SQL Server, SQLite")
    print()
    raw = input("  Enable DBHub integration? (yes / skip) [skip]: ").strip().lower()
    if raw != "yes":
        info("DBHub — skipped (enable later by re-running init)")
        return

    print()
    print("  DSN examples:")
    print("    PostgreSQL:  postgres://user:pass@host:5432/dbname")
    print("    MySQL:       mysql://user:pass@host:3306/dbname")
    print("    SQLite:      sqlite:///absolute/path/to/db.sqlite")
    print("    Supabase:    postgres://postgres.[ref]:[pass]@aws-0-[region].pooler.supabase.com:5432/postgres?sslmode=require")
    print()
    dsn = input("  DSN (leave blank to add later): ").strip()
    server_name = input("  MCP server name [dbhub]: ").strip() or "dbhub"

    if "integrations" not in config:
        config["integrations"] = {}
    config["integrations"]["dbhub"] = {
        "enabled":    True,
        "mcp_server": server_name,
    }
    config_path.write_text(json.dumps(config, indent=2))

    if dsn:
        settings_local_path = project_root / ".claude" / "settings.local.json"
        settings_local_path.parent.mkdir(exist_ok=True)
        existing = {}
        if settings_local_path.exists():
            existing = json.loads(settings_local_path.read_text(encoding="utf-8"))
        if "mcpServers" not in existing:
            existing["mcpServers"] = {}
        existing["mcpServers"][server_name] = {
            "command": "npx",
            "args": ["@bytebase/dbhub", "--transport", "stdio", "--dsn", dsn],
        }
        settings_local_path.write_text(json.dumps(existing, indent=2))
        ok(f"DBHub — configured and written to .claude/settings.local.json")
    else:
        ok(f"DBHub — enabled in config (add DSN to .claude/settings.local.json when ready)")
    print()
    print("  ⚠  DSN gotchas:")
    print("     - Special chars in password must be URL-encoded: !→%21  @→%40  #→%23  $→%24")
    print("     - If you see an ESM/CJS crash on startup, apply this fix once:")
    print('         node -e "const p=require(\'path\').join(require(\'os\').homedir(),\'AppData\',\'Roaming\',\'npm\',\'node_modules\',\'@bytebase\',\'dbhub\',\'node_modules\',\'ssh-config\',\'package.json\'); const f=require(\'fs\'); const j=JSON.parse(f.readFileSync(p)); j.type=\'module\'; f.writeFileSync(p,JSON.stringify(j,null,2));"')
    print("       (Linux/Mac: adjust path to your global npm prefix)")
    print()
    print("  Restart Claude Code to activate DBHub.")


# ── Cross-stack meta setup ────────────────────────────────────────────────────

def setup_cross_stack_meta(project_root: Path, profile_names: list, dry_run: bool):
    """
    If both a backend profile (fastapi or python) and flutter are active,
    copy meta_flutter_fastapi.md to .stangent/meta.md — unless it already exists.
    """
    backend_profiles = {"fastapi", "python"}
    has_backend = any(p in backend_profiles for p in profile_names)
    has_flutter = "flutter" in profile_names

    if not (has_backend and has_flutter):
        return

    dst = project_root / ".stangent" / "meta.md"
    if dst.exists():
        ok(".stangent/meta.md — already present, skipping")
        return

    src = STANGENT_PATH / "templates" / "meta_flutter_fastapi.md"
    if not src.exists():
        warn(".stangent/meta.md — template not found in stangent source, skipping")
        return

    if not dry_run:
        dst.write_text(src.read_text(encoding="utf-8"), encoding="utf-8")
    ok(".stangent/meta.md — created from meta_flutter_fastapi.md starter")
    info("Edit .stangent/meta.md to fill in your actual route-to-service mappings.")


# ── Supabase setup ────────────────────────────────────────────────────────────

def configure_supabase(config: dict, config_path: Path, profile_names: list, dry_run: bool):
    """
    Optionally configure Supabase integration.
    Only prompts if not already enabled. Skipped silently on dry-run.
    """
    supabase = config.get("integrations", {}).get("supabase", {})
    if supabase.get("enabled"):
        ok(f"Supabase — already configured ({supabase.get('project_url', 'url not set')})")
        return

    # Only prompt when Supabase is plausibly in use
    has_flutter  = "flutter" in profile_names
    has_backend  = any(p in {"fastapi", "python"} for p in profile_names)
    if not (has_flutter or has_backend):
        return

    if dry_run:
        info("Supabase — skipped (dry-run)")
        return

    header("Supabase Integration (optional)")
    print("  Enable Supabase-aware security rules: RLS enforcement on migrations,")
    print("  service_role key leak detection, JWT middleware verification,")
    print("  realtime subscription cleanup, and architecture detection.")
    print()
    raw = input("  Enable Supabase integration? (yes / skip) [skip]: ").strip().lower()
    if raw != "yes":
        info("Supabase — skipped (enable later by re-running init)")
        return

    print()
    project_url = input("  Supabase project URL (e.g. https://xxx.supabase.co) [leave blank to add later]: ").strip()
    direct_conn = input("  Direct PG connection string [leave blank to add later]: ").strip()

    if "integrations" not in config:
        config["integrations"] = {}
    config["integrations"]["supabase"] = {
        "enabled":           True,
        "project_url":       project_url or None,
        "direct_connection": direct_conn or None,
    }
    config_path.write_text(json.dumps(config, indent=2))
    ok("Supabase — enabled in config")
    if not project_url or not direct_conn:
        info("Add missing values to .stangent/config.json when ready.")
    print()
    print("  Next steps:")
    print("  1. Copy the meta template if you haven't already:")
    print("       cp .stangent/templates/meta_flutter_fastapi.md .stangent/meta.md")
    print("     (or re-run init — it will copy it automatically for double-stack projects)")
    print("  2. If using DBHub for live schema queries, set direct_connection to your")
    print("     Supabase direct PG connection string (port 5432, not the pooler).")
    print()


# ── Uninit ────────────────────────────────────────────────────────────────────

_STANGENT_COMMAND_FILES = [
    "abandon.md", "adr.md", "cleanup.md", "doctor.md", "feature.md",
    "gateway.md", "implement.md", "plan.md", "resume.md", "review.md",
    "srs.md", "status.md", "uninit.md",
]

_GITIGNORE_MARKER = "# Stangent"


def _remove_file(path: Path, label: str, dry_run: bool):
    if path.exists():
        if not dry_run:
            path.unlink()
        info(f"Deleted {label}")


def _remove_dir_if_empty(path: Path, dry_run: bool):
    if path.exists() and not any(path.iterdir()):
        if not dry_run:
            path.rmdir()


def _remove_agent_files(project_root: Path, dry_run: bool) -> int:
    count = 0
    agents_dir = project_root / CLAUDE_AGENTS_DIR
    subagents_dir = project_root / CLAUDE_SUBAGENTS_DIR

    for pattern_dir, label in [(agents_dir, ".claude/agents"), (subagents_dir, ".claude/agents/subagents")]:
        if not pattern_dir.exists():
            continue
        for f in sorted(pattern_dir.glob("stangent*.md")):
            _remove_file(f, f"{label}/{f.name}", dry_run)
            count += 1

    _remove_dir_if_empty(subagents_dir, dry_run)
    return count


def _remove_command_files(project_root: Path, dry_run: bool) -> int:
    count = 0
    commands_dir = project_root / CLAUDE_COMMANDS_DIR
    if not commands_dir.exists():
        return 0
    for name in _STANGENT_COMMAND_FILES:
        f = commands_dir / name
        if f.exists():
            _remove_file(f, f".claude/commands/{name}", dry_run)
            count += 1
    return count


def _remove_gateway_hook(project_root: Path, dry_run: bool):
    settings_path = project_root / ".claude" / "settings.json"
    if not settings_path.exists():
        return

    try:
        data = json.loads(settings_path.read_text(encoding="utf-8"))
    except Exception:
        warn(".claude/settings.json — could not parse, skipping hook removal")
        return

    hooks = data.get("hooks", {})
    pre = hooks.get("PreToolUse", [])
    new_pre = [
        entry for entry in pre
        if not any("gateway.py" in h.get("command", "") for h in entry.get("hooks", []))
    ]

    if len(new_pre) == len(pre):
        ok(".claude/settings.json — no gateway hook found (already clean)")
        return

    if not new_pre:
        hooks.pop("PreToolUse", None)
    else:
        hooks["PreToolUse"] = new_pre

    if not hooks:
        data.pop("hooks", None)

    if not dry_run:
        settings_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    info(".claude/settings.json — gateway hook removed")
    if data:
        ok(".claude/settings.json — other settings preserved")


def _remove_gateway_files(project_root: Path, dry_run: bool):
    gateway_dir = project_root / ".stangent" / "gateway"
    for name in ("gateway.py", "active.json", "active.json.paused"):
        _remove_file(gateway_dir / name, f".stangent/gateway/{name}", dry_run)
    _remove_dir_if_empty(gateway_dir, dry_run)


def _remove_stangent_gitignore_block(project_root: Path, dry_run: bool):
    gitignore = project_root / ".gitignore"
    if not gitignore.exists():
        return

    text = gitignore.read_text(encoding="utf-8")
    if _GITIGNORE_MARKER not in text:
        ok(".gitignore — no stangent block found (already clean)")
        return

    lines = text.splitlines(keepends=True)
    new_lines = []
    inside_block = False
    for line in lines:
        if line.strip() == _GITIGNORE_MARKER:
            inside_block = True
            continue
        if inside_block and line.strip() == "":
            inside_block = False
            continue
        if not inside_block:
            new_lines.append(line)

    if not dry_run:
        gitignore.write_text("".join(new_lines), encoding="utf-8")
    info(".gitignore — stangent entries removed")


def _remove_stangent_dir(project_root: Path, dry_run: bool) -> int:
    import shutil
    stangent_dir = project_root / ".stangent"
    if not stangent_dir.exists():
        ok(".stangent/ — already gone")
        return 0

    count = sum(1 for _ in stangent_dir.rglob("*") if _.is_file())
    if not dry_run:
        shutil.rmtree(stangent_dir)
    info(f".stangent/ — deleted ({count} file(s))")
    return count


def uninit_project(project_root: Path, hard: bool, dry_run: bool) -> bool:
    """
    Remove Stangent artefacts from a project.

    Soft (hard=False): removes agents, commands, gateway hook, gateway.py.
                       Keeps .stangent/ (features, SRS, decisions, config).
    Hard (hard=True):  everything above + deletes .stangent/ and gitignore block.

    Returns True if anything was removed.
    """
    if dry_run:
        print(f"\n{C.WARN} DRY RUN — no files will be deleted\n")

    mode = "HARD" if hard else "SOFT"
    header(f"Uninit ({mode}) — {project_root.name}")

    agent_count   = _remove_agent_files(project_root, dry_run)
    command_count = _remove_command_files(project_root, dry_run)
    _remove_gateway_hook(project_root, dry_run)
    _remove_gateway_files(project_root, dry_run)

    if hard:
        _remove_stangent_dir(project_root, dry_run)
        _remove_stangent_gitignore_block(project_root, dry_run)

    header("Done")

    if hard:
        print(f"""
  Stangent fully removed from {project_root.name}.

  Deleted: {agent_count} agent file(s), {command_count} command file(s),
           gateway hook, gateway.py, and .stangent/ directory.

  To start fresh: python {STANGENT_PATH}/init.py
""")
    else:
        print(f"""
  Stangent tooling removed from {project_root.name}.

  Deleted: {agent_count} agent file(s), {command_count} command file(s),
           gateway hook, and gateway.py.

  Kept:    .stangent/ (config, features, SRS, decisions)

  To re-install: python {STANGENT_PATH}/init.py
""")

    return (agent_count + command_count) > 0
