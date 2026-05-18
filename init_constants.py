"""
Stangent constants: providers, profiles, directory layout, agent configs,
console formatting helpers, and tool install hints.
"""

from pathlib import Path


STANGENT_PATH = Path(__file__).parent.resolve()
REQUIRED_PYTHON = (3, 10)
VERSION = "1.0.0"

PROFILES = {
    "flutter": {
        "detect_files":   ["pubspec.yaml"],
        "required_tools": ["flutter", "dart"],
        "optional_tools": ["detect-secrets"],
        "src_root":       "lib/",
    },
    "python": {
        "detect_files":   ["pyproject.toml", "requirements.txt", "setup.py"],
        "required_tools": ["ruff", "pytest", "bandit", "pip-audit", "detect-secrets"],
        "optional_tools": ["pytest-cov", "pytest-json-report"],
        "src_root":       "src/",
    },
    "fastapi": {
        # Content-based detection: checks for 'fastapi' inside requirements.txt
        # or pyproject.toml. Falls back to --profile fastapi if auto-detect fails.
        "detect_files":    ["pyproject.toml", "requirements.txt"],
        "detect_content":  ["fastapi"],   # init.py checks file content for this string
        "required_tools":  ["ruff", "pytest", "pytest-asyncio", "httpx",
                            "bandit", "pip-audit", "detect-secrets"],
        "optional_tools":  ["pytest-cov", "pytest-json-report", "asgi-lifespan"],
        "src_root":        "src/",
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
    ".stangent/profiles/",
    ".stangent/templates/",
    ".stangent/prompts/",
    ".stangent/contracts/",
    ".stangent/gateway/",
    ".stangent/memory/",
]

# Project-level (scoped to one repo)
CLAUDE_COMMANDS_DIR  = ".claude/commands/"
CLAUDE_AGENTS_DIR    = ".claude/agents/"
CLAUDE_SUBAGENTS_DIR = ".claude/agents/subagents/"

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
        "display_name": "Orchestrator",
        "color":        "purple",
        "filename":     "stangent.md",
    },
    "planner": {
        "display_name": "Planner",
        "color":        "blue",
        "filename":     "stangent-planner.md",
    },
    "implementer": {
        "display_name": "Implementer",
        "color":        "cyan",
        "filename":     "stangent-implementer.md",
    },
    "reviewer": {
        "display_name": "Reviewer",
        "color":        "orange",
        "filename":     "stangent-reviewer.md",
    },
    "srs_agent": {
        "display_name": "SRS Agent",
        "color":        "green",
        "filename":     "stangent-srs.md",
    },
    "adr_agent": {
        "display_name": "ADR Agent",
        "color":        "yellow",
        "filename":     "stangent-adr.md",
    },
}

# Sub-agents: deployed to .claude/agents/subagents/ but without display_name/color so
# they are invisible in the Claude Code dropdown. Referenced at runtime by the
# main agents using the local path — no dependency on the stangent repo at runtime.
# Maps source path (relative to stangent/agents/) → output filename in .claude/agents/
SUBAGENTS: dict[str, str] = {
    "subagents/linter":           "stangent-linter.md",
    "subagents/unit_tester":      "stangent-unit-tester.md",
    "subagents/query_analyzer":   "stangent-query-analyzer.md",
    "subagents/security_scanner": "stangent-security-scanner.md",
}


# ─── Formatting helpers ───────────────────────────────────────────────────────

class C:
    OK   = "\033[92m✓\033[0m"
    FAIL = "\033[91m✗\033[0m"
    WARN = "\033[93m⚠\033[0m"
    INFO = "\033[94m•\033[0m"
    BOLD = "\033[1m"
    END  = "\033[0m"


def ok(msg):     print(f"  {C.OK}  {msg}")
def fail(msg):   print(f"  {C.FAIL}  {msg}")
def warn(msg):   print(f"  {C.WARN}  {msg}")
def info(msg):   print(f"  {C.INFO}  {msg}")
def header(msg): print(f"\n{C.BOLD}{msg}{C.END}")


# Install hints shown inline when a tool is missing.
# pip tools are grouped at the end into a single install command.
TOOL_INSTALL_HINTS: dict[str, str] = {
    # pip
    "ruff":               "pip install ruff",
    "pytest":             "pip install pytest",
    "pytest-asyncio":     "pip install pytest-asyncio",
    "httpx":              "pip install httpx",
    "asgi-lifespan":      "pip install asgi-lifespan",
    "bandit":             "pip install bandit",
    "pip-audit":          "pip install pip-audit",
    "detect-secrets":     "pip install detect-secrets",
    "pytest-cov":         "pip install pytest-cov",
    "pytest-json-report": "pip install pytest-json-report",
    # Flutter / Dart
    "flutter":            "https://flutter.dev/docs/get-started/install",
    "dart":               "Included with Flutter — install Flutter first",
}
