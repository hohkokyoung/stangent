"""
Stangent initializer.

TWO MODES:

  Global install (run ONCE, ever):
      python init.py --global
      Installs agents and commands into ~/.claude/ so they appear in
      every project automatically. No per-project setup needed for visibility.

  Project init (run once per project):
      python init.py
      Creates config.json + .stangent/ scaffolding for the current project.
      Agents must already be globally installed (or will be installed locally).

  Both at once:
      python init.py --global           # installs globally
      cd your-project && python init.py # then scaffold the project

Options:
    --global    Install agents/commands to ~/.claude/ (user-level, all projects)
    --profile   Override auto-detected profile (python | flutter)
    --dry-run   Show what would be done without writing anything
    --verify    Only run environment validation, no scaffolding
"""

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


STANGENT_PATH = Path(__file__).parent.resolve()
REQUIRED_PYTHON = (3, 10)
VERSION = "1.0.0"

PROFILES = {
    "flutter": {
        "detect_files": ["pubspec.yaml"],
        "required_tools": ["flutter", "dart"],
        "optional_tools": ["detect-secrets"],
        "src_root": "lib/",
    },
    "python": {
        "detect_files": ["pyproject.toml", "requirements.txt", "setup.py"],
        "required_tools": ["ruff", "pytest", "bandit", "pip-audit", "detect-secrets"],
        "optional_tools": ["pytest-cov", "pytest-json-report"],
        "src_root": "src/",
    },
}

# Supported LLM providers.
#
# required_env  — ALL must be set for the provider to work
# optional_env  — nice-to-have (e.g. region, base URL override)
# detect_env    — ANY of these present → auto-detect this provider
# sdk           — which SDK to use: "anthropic" or "openai"
# api_key_env   — env var name for the API key (openai-sdk providers)
# base_url      — hardcoded base URL for openai-sdk providers (None = default)
# default_models — used when building a fresh config.json
PROVIDERS: dict[str, dict] = {
    # ── Anthropic family ──────────────────────────────────────────────────
    "anthropic": {
        "display":        "Anthropic (direct API)",
        "required_env":   ["ANTHROPIC_API_KEY"],
        "optional_env":   [],
        "detect_env":     ["ANTHROPIC_API_KEY"],
        "sdk":            "anthropic",
        "api_key_env":    "ANTHROPIC_API_KEY",
        "base_url":       None,
        "default_models": {
            "strong": "claude-sonnet-4-6",
            "fast":   "claude-haiku-4-5-20251001",
        },
    },
    "bedrock": {
        "display":        "AWS Bedrock",
        "required_env":   ["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"],
        "optional_env":   ["AWS_DEFAULT_REGION"],
        "detect_env":     ["AWS_ACCESS_KEY_ID"],
        "sdk":            "anthropic-bedrock",
        "api_key_env":    None,
        "base_url":       None,
        "default_models": {
            "strong": "us.anthropic.claude-3-5-sonnet-20241022-v2:0",
            "fast":   "us.anthropic.claude-3-haiku-20240307-v1:0",
        },
    },
    "vertex": {
        "display":        "Google Vertex AI",
        "required_env":   ["GOOGLE_CLOUD_PROJECT"],
        "optional_env":   ["GOOGLE_APPLICATION_CREDENTIALS", "GOOGLE_CLOUD_REGION"],
        "detect_env":     ["GOOGLE_CLOUD_PROJECT", "GOOGLE_APPLICATION_CREDENTIALS"],
        "sdk":            "anthropic-vertex",
        "api_key_env":    None,
        "base_url":       None,
        "default_models": {
            "strong": "claude-3-5-sonnet-v2@20241022",
            "fast":   "claude-3-haiku@20240307",
        },
    },
    # ── OpenAI-compatible family (all use the openai SDK) ─────────────────
    "openai": {
        "display":        "OpenAI",
        "required_env":   ["OPENAI_API_KEY"],
        "optional_env":   ["OPENAI_BASE_URL"],
        "detect_env":     ["OPENAI_API_KEY"],
        "sdk":            "openai",
        "api_key_env":    "OPENAI_API_KEY",
        "base_url":       None,   # set OPENAI_BASE_URL in .env for custom endpoints
        "default_models": {
            "strong": "gpt-4o",
            "fast":   "gpt-4o-mini",
        },
    },
    "groq": {
        "display":        "Groq (free tier — very fast)",
        "required_env":   ["GROQ_API_KEY"],
        "optional_env":   [],
        "detect_env":     ["GROQ_API_KEY"],
        "sdk":            "openai",
        "api_key_env":    "GROQ_API_KEY",
        "base_url":       "https://api.groq.com/openai/v1",
        "default_models": {
            "strong": "llama-3.3-70b-versatile",
            "fast":   "llama-3.1-8b-instant",
        },
    },
    "openrouter": {
        "display":        "OpenRouter (has free models)",
        "required_env":   ["OPENROUTER_API_KEY"],
        "optional_env":   [],
        "detect_env":     ["OPENROUTER_API_KEY"],
        "sdk":            "openai",
        "api_key_env":    "OPENROUTER_API_KEY",
        "base_url":       "https://openrouter.ai/api/v1",
        "default_models": {
            # :free suffix = free tier on OpenRouter
            "strong": "meta-llama/llama-3.3-70b-instruct:free",
            "fast":   "meta-llama/llama-3.1-8b-instruct:free",
        },
    },
    "ollama": {
        "display":        "Ollama (local, fully free)",
        "required_env":   [],   # no key needed — it's local
        "optional_env":   ["OLLAMA_HOST"],
        "detect_env":     [],   # can't auto-detect, use --provider ollama
        "sdk":            "openai",
        "api_key_env":    None,
        "base_url":       "http://localhost:11434/v1",
        "default_models": {
            # change to whatever model you have pulled locally
            "strong": "qwen2.5-coder:7b",
            "fast":   "qwen2.5-coder:1.5b",
        },
    },
}

STANGENT_DIRS = [
    ".stangent/features/",
    ".stangent/archive/",
    ".stangent/logs/",
]

# Project-level (scoped to one repo)
CLAUDE_COMMANDS_DIR = ".claude/commands/"
CLAUDE_AGENTS_DIR   = ".claude/agents/"

# User-level (available in every project, no init needed)
GLOBAL_CLAUDE_DIR       = Path.home() / ".claude"
GLOBAL_COMMANDS_DIR     = GLOBAL_CLAUDE_DIR / "commands"
GLOBAL_AGENTS_DIR       = GLOBAL_CLAUDE_DIR / "agents"

# Agents to expose in the Claude Code mode-selector dropdown.
# Each entry maps an agents/<key>.md source file to a Claude Code agent file.
# display_name → shown in the VS Code dropdown
# color        → accent colour in the UI (purple/blue/cyan/orange/green/yellow/red/gray)
# filename     → output filename in .claude/agents/
DROPDOWN_AGENTS: dict[str, dict] = {
    "orchestrator": {
        "display_name": "Stangent",
        "color":        "purple",
        "filename":     "stangent.md",
    },
    "planner": {
        "display_name": "Stangent Planner",
        "color":        "blue",
        "filename":     "stangent-planner.md",
    },
    "implementer": {
        "display_name": "Stangent Implementer",
        "color":        "cyan",
        "filename":     "stangent-implementer.md",
    },
    "reviewer": {
        "display_name": "Stangent Reviewer",
        "color":        "orange",
        "filename":     "stangent-reviewer.md",
    },
    "srs_agent": {
        "display_name": "Stangent SRS",
        "color":        "green",
        "filename":     "stangent-srs.md",
    },
    "adr_agent": {
        "display_name": "Stangent ADR",
        "color":        "yellow",
        "filename":     "stangent-adr.md",
    },
}


# ─── Formatting helpers ───────────────────────────────────────────────────────

class C:
    OK   = "\033[92m✓\033[0m"
    FAIL = "\033[91m✗\033[0m"
    WARN = "\033[93m⚠\033[0m"
    INFO = "\033[94m•\033[0m"
    BOLD = "\033[1m"
    END  = "\033[0m"


def ok(msg):   print(f"  {C.OK}  {msg}")
def fail(msg): print(f"  {C.FAIL}  {msg}")
def warn(msg): print(f"  {C.WARN}  {msg}")
def info(msg): print(f"  {C.INFO}  {msg}")
def header(msg): print(f"\n{C.BOLD}{msg}{C.END}")


# Install hints shown inline when a tool is missing.
# pip tools are grouped at the end into a single install command.
TOOL_INSTALL_HINTS: dict[str, str] = {
    # pip
    "ruff":               "pip install ruff",
    "pytest":             "pip install pytest",
    "bandit":             "pip install bandit",
    "pip-audit":          "pip install pip-audit",
    "detect-secrets":     "pip install detect-secrets",
    "pytest-cov":         "pip install pytest-cov",
    "pytest-json-report": "pip install pytest-json-report",
    # Flutter / Dart
    "flutter":            "https://flutter.dev/docs/get-started/install",
    "dart":               "Included with Flutter — install Flutter first",
}


# ─── Agent frontmatter conversion ────────────────────────────────────────────

def parse_frontmatter(content: str) -> tuple[dict, str]:
    """
    Parse YAML-ish frontmatter from a stangent agent .md file.
    Returns (frontmatter_dict, body_text).

    Handles:
      - string fields:   key: value
      - list fields:     key:\n  - item\n  - item
      - block scalars:   key: >\n  line1\n  line2  (joined to single string)

    No external dependencies — pure stdlib regex.
    """
    if not content.startswith("---\n"):
        return {}, content

    end = content.find("\n---\n", 4)
    if end == -1:
        return {}, content

    fm_text = content[4:end]
    body    = content[end + 5:]   # skip the closing "\n---\n"

    result: dict = {}
    lines = fm_text.splitlines()
    i = 0
    while i < len(lines):
        line = lines[i]
        if not line or line.startswith("#"):
            i += 1
            continue

        m = re.match(r"^(\w+):\s*(.*)", line)
        if not m:
            i += 1
            continue

        key   = m.group(1)
        value = m.group(2).strip()

        if value in ("", ">"):
            # Peek ahead for indented sub-lines (list items or block scalar)
            sub: list[str] = []
            j = i + 1
            while j < len(lines) and (lines[j].startswith("  ") or lines[j].startswith("\t")):
                sub.append(lines[j].strip())
                j += 1

            if sub and sub[0].startswith("- "):
                result[key] = [s[2:] for s in sub if s.startswith("- ")]
            elif sub:
                result[key] = " ".join(sub) if value == ">" else "\n".join(sub)
            else:
                result[key] = value
            i = j
        else:
            result[key] = value
            i += 1

    return result, body


def convert_for_claude_code(content: str, display_name: str, color: str) -> str:
    """
    Convert a stangent agent .md to Claude Code dropdown-compatible format.

    Differences between our format and Claude Code's expected format:
      - tools: YAML list  →  tools: comma-separated string
      - name:  internal   →  name: display_name (human-readable)
      - color: not present → color: <color>
      - Internal fields (version, type, inputs, outputs, profile_aware,
        allows_ask_developer, bash_allowlist, bash_blocklist) are stripped
        from the frontmatter Claude Code sees, but the agent body is
        preserved verbatim — the constraints in the body still apply.

    The returned string is ready to write to .claude/agents/<filename>.md.
    """
    fm, body = parse_frontmatter(content)

    # tools: list → "Tool1, Tool2, ..."
    tools = fm.get("tools", [])
    tools_str = ", ".join(tools) if isinstance(tools, list) else str(tools)

    # description: collapse block scalar to one line
    description = fm.get("description", "")
    if isinstance(description, str) and "\n" in description:
        description = description.split("\n")[0].strip()

    new_fm_lines = ["---", f"name: {display_name}", f"color: {color}"]
    if description:
        new_fm_lines.append(f"description: {description}")
    if tools_str:
        new_fm_lines.append(f"tools: {tools_str}")
    new_fm_lines.append("---")

    return "\n".join(new_fm_lines) + "\n" + body


# ─── Environment validation ───────────────────────────────────────────────────

def check_python_version() -> bool:
    if sys.version_info < REQUIRED_PYTHON:
        fail(f"Python {REQUIRED_PYTHON[0]}.{REQUIRED_PYTHON[1]}+ required. "
             f"Found {sys.version_info.major}.{sys.version_info.minor}")
        return False
    ok(f"Python {sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}")
    return True


def _load_dotenv() -> dict[str, str]:
    """Read .env file and return {KEY: value} without modifying os.environ."""
    env_file = Path(".env")
    if not env_file.exists():
        return {}
    result = {}
    for line in env_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        result[k.strip()] = v.strip().strip("\"'")
    return result


def _get_env(key: str, dotenv: dict[str, str]) -> str:
    """Return env var from os.environ, falling back to .env file."""
    return os.environ.get(key, dotenv.get(key, ""))


def detect_provider() -> str | None:
    """
    Auto-detect provider from environment variables.
    Checks ALL providers in priority order: anthropic, groq, openrouter,
    openai, bedrock, vertex. ollama is excluded (no env var to detect).
    Returns provider name or None if nothing is set.
    """
    PRIORITY = ["anthropic", "groq", "openrouter", "openai", "bedrock", "vertex"]
    dotenv = _load_dotenv()
    for name in PRIORITY:
        prov = PROVIDERS.get(name, {})
        if prov.get("detect_env") and any(_get_env(e, dotenv) for e in prov["detect_env"]):
            return name
    return None


def check_credentials(provider_name: str) -> bool:
    """
    Verify that all required env vars for the given provider are present.
    Reads from os.environ and .env file.
    Providers with no required_env (e.g. Ollama) always pass.
    """
    dotenv  = _load_dotenv()
    prov    = PROVIDERS[provider_name]
    all_ok  = True

    if not prov["required_env"]:
        ok(f"{prov['display']} — no API key required")
        return True

    for key in prov["required_env"]:
        val = _get_env(key, dotenv)
        if not val:
            fail(f"{key} not set (required for {prov['display']})")
            all_ok = False
        else:
            masked = val[:6] + "..." + val[-3:] if len(val) > 9 else "***"
            ok(f"{key} ({masked})")

    for key in prov["optional_env"]:
        val = _get_env(key, dotenv)
        if val:
            ok(f"{key} (optional, set)")
        # silently skip missing optionals

    return all_ok


def check_git() -> bool:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--git-dir"],
            capture_output=True, text=True, cwd=Path.cwd()
        )
        if result.returncode == 0:
            ok("git repository detected")
            return True
        fail("Not a git repository.  git init")
        print("  The pipeline agents (implement, review) require git to create")
        print("  branches and commits. Scaffolding will continue but the pipeline")
        print("  will fail until you initialise git.")
        return False
    except FileNotFoundError:
        fail("git not found in PATH.  https://git-scm.com/downloads")
        return False


def tool_in_path(tool: str) -> bool:
    return shutil.which(tool) is not None


def check_tools(
    profile_name: str,
    already_checked: set[str] | None = None,
) -> tuple[bool, list[str]]:
    """
    Check required and optional tools for a profile.
    already_checked: shared set across multiple profile calls — tools already
    verified (pass or fail) are silently skipped so they aren't reported twice.
    Prints install hints inline for any missing tool.
    Returns (all_required_found, list_of_missing_required_tools).
    """
    if already_checked is None:
        already_checked = set()

    profile   = PROFILES[profile_name]
    all_ok    = True
    missing_required: list[str] = []
    missing_pip: list[str] = []       # pip tools missing — for combined one-liner

    for tool in profile["required_tools"]:
        if tool in already_checked:
            continue
        already_checked.add(tool)
        if tool_in_path(tool):
            ok(f"{tool}")
        else:
            hint = TOOL_INSTALL_HINTS.get(tool, "")
            fail(f"{tool} — not found (required).  {hint}")
            if hint.startswith("pip install"):
                missing_pip.append(tool)
            all_ok = False
            missing_required.append(tool)

    for tool in profile.get("optional_tools", []):
        if tool in already_checked:
            continue
        already_checked.add(tool)
        if tool_in_path(tool):
            ok(f"{tool} (optional)")
        else:
            hint = TOOL_INSTALL_HINTS.get(tool, "")
            warn(f"{tool} — not found (optional).  {hint}")

    # If multiple pip tools are missing, show a single combined install command
    if len(missing_pip) > 1:
        combined = "pip install " + " ".join(missing_pip)
        print(f"\n  Install all missing {profile_name} tools at once:")
        print(f"    {combined}\n")

    return all_ok, missing_required


# ─── Profile detection ────────────────────────────────────────────────────────

def detect_profiles(project_root: Path) -> list[str]:
    """
    Return all profiles whose detection files are present.
    Checks project root first, then one level of subdirectories (monorepo support).
    """
    found = []
    # Collect all candidate directories: root + immediate subdirs
    candidates = [project_root] + [
        d for d in sorted(project_root.iterdir())
        if d.is_dir() and not d.name.startswith(".")
    ]
    for profile_name, profile in PROFILES.items():
        for detect_file in profile["detect_files"]:
            if any((d / detect_file).exists() for d in candidates):
                found.append(profile_name)
                break
    return found


def detect_src_root(project_root: Path, profile_name: str) -> str:
    """
    Best-guess src root for a single profile.

    Strategy:
    1. Find where the profile's detection file lives (root or one level deep).
       This handles monorepos where e.g. pubspec.yaml is in mobile/ not root.
    2. Inside that directory, look for the profile's standard src_root.
    3. Fall back to the raw default if nothing else matches.

    Returns a path string relative to project_root (e.g. "mobile/lib/").
    """
    profile   = PROFILES[profile_name]
    default   = profile["src_root"]          # e.g. "lib/" or "src/"
    det_files = profile["detect_files"]

    # Step 1 — find the directory that contains the profile's detection file
    anchor_dir: Path | None = None

    # Check project root first
    for det in det_files:
        if (project_root / det).exists():
            anchor_dir = project_root
            break

    # If not at root, search one level of subdirectories (monorepo support)
    if anchor_dir is None:
        for subdir in sorted(project_root.iterdir()):
            if not subdir.is_dir() or subdir.name.startswith("."):
                continue
            for det in det_files:
                if (subdir / det).exists():
                    anchor_dir = subdir
                    break
            if anchor_dir:
                break

    if anchor_dir is None:
        # Nothing found — return default unchanged
        return default

    # Step 2 — inside anchor_dir, find the src root
    if (anchor_dir / default).exists():
        src = anchor_dir / default
    else:
        src = None
        for guess in ["src/", "lib/", "app/"]:
            candidate = anchor_dir / guess
            if candidate.exists():
                src = candidate
                break
        if src is None:
            src = anchor_dir  # use the anchor dir itself as the src root

    # Step 3 — return relative to project_root, always with trailing slash
    rel = src.relative_to(project_root).as_posix()
    return rel.rstrip("/") + "/"


def detect_src_roots(project_root: Path, profile_names: list[str]) -> dict[str, str]:
    """Return {profile_name: src_root} for every profile in the list."""
    return {name: detect_src_root(project_root, name) for name in profile_names}


# ─── Config generation ────────────────────────────────────────────────────────

def build_config(
    project_root: Path,
    profile_names: list[str],
    provider_name: str = "anthropic",
) -> dict:
    primary       = profile_names[0]
    profile_roots = detect_src_roots(project_root, profile_names)
    prov          = PROVIDERS[provider_name]
    strong        = prov["default_models"]["strong"]
    fast          = prov["default_models"]["fast"]

    return {
        "_stangent_version": VERSION,
        "stangent_path": str(STANGENT_PATH),
        "provider": {
            "name":     provider_name,
            # base_url: set this to use any OpenAI-compatible endpoint
            # (Groq, Together, Ollama, Azure, local LM Studio, etc.)
            # Leave null to use the provider's default API endpoint.
            "base_url": None,
        },
        "profile":       primary,
        "profiles":      profile_names,
        "profile_roots": profile_roots,
        "paths": {
            "src_root": profile_roots[primary],
            "feature_dir": ".stangent/features/",
            "archive_dir": ".stangent/archive/",
            "log_dir": ".stangent/logs/",
            "srs_path": ".stangent/SRS.md",
            "decisions_path": ".stangent/decisions.md",
            "registry_path": ".stangent/features_registry.json",
            "context_cache": ".stangent/context_cache.md",
            "env_example": ".env.example",
        },
        "pipeline": {
            "max_retries": 3,
            "ask_developer_timeout_minutes": 30,
            "auto_branch": True,
            "branch_prefix": "stangent/",
            "remind_pr_on_complete": False,
            "pr_target_branch": "dev",
            "archive_completed_after_days": 7,
        },
        "models": {
            "orchestrator":     strong,
            "planner":          strong,
            "implementer":      strong,
            "reviewer":         strong,
            "srs_agent":        strong,
            "linter":           fast,
            "unit_tester":      fast,
            "query_analyzer":   fast,
            "security_scanner": strong,
        },
        "feature_id": {
            "prefix": "FEAT",
            "padding": 3,
        },
        "integrations": {
            "srs_sync": {
                "enabled":   False,
                "provider":  "google_docs",
                "target_id": "",
                "trigger":   "manual",
                "mcp_tool":  "",
            },
        },
    }


# ─── Scaffolding ──────────────────────────────────────────────────────────────

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
    /feature  /plan  /implement  /review  /srs  /status  /abandon  /adr  /sync-srs

  Next: scaffold each project with:
    cd your-project && python {STANGENT_PATH}/init.py --profile <name>
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

    current_agent_files = {cfg["filename"] for cfg in DROPDOWN_AGENTS.values()}

    # Remove stale agent files from previous stangent versions
    for dst_file in sorted(agents_dst.glob("stangent*.md")):
        if dst_file.name not in current_agent_files:
            if not dry_run:
                dst_file.unlink()
            warn(f"agents/{dst_file.name} — removed (no longer in stangent)")

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


def create_srs(project_root: Path, config: dict, dry_run: bool):
    srs_path = project_root / config["paths"]["srs_path"]
    if srs_path.exists():
        ok("SRS.md already exists")
        return

    template = (STANGENT_PATH / "templates" / "srs.md").read_text(encoding="utf-8")
    project_name = project_root.name
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    template = template.replace("{{PROJECT_NAME}}", project_name)
    template = template.replace("{{PROFILE}}", config["profile"])
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
        ".stangent/logs/",
        ".stangent/context_cache.md",
        ".stangent/features_registry.json",
        ".stangent/coverage_baseline.json",
    ]
    entries_to_keep = [
        "# Keep these stangent files in git:",
        "!.stangent/config.json",
        "!.stangent/features/",
        "!.stangent/archive/",
        "!.stangent/SRS.md",
        "!.stangent/decisions.md",
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
| `/abandon FEAT-XXX` | Cleanly abandon a feature |
| `/adr <title>` | Record an architectural decision |
| `/sync-srs` | Manually push SRS to Google Docs or OneDrive |

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
/feature → PLANNING → AWAITING_CONFIRMATION → IMPLEMENTING
        → REVIEWING → SRS_UPDATE → COMPLETE
```

The pipeline pauses at AWAITING_CONFIRMATION. You must confirm the spec
before implementation begins. Review the spec at:
`.stangent/features/FEAT-XXX-<slug>.md`

## Setup

| Setting | Value |
|---------|-------|
| Profiles | {", ".join(config.get("profiles", [config["profile"]]))} |
| Provider | {provider_name} |
| Stangent | `{config["stangent_path"]}` |

## Decisions Log

When the team makes an architectural decision, record it in:
`.stangent/decisions.md`

All agents read this file. They will honour your decisions automatically.

---
Generated by stangent v{VERSION} on {datetime.now(timezone.utc).strftime("%Y-%m-%d")}
"""

    action = "Updated" if doc_path.exists() else "Created"
    if not dry_run:
        doc_path.write_text(content, encoding="utf-8")
    info(f"{action} .stangent/HOW_THIS_WORKS.md")


# ─── Integrations setup ──────────────────────────────────────────────────────

def configure_srs_sync(config: dict, config_path: Path, dry_run: bool):
    """
    Optionally configure SRS → Google Docs / OneDrive sync.
    Only prompts if sync is not already enabled. Skipped silently on dry-run.
    """
    srs_sync = config.get("integrations", {}).get("srs_sync", {})
    if srs_sync.get("enabled"):
        provider = srs_sync.get("provider", "?")
        target   = srs_sync.get("target_id", "?")
        ok(f"SRS sync — already configured ({provider}: {target})")
        return

    if dry_run:
        info("SRS sync — skipped (dry-run)")
        return

    header("SRS Sync (optional)")
    print("  Push your SRS to Google Docs or OneDrive after each feature,")
    print("  so project managers can read it without repository access.")
    print()
    print("  Requires:")
    print("    • An MCP server installed and configured in ~/.claude/settings.json")
    print("    • Google Docs: OAuth app credentials (GOOGLE_APPLICATION_CREDENTIALS)")
    print("    • OneDrive: no stable MCP server yet — Google Docs recommended for now")
    print()
    print(f"  Full setup guide: {STANGENT_PATH / 'agents' / 'srs_sync_agent.md'}")
    print()
    raw = input("  Set up SRS sync now? (google_docs / onedrive / skip) [skip]: ").strip().lower()
    if raw not in ("google_docs", "onedrive"):
        info("SRS sync — skipped (configure later in .stangent/config.json)")
        return

    provider = raw
    print()
    target_id = input("  Document/file ID or path: ").strip()
    if not target_id:
        warn("No target ID provided — SRS sync not configured.")
        return

    mcp_tool_default = (
        "mcp__gdrive__update_document" if provider == "google_docs"
        else "mcp__msgraph__update_file"
    )
    print(f"  MCP tool name [{mcp_tool_default}]: ", end="")
    mcp_tool = input().strip() or mcp_tool_default

    trigger_raw = input("  Trigger: (on_complete / manual) [on_complete]: ").strip().lower()
    trigger = trigger_raw if trigger_raw in ("on_complete", "manual") else "on_complete"

    # Write into config and save
    if "integrations" not in config:
        config["integrations"] = {}
    config["integrations"]["srs_sync"] = {
        "enabled":   True,
        "provider":  provider,
        "target_id": target_id,
        "trigger":   trigger,
        "mcp_tool":  mcp_tool,
    }
    if not dry_run:
        config_path.write_text(json.dumps(config, indent=2))
    ok(f"SRS sync configured — {provider} ({target_id}), trigger: {trigger}")
    print()
    print(f"  See {STANGENT_PATH / 'agents' / 'srs_sync_agent.md'} for MCP setup instructions.")


# ─── Main ─────────────────────────────────────────────────────────────────────

def run(args):
    project_root = Path.cwd()
    dry_run = args.dry_run
    verify_only = args.verify

    if dry_run:
        print(f"\n{C.WARN} DRY RUN — no files will be written\n")

    # ── Global install (short-circuits everything else) ────────────────────
    if getattr(args, "global_install", False):
        install_global(dry_run)
        return

    # ── 1. Environment checks ──────────────────────────────────────────────

    header("Environment Checks")
    env_ok = True
    env_ok &= check_python_version()
    env_ok &= check_git()

    # ── 1b. Provider detection + credential check ──────────────────────────

    header("Provider")

    if args.provider:
        if args.provider not in PROVIDERS:
            fail(f"Unknown provider: {args.provider}. "
                 f"Valid: {', '.join(PROVIDERS)}")
            sys.exit(1)
        provider_name = args.provider
        ok(f"Provider: {PROVIDERS[provider_name]['display']} (from --provider flag)")
    else:
        # Try existing config.json first, then env var auto-detect
        existing_config = project_root / ".stangent" / "config.json"
        if not existing_config.exists():
            existing_config = project_root / "config.json"  # legacy fallback
        if existing_config.exists():
            try:
                cfg_data = json.loads(existing_config.read_text(encoding="utf-8"))
                provider_name = cfg_data.get("provider", {}).get("name") or \
                                cfg_data.get("provider") or "anthropic"
                if isinstance(provider_name, str) and provider_name in PROVIDERS:
                    ok(f"Provider: {PROVIDERS[provider_name]['display']} (from config.json)")
                else:
                    provider_name = None
            except Exception:
                provider_name = None
        else:
            provider_name = None

        if not provider_name:
            provider_name = detect_provider()
            if provider_name:
                ok(f"Provider: {PROVIDERS[provider_name]['display']} (auto-detected)")
            else:
                warn("Could not auto-detect provider.")
                print("\n  Supported providers:")
                for name, p in PROVIDERS.items():
                    print(f"    {name:<12} — {p['display']}")
                provider_name = input("\n  Enter provider name: ").strip().lower()
                if provider_name not in PROVIDERS:
                    fail(f"Unknown provider: {provider_name}")
                    sys.exit(1)

    env_ok &= check_credentials(provider_name)

    # ── 2. Profile detection ───────────────────────────────────────────────

    header("Profile Detection")

    if args.profile:
        # Accept comma-separated: --profile python,flutter
        requested = [p.strip().lower() for p in args.profile.split(",") if p.strip()]
        unknown = [p for p in requested if p not in PROFILES]
        if unknown:
            fail(f"Unknown profile(s): {', '.join(unknown)}. "
                 f"Valid: {', '.join(PROFILES)}")
            sys.exit(1)
        profile_names = requested
        ok(f"Profile(s): {', '.join(profile_names)} (from --profile flag)")
    else:
        profile_names = detect_profiles(project_root)
        if profile_names:
            ok(f"Profile(s): {', '.join(profile_names)} (auto-detected)")
        else:
            warn("Could not auto-detect profile.")
            print("\n  Supported profiles:")
            for name in PROFILES:
                print(f"    {name}")
            print("  You can enter multiple, comma-separated (e.g. python,flutter)")
            raw = input("\n  Enter profile name(s): ").strip().lower()
            profile_names = [p.strip() for p in raw.split(",") if p.strip()]
            unknown = [p for p in profile_names if p not in PROFILES]
            if unknown or not profile_names:
                fail(f"Unknown or empty profile(s): {', '.join(unknown or ['(none)'])}")
                sys.exit(1)

    # ── 3. Tool checks ─────────────────────────────────────────────────────

    all_tools_ok  = True
    all_missing: list[str] = []
    checked_tools: set[str] = set()   # shared across profiles — deduplicates shared tools

    for pname in profile_names:
        header(f"Tool Checks ({pname})")
        t_ok, missing = check_tools(pname, checked_tools)
        if not t_ok:
            all_tools_ok = False
            all_missing.extend(f"{pname}:{m}" for m in missing)

    if not all_tools_ok:
        print(f"\n  Missing required tools: {', '.join(all_missing)}")
        print("  See profiles/<name>.md for install instructions.")
        if not verify_only:
            proceed = input("\n  Continue anyway? Some sub-agents may fail. (yes/no): ").strip().lower()
            if proceed != "yes":
                sys.exit(1)

    if verify_only:
        header("Verification Complete")
        if env_ok and all_tools_ok:
            ok("All checks passed.")
        else:
            warn("Some checks failed. See above.")
        return

    # ── 4. Scaffolding ─────────────────────────────────────────────────────

    header("Project Scaffolding")

    # .stangent/ must exist before we write config inside it
    create_stangent_dirs(project_root, dry_run)

    # Validate profile .md files exist before generating config.
    # Agents read these at runtime — a missing profile causes silent failures.
    profiles_ok = True
    for pname in profile_names:
        profile_md = STANGENT_PATH / "profiles" / f"{pname}.md"
        if profile_md.exists():
            ok(f"profiles/{pname}.md — found")
        else:
            fail(f"profiles/{pname}.md — NOT FOUND in stangent installation")
            print(f"  Expected: {profile_md}")
            print(f"  The '{pname}' profile is referenced but its definition file is missing.")
            print(f"  Agents will fail at runtime when they try to read it.")
            profiles_ok = False
    if not profiles_ok:
        proceed = input("\n  Profile files missing. Continue anyway? (yes/no): ").strip().lower()
        if proceed != "yes":
            sys.exit(1)

    config      = build_config(project_root, profile_names, provider_name)
    config_path = project_root / ".stangent" / "config.json"

    # Migration: old stangent used config.json at project root
    old_config_path = project_root / "config.json"
    if old_config_path.exists() and not config_path.exists():
        warn("Found legacy config.json at project root — migrating to .stangent/config.json")
        if not dry_run:
            config_path.write_text(old_config_path.read_text(encoding="utf-8"), encoding="utf-8")
            old_config_path.unlink()
        info("Migrated — you can delete config.json from your project root")

    if config_path.exists():
        existing = json.loads(config_path.read_text(encoding="utf-8"))
        stangent_path_changed = existing.get("stangent_path") != str(STANGENT_PATH)
        old_version = existing.get("_stangent_version", "0.0.0")

        # ── Always overwrite: structural fields the user never edits ─────────
        existing["stangent_path"]     = str(STANGENT_PATH)
        existing["_stangent_version"] = VERSION
        existing["profile"]           = config["profile"]
        existing["profiles"]          = config["profiles"]
        existing["profile_roots"]     = config["profile_roots"]

        # Detect provider change — if provider switched, reset models to the
        # new provider's defaults (old model IDs are incompatible).
        old_provider_name = (existing.get("provider") or {}).get("name", "")
        provider_changed  = old_provider_name != provider_name
        existing["provider"] = config["provider"]
        if provider_changed and old_provider_name:
            existing["models"] = config["models"]
            warn(f"Provider changed ({old_provider_name} → {provider_name}) — "
                 f"model names reset to {provider_name} defaults")

        # ── Deep merge: add new keys from template, keep user values ─────────
        # For each dict section in the fresh config, copy over any keys that
        # are missing from the existing config. User-set values are untouched.
        # Skip models if we just reset them due to a provider change.
        new_keys: list[str] = []
        sections_to_merge = ("pipeline", "paths", "feature_id", "integrations") \
                            + (() if provider_changed else ("models",))
        for section in sections_to_merge:
            fresh_section = config.get(section, {})
            if section not in existing:
                existing[section] = fresh_section
                new_keys.append(section)
            else:
                for key, value in fresh_section.items():
                    if key not in existing[section]:
                        existing[section][key] = value
                        new_keys.append(f"{section}.{key}")

        # ── Key renames: migrate old names to new ones ────────────────────────
        # Format: (section, old_key, new_key)
        _RENAMES: list[tuple[str, str, str]] = [
            ("pipeline", "auto_pr_on_complete", "remind_pr_on_complete"),
        ]
        renamed: list[str] = []
        for section, old_key, new_key in _RENAMES:
            sec = existing.get(section, {})
            if old_key in sec and new_key not in sec:
                sec[new_key] = sec.pop(old_key)
                renamed.append(f"{section}.{old_key} → {new_key}")

        config = existing
        if not dry_run:
            config_path.write_text(json.dumps(config, indent=2))

        if stangent_path_changed:
            ok(".stangent/config.json — updated stangent_path (installation moved)")
        elif provider_changed and old_provider_name:
            ok(f".stangent/config.json — provider switched "
               f"({old_provider_name} → {provider_name}), models reset to defaults")
        elif new_keys or renamed or old_version != VERSION:
            details = []
            if new_keys:
                details.append(f"added: {', '.join(new_keys)}")
            if renamed:
                details.append(f"renamed: {', '.join(renamed)}")
            ok(f".stangent/config.json — upgraded v{old_version} → v{VERSION}"
               + (f" ({'; '.join(details)})" if details else ""))
        else:
            ok(f".stangent/config.json — up to date ({provider_name}, {', '.join(profile_names)})")
    else:
        if not dry_run:
            config_path.write_text(json.dumps(config, indent=2))
        info(f".stangent/config.json — created ({provider_name}, {', '.join(profile_names)})")
    # ── 4b. Optional SRS sync setup ────────────────────────────────────────
    configure_srs_sync(config, config_path, dry_run)

    init_registry(project_root, config, dry_run)
    copy_commands(project_root, config, dry_run)
    copy_claude_agents(project_root, dry_run)
    create_srs(project_root, config, dry_run)
    create_decisions(project_root, config, dry_run)
    create_env_example(project_root, dry_run)
    update_gitignore(project_root, dry_run)
    create_onboarding_doc(project_root, config, dry_run)

    # ── 5. Summary ─────────────────────────────────────────────────────────

    header("Done")

    global_installed = GLOBAL_AGENTS_DIR.exists() and any(GLOBAL_AGENTS_DIR.glob("stangent*.md"))
    global_hint = (
        "" if global_installed else
        f"\n  Tip: Run 'python {STANGENT_PATH}/init.py --global' once to make\n"
        "  agents available in ALL projects without per-project init.\n"
    )

    roots_display = "  ".join(
        f"{n}: {r}" for n, r in config["profile_roots"].items()
    )
    print(f"""
  Project:   {project_root.name}
  Profiles:  {', '.join(config['profiles'])}
  Roots:     {roots_display}
  Stangent:  {STANGENT_PATH}
{global_hint}
  Open Claude Code and use the Stangent agent from the mode selector,
  or type /feature <describe what you want to build>

  See .stangent/HOW_THIS_WORKS.md for full documentation.
""")

    if not env_ok or not all_tools_ok:
        warn("Some environment checks failed. Fix them before running the pipeline.")


def main():
    parser = argparse.ArgumentParser(
        description="Initialize Stangent — global install or per-project scaffold.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--global", dest="global_install", action="store_true",
        help=(
            "Install agents and commands to ~/.claude/ so they appear in "
            "every project without per-project init. Run once, ever."
        ),
    )
    parser.add_argument(
        "--provider",
        help=(
            "LLM provider to use. "
            "Options: anthropic | openai | bedrock | vertex. "
            "Auto-detected from environment if not set."
        ),
    )
    parser.add_argument(
        "--profile",
        help="Override auto-detected profile(s). Single: python  Multiple: python,flutter",
    )
    parser.add_argument("--dry-run", action="store_true",
                        help="Show what would be done without writing anything")
    parser.add_argument("--verify", action="store_true",
                        help="Only run environment validation, no scaffolding")
    args = parser.parse_args()
    run(args)


if __name__ == "__main__":
    main()
