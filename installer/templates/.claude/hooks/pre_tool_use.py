#!/usr/bin/env python3
"""Hard safety hook. Blocks destructive and out-of-contract operations.

Reads Claude Code's PreToolUse JSON payload on stdin. Exits 2 (with a message
on stdout) to deny the call. Exits 0 to allow.

Two kinds of rule live here:

  1. **Hard safety** (role-independent) — destructive shell commands and writes
     outside the repo root. These fire for every agent, always.

  2. **Role-scoped contracts** (Phase 2) — enforced only when the dispatcher has
     written `.claude/state/current_role.txt`. Two rules:
       - directory-restricted roles (auditor/debugger/planner/reviewer/sketcher/
         architect/security-reviewer) may only write under their whitelisted
         prefixes;
       - no subagent may run a git mutation (commit/push/merge/rebase/...).
     If no role state is set the role rules fail OPEN — this hook never guesses.

MCP tool gating (e.g. "planner must not call retrieve") is intentionally NOT
enforced here: it is already owned by each agent's `tools:` frontmatter at the
harness level. Duplicating it would create two sources of truth that drift.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

# Derive the repo root from THIS file's location, not the process cwd: the hook
# runs with an unreliable cwd (and an often-unset CLAUDE_PROJECT_DIR), so a
# cwd-based root misjudged the repo boundary whenever cwd drifted into a
# subdirectory. __file__ is <repo>/.claude/hooks/pre_tool_use.py → parents[2].
REPO_ROOT = Path(__file__).resolve().parents[2]
AGENTIC_YML = REPO_ROOT / ".claude" / ".agentic.yml"
ROLE_STATE = REPO_ROOT / ".claude" / "state" / "current_role.txt"

# Role-independent destructive patterns (in addition to `rm -rf`, handled by
# is_dangerous_rm, and the user's gateway.deny list).
HARD_PATTERNS = [
    r"\bgit\s+clean\s+-fdx\b",
    r"\bgit\s+push\b[^\n]*\s(?:--force(?:-with-lease)?|-f)\b",
    r"\bgit\s+reset\s+--hard\b",
    r"\bDROP\s+TABLE\b",
    r"\bDROP\s+DATABASE\b",
    r"\bTRUNCATE\b",
    r"\bsupabase\s+db\s+reset\b",
]

WRITE_TOOLS = {"Write", "Edit", "NotebookEdit"}

# Git subcommands that mutate history or remotes. No subagent should run these —
# commits and merges are user-driven. Read-only git (diff/log/status/show/
# ls-files) is deliberately NOT matched.
GIT_MUTATION_RE = re.compile(
    r"\bgit\s+(?:commit|push|merge|rebase|cherry-pick|revert|tag)\b",
    re.IGNORECASE,
)

# Directory-restricted roles: each may write ONLY under these repo-relative
# prefixes. A prefix ending in "/" matches any path beneath it; a prefix without
# a trailing slash matches that exact file. Roles absent from this map
# (implementer/tester/refactor) write freely, subject to the repo-boundary rule.
ROLE_WRITE_WHITELIST = {
    "auditor": [".claude/state/audit/"],
    "debugger": [".claude/state/debug/"],
    "architect": [".claude/state/design-review/"],
    "security-reviewer": [".claude/state/security-review/"],
    "planner": [".claude/state/plans/"],
    "reviewer": [".claude/state/plans/"],
    "sketcher": [".claude/state/plans/", ".claude/design/", ".claude/launch.json"],
    # designer drafts the UI design spec to state; /agentic-design promotes the
    # approved draft to committed docs/design/ (the command, not this role).
    "designer": [".claude/state/design-spec/"],
    # design-critic writes a UI-adherence findings report only.
    "design-critic": [".claude/state/ui-review/"],
}


def load_extra_patterns() -> list[str]:
    if not AGENTIC_YML.exists() or yaml is None:
        return []
    try:
        cfg = yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or {}
    except Exception:
        return []
    gateway = (cfg.get("gateway") or {}).get("deny") or []
    # Entries are treated as literal substrings (regex-escaped). Documented as
    # such in .agentic.yml.
    return [re.escape(p) for p in gateway if isinstance(p, str)]


def is_dangerous_rm(cmd: str) -> bool:
    """True if `cmd` is an `rm` invocation combining recursive AND force.

    Catches all flag orderings and long options: -rf, -fr, -Rf, -r -f,
    --recursive --force, -r --force, etc.
    """
    if not re.search(r"\brm\b", cmd, flags=re.IGNORECASE):
        return False
    recursive = bool(
        re.search(r"(?:^|\s)-[a-zA-Z]*[rR]", cmd)
        or re.search(r"--recursive\b", cmd, flags=re.IGNORECASE)
    )
    force = bool(
        re.search(r"(?:^|\s)-[a-zA-Z]*f", cmd)
        or re.search(r"--force\b", cmd, flags=re.IGNORECASE)
    )
    return recursive and force


def repo_relative(path_str: str) -> str | None:
    """Return the POSIX path of `path_str` relative to the repo root, or None if
    it does not resolve inside the repo."""
    try:
        rel = Path(path_str).resolve().relative_to(REPO_ROOT)
    except (ValueError, OSError):
        return None
    return rel.as_posix()


def is_inside_repo(path_str: str) -> bool:
    return repo_relative(path_str) is not None


def current_role() -> str | None:
    try:
        return ROLE_STATE.read_text(encoding="utf-8").strip() or None
    except OSError:
        return None


def path_allowed_for_role(rel: str, prefixes: list[str]) -> bool:
    for pre in prefixes:
        if pre.endswith("/"):
            if rel.startswith(pre):
                return True
        elif rel == pre or rel.startswith(pre + "/"):
            return True
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
    role = current_role()

    if tool == "Bash":
        cmd = (tool_input.get("command") or "").strip()

        # 1. Hard destructive patterns (role-independent).
        if is_dangerous_rm(cmd):
            deny("matched safety pattern: rm recursive+force")
        for pat in HARD_PATTERNS + load_extra_patterns():
            if re.search(pat, cmd, flags=re.IGNORECASE):
                deny(f"matched safety pattern: {pat}")

        # psql destructive guard (rough)
        if re.search(r"\bpsql\b", cmd, flags=re.IGNORECASE):
            if "migrations/" not in cmd and "migration" not in cmd:
                if re.search(r"\b(drop|truncate|delete\s+from)\b", cmd, flags=re.IGNORECASE):
                    deny("raw psql with destructive command outside migrations/")

        # 2. No git mutations while any subagent is active.
        if role and GIT_MUTATION_RE.search(cmd):
            deny(f"role '{role}' may not run git history/remote mutations — commits are user-driven")

    if tool in WRITE_TOOLS:
        target = (
            tool_input.get("file_path")
            or tool_input.get("notebook_path")
            or tool_input.get("path")
            or ""
        )
        if target:
            # 1. Writes outside the repo root (role-independent).
            rel = repo_relative(target)
            if rel is None:
                deny(f"write outside repo root denied: {target}")
            # 2. Directory-restricted roles.
            elif role in ROLE_WRITE_WHITELIST and not path_allowed_for_role(
                rel, ROLE_WRITE_WHITELIST[role]
            ):
                allowed = ", ".join(ROLE_WRITE_WHITELIST[role])
                deny(f"role '{role}' may only write under: {allowed} (got {rel})")

    sys.exit(0)


if __name__ == "__main__":
    main()
