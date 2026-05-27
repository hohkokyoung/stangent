#!/usr/bin/env python3
"""Hard safety hook. Blocks destructive operations from any agent.

Reads Claude Code's PreToolUse JSON payload on stdin. Exits non-zero (with a
message on stdout) to deny the call. Exits 0 to allow.

v1 rule: hard safety ONLY. No tool filtering. No context-aware gating.
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

REPO_ROOT = Path.cwd().resolve()
AGENTIC_YML = REPO_ROOT / ".claude" / ".agentic.yml"

HARD_PATTERNS = [
    r"\brm\s+-rf\b",
    r"\bgit\s+clean\s+-fdx\b",
    r"\bgit\s+push\s+(?:--force\b|-f\b)",
    r"\bgit\s+reset\s+--hard\b",
    r"\bDROP\s+TABLE\b",
    r"\bDROP\s+DATABASE\b",
    r"\bTRUNCATE\b",
    r"\bsupabase\s+db\s+reset\b",
]

WRITE_TOOLS = {"Write", "Edit", "NotebookEdit"}


def load_extra_patterns() -> list[str]:
    if not AGENTIC_YML.exists() or yaml is None:
        return []
    try:
        cfg = yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or {}
    except Exception:
        return []
    gateway = (cfg.get("gateway") or {}).get("deny") or []
    return [re.escape(p) for p in gateway if isinstance(p, str)]


def is_inside_repo(path_str: str) -> bool:
    try:
        p = Path(path_str).resolve()
    except Exception:
        return False
    try:
        p.relative_to(REPO_ROOT)
        return True
    except ValueError:
        return False


def deny(reason: str) -> None:
    sys.stdout.write(f"[agentic deny] {reason}\n")
    # Exit code 2 = deny in Claude Code hooks contract
    sys.exit(2)


def main() -> None:
    raw = sys.stdin.read() or "{}"
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        sys.exit(0)

    tool = payload.get("tool_name") or payload.get("tool") or ""
    tool_input = payload.get("tool_input") or payload.get("input") or {}

    # Raw psql outside migrations/
    if tool == "Bash":
        cmd = (tool_input.get("command") or "").strip()
        patterns = HARD_PATTERNS + load_extra_patterns()
        for pat in patterns:
            if re.search(pat, cmd, flags=re.IGNORECASE):
                deny(f"matched safety pattern: {pat}")

        # psql destructive guard (rough)
        if re.search(r"\bpsql\b", cmd, flags=re.IGNORECASE):
            if "migrations/" not in cmd and "migration" not in cmd:
                if re.search(r"\b(drop|truncate|delete\s+from)\b", cmd, flags=re.IGNORECASE):
                    deny("raw psql with destructive command outside migrations/")

    # Writes outside repo root
    if tool in WRITE_TOOLS:
        target = tool_input.get("file_path") or tool_input.get("path") or ""
        if target and not is_inside_repo(target):
            deny(f"write outside repo root denied: {target}")

    sys.exit(0)


if __name__ == "__main__":
    main()
