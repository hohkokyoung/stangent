"""
Stangent config: build_config, validate_config, parse_frontmatter,
and convert_for_claude_code.
"""

import re
from pathlib import Path

from init_constants import PROVIDERS, VERSION
from init_env import detect_src_roots


_REQUIRED_CONFIG_FIELDS: list[tuple[str, str]] = [
    ("profiles", ""),
    ("paths", "feature_dir"),
    ("paths", "log_dir"),
    ("paths", "decisions_path"),
    ("paths", "registry_path"),
    ("pipeline", "max_retries"),
    ("pipeline", "ask_developer_timeout_minutes"),
]


def validate_config(config: dict) -> list[str]:
    """
    Return a list of missing field descriptions.
    Empty list means the config is valid.
    """
    missing: list[str] = []
    for section, key in _REQUIRED_CONFIG_FIELDS:
        if section not in config:
            missing.append(section)
            continue
        if key and key not in config[section]:
            missing.append(f"{section}.{key}")
    return missing


def parse_frontmatter(content: str) -> tuple[dict, str]:
    """
    Parse YAML-ish frontmatter from a stangent agent .md file.
    Returns (frontmatter_dict, body_text).

    Handles:
      - string fields:   key: value
      - quoted strings:  key: "value with : colons" or key: 'value'
      - list fields:     key:\n  - item\n  - item
      - block scalars:   key: >\n  line1\n  line2  (joined to single string)

    No external dependencies — pure stdlib.
    """
    if not content.startswith("---\n"):
        return {}, content

    end = content.find("\n---\n", 4)
    if end == -1:
        return {}, content

    fm_text = content[4:end]
    body    = content[end + 5:]

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

        # Strip surrounding quotes from scalar values
        if (value.startswith('"') and value.endswith('"')) or \
           (value.startswith("'") and value.endswith("'")):
            result[key] = value[1:-1]
            i += 1
            continue

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
                result[key] = ""
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
        "provider": {
            "name":     provider_name,
            "base_url": None,
        },
        "profiles":      profile_names,
        "profile_roots": profile_roots,
        "paths": {
            "src_root":       profile_roots[primary],
            "feature_dir":    ".stangent/features/",
            "archive_dir":    ".stangent/archive/",
            "log_dir":        ".stangent/logs/",
            "srs_path":       ".stangent/SRS.md",
            "decisions_path": ".stangent/decisions.md",
            "registry_path":  ".stangent/features_registry.json",
            "context_cache":  ".stangent/context_cache.md",
            "env_example":    ".env.example",
            "profiles_dir":   ".stangent/profiles/",
            "templates_dir":  ".stangent/templates/",
            "prompts_dir":    ".stangent/prompts/",
            "contracts_dir":  ".stangent/contracts/",
            "gateway_path":   ".stangent/gateway/gateway.py",
        },
        "pipeline": {
            "max_retries":                   3,
            "ask_developer_timeout_minutes": 30,
            "agent_context_budget_chars":    300000,
            "auto_branch":                   True,
            "branch_prefix":                 "stangent/",
            "remind_pr_on_complete":         False,
            "pr_target_branch":              "dev",
            "archive_completed_after_days":  7,
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
            "prefix":  "FEAT",
            "padding": 3,
        },
        "integrations": {
            "dbhub": {
                "enabled":    False,
                "mcp_server": "dbhub",
            },
        },
    }
