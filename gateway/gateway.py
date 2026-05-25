#!/usr/bin/env python3
"""
Stangent Gateway v2 — PreToolUse hook for Claude Code.

Reads tool call JSON from stdin, enforces six layers in order:
  1. Hard bash blocks    — always, no feature context needed
  2. Write guard         — block Write on existing stangent-managed files (use Edit)
  2b. Bash bypass guard  — block python -c / shell writes targeting same guarded files
  3. Agent/state check  — is this agent allowed in the current pipeline state?
  4. blocked_paths      — path explicitly Out of Bounds for this feature
  5. allowed_paths      — path not in Files to Touch (system paths exempt)
  6. Contract bash blocklist + capability check

active.json format:
  { "feature_id": "FEAT-001", "state": "IMPLEMENTING",
    "agent": "implementer", "activated_at": "2026-05-12T10:00:00Z" }

contract JSON format:
  { "feature_id": "FEAT-001",
    "allowed_paths":  ["src/auth/**", "tests/auth/"],
    "blocked_paths":  ["lib/screens/home.dart"],
    "bash_blocklist": [],
    "allowed_agents": { "IMPLEMENTING": ["implementer", "linter", ...], ... } }

Exit codes: 0 = allow, 2 = block (non-zero is enough for Claude Code to block).
All decisions appended to .stangent/logs/gateway_audit.jsonl.
"""
import sys
import json
import fnmatch
import datetime
from pathlib import Path

GATEWAY_DIR   = Path(".stangent/gateway")
CONTRACTS_DIR = Path(".stangent/contracts")
AUDIT_LOG     = Path(".stangent/logs/gateway_audit.jsonl")

# These paths bypass the allowed_paths whitelist — agents always need them.
SYSTEM_PATH_PREFIXES: tuple[str, ...] = (
    ".stangent/",
    ".claude/",
    ".env.example",
    ".gitignore",
)

# Always blocked — enforced before any feature context is loaded.
HARD_BLOCKED_BASH: list[str] = [
    "git push --force",
    "git push -f ",
    "git push -f\n",
    "git reset --hard",
    "git clean -f",
    "git checkout --",
    "rm -rf",
    "DROP TABLE",
    "DELETE FROM",
]

# Stangent-managed files that must be updated via Edit (not Write) after initial
# creation. Writing them resends the full file on every call — a major token waste.
WRITE_GUARDED_PATTERNS: tuple[str, ...] = (
    ".stangent/features/FEAT-*.md",
    ".stangent/SRS.md",
    ".stangent/features_registry.json",
    ".stangent/memory.md",
)

# Default allowed agents per state — used when contract has no allowed_agents.
DEFAULT_ALLOWED_AGENTS: dict[str, list[str]] = {
    "CREATED":               ["orchestrator"],
    "PLANNING":              ["planner"],
    "AWAITING_CONFIRMATION": ["orchestrator"],
    "CONFIRMED":             ["orchestrator"],
    "IMPLEMENTING":          ["implementer", "linter", "unit_tester", "query_analyzer"],
    "REVIEWING":             ["reviewer", "security_scanner"],
    "REVIEW_PASS":           ["orchestrator"],
    "REFINING":              ["planner"],
    "SRS_UPDATE":            ["srs_agent"],
    "COMPLETE":              ["orchestrator"],
    "PAUSED":                ["orchestrator"],
    "BLOCKED":               ["orchestrator"],
    "ESCALATED":             [],
    "FAILED":                [],
    "ABANDONED":             [],
}


# ── Loaders ──────────────────────────────────────────────────────────────────

def load_active() -> dict | None:
    p = GATEWAY_DIR / "active.json"
    if not p.exists():
        return None
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return None


def load_contract(feature_id: str) -> dict | None:
    p = CONTRACTS_DIR / f"{feature_id}.json"
    if not p.exists():
        return None
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return None


# ── Matchers ─────────────────────────────────────────────────────────────────

def is_system_path(file_path: str) -> bool:
    p = Path(file_path).as_posix()
    return any(p.startswith(prefix) for prefix in SYSTEM_PATH_PREFIXES)


def is_write_guarded(file_path: str) -> bool:
    """Return True if this stangent-managed file must use Edit (not Write) after creation."""
    p = Path(file_path).as_posix()
    for pattern in WRITE_GUARDED_PATTERNS:
        if fnmatch.fnmatch(p, f"*/{pattern}") or fnmatch.fnmatch(p, pattern):
            return True
    return False


def path_matches(file_path: str, patterns: list[str]) -> str | None:
    """Return first matching pattern or None."""
    p = Path(file_path).as_posix()
    for pattern in patterns:
        pat = Path(pattern).as_posix().rstrip("/")
        if p == pat or p.startswith(pat + "/") or fnmatch.fnmatch(p, pat):
            return pattern
    return None


def bash_matches(command: str, patterns: list[str]) -> str | None:
    """Return first matching pattern (case-insensitive substring) or None."""
    cmd = command.lower()
    for pattern in patterns:
        if pattern.lower() in cmd:
            return pattern
    return None


# ── Audit log ─────────────────────────────────────────────────────────────────

def log_decision(
    decision: str,
    tool: str,
    target: str,
    reason: str | None,
    active: dict | None,
) -> None:
    """Append one JSONL line to audit log. Never raises — audit failure must not block."""
    try:
        AUDIT_LOG.parent.mkdir(parents=True, exist_ok=True)
        entry = {
            "ts":         datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "feature_id": (active or {}).get("feature_id", ""),
            "state":      (active or {}).get("state", ""),
            "agent":      (active or {}).get("agent", ""),
            "tool":       tool,
            "target":     target,
            "decision":   decision,
            "reason":     reason,
        }
        with AUDIT_LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps(entry) + "\n")
    except Exception:
        pass


# ── Decision helpers ──────────────────────────────────────────────────────────

def block(msg: str, tool: str, target: str, active: dict | None) -> None:
    log_decision("block", tool, target, msg, active)
    print(msg, file=sys.stderr)
    sys.exit(2)


def allow(tool: str, target: str, active: dict | None) -> None:
    log_decision("allow", tool, target, None, active)
    sys.exit(0)


# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    try:
        call = json.loads(sys.stdin.read())
    except Exception:
        sys.exit(0)

    tool_name  = call.get("tool_name", "")
    tool_input = call.get("tool_input", {})
    file_path  = tool_input.get("file_path", "")
    command    = tool_input.get("command", "")
    target     = file_path or command

    # ── Layer 1: Hard bash blocks (always, no feature context needed) ─────────
    if tool_name == "Bash" and command:
        matched = bash_matches(command, HARD_BLOCKED_BASH)
        if matched:
            block(
                f"[Gateway] BLOCKED — hard-blocked bash pattern: {matched!r}\n"
                f"Command: {command!r}",
                tool_name, command, None,
            )

    # ── Load feature context ──────────────────────────────────────────────────
    active     = load_active()
    feature_id = (active or {}).get("feature_id", "")
    state      = (active or {}).get("state", "")
    agent      = (active or {}).get("agent", "")
    contract   = load_contract(feature_id) if feature_id else None

    # ── Layer 2: Write-on-existing stangent-file guard ───────────────────────
    # Fires regardless of feature state. Write on an existing managed file
    # resends full contents every call — force Edit for incremental updates.
    if tool_name == "Write" and file_path and is_write_guarded(file_path):
        target_path = Path(file_path)
        if target_path.exists():
            size_kb = target_path.stat().st_size // 1024
            block(
                f"[Gateway] BLOCKED — use the Edit tool, not Write.\n"
                f"{file_path!r} already exists ({size_kb} KB).\n"
                f"Do NOT investigate this block, read gateway.py, or use python -c as a workaround.\n"
                f"Simply switch to the Edit tool with old_string/new_string:\n"
                f"  tool:       Edit\n"
                f"  file_path:  (same path)\n"
                f"  old_string: first unique line(s) of the section to replace\n"
                f"  new_string: replacement content",
                tool_name, file_path, active,
            )

    # ── Layer 2b: Bash-based write bypass guard ───────────────────────────────
    # Catches agents using python -c or shell redirection to bypass Layer 2.
    if tool_name == "Bash" and command:
        WRITE_VERBS = ("write_text", ".write(", "json.dump", "open(")
        cmd_has_write = any(v in command for v in WRITE_VERBS)
        if cmd_has_write:
            for pattern in WRITE_GUARDED_PATTERNS:
                # Check for guarded path segment in the command string
                segment = pattern.replace("*", "").replace(".stangent/", ".stangent/")
                hints = [
                    segment,
                    segment.replace("/", "\\"),
                    Path(segment).name,  # e.g. "SRS.md", "features_registry.json"
                ]
                if any(h and h in command for h in hints):
                    block(
                        f"[Gateway] BLOCKED — do not write guarded stangent files via Bash.\n"
                        f"Detected write verb in command targeting a guarded path ({segment!r}).\n"
                        f"Use the Edit tool instead — do not attempt Python or shell workarounds.",
                        tool_name, command, active,
                    )

    if not active:
        allow(tool_name, target, None)

    # ── Warn when active feature has no contract (path enforcement will be skipped) ──
    if active and feature_id and contract is None:
        log_decision("warn", tool_name, target, "no contract found for active feature — path enforcement disabled", active)

    # ── Layer 3: Agent/state check ────────────────────────────────────────────
    if agent and state and tool_name in ("Write", "Edit", "Bash"):
        contract_agents = (contract or {}).get("allowed_agents", {})
        allowed = (
            contract_agents.get(state, [])
            if contract_agents
            else DEFAULT_ALLOWED_AGENTS.get(state, [])
        )
        if allowed and agent not in allowed:
            block(
                f"[Gateway] BLOCKED — {agent!r} cannot act in state {state!r}.\n"
                f"Allowed agents for {state}: {allowed}\n"
                f"Feature: {feature_id}",
                tool_name, target, active,
            )

    # ── Layer 3 + 4: Path checks (Write and Edit only, non-system paths) ──────
    if tool_name in ("Write", "Edit") and file_path and not is_system_path(file_path):
        blocked_paths = (contract or {}).get("blocked_paths", [])
        allowed_paths = (contract or {}).get("allowed_paths", [])

        # Layer 3: blocked_paths
        matched = path_matches(file_path, blocked_paths)
        if matched:
            block(
                f"[Gateway] BLOCKED — path is Out of Bounds for {feature_id}.\n"
                f"Path:  {file_path!r}\n"
                f"Rule:  {matched!r}\n"
                f"Update ## Out of Bounds in the spec or use ASK_DEVELOPER.",
                tool_name, file_path, active,
            )

        # Layer 4: allowed_paths whitelist (only when non-empty)
        if allowed_paths:
            matched = path_matches(file_path, allowed_paths)
            if not matched:
                block(
                    f"[Gateway] BLOCKED — path not in ## Files to Touch for {feature_id}.\n"
                    f"Path:    {file_path!r}\n"
                    f"Allowed: {allowed_paths}\n"
                    f"Add this file to ## Files to Touch in the spec.",
                    tool_name, file_path, active,
                )

    # ── Layer 5: Contract bash blocklist ─────────────────────────────────────
    if tool_name == "Bash" and command:
        contract_bash = (contract or {}).get("bash_blocklist", [])
        if contract_bash:
            matched = bash_matches(command, contract_bash)
            if matched:
                block(
                    f"[Gateway] BLOCKED — bash blocked by feature contract for {feature_id}.\n"
                    f"Pattern: {matched!r}\n"
                    f"Command: {command!r}",
                    tool_name, command, active,
                )

    # ── Layer 6: Capability check (bash whitelist per agent) ──────────────────
    # Only enforced when the contract defines capabilities for the current agent.
    # Each capability entry "bash:X" means the command must contain token X.
    if tool_name == "Bash" and command and agent:
        capabilities: dict = (contract or {}).get("capabilities", {})
        agent_caps: list[str] = capabilities.get(agent, [])
        bash_caps = [c[5:] for c in agent_caps if c.startswith("bash:")]
        if bash_caps:
            # Command is allowed if it contains at least one permitted token
            cmd_lower = command.lower()
            permitted = any(cap.lower() in cmd_lower for cap in bash_caps)
            if not permitted:
                block(
                    f"[Gateway] BLOCKED — bash command not in capabilities for {agent!r}.\n"
                    f"Command:     {command!r}\n"
                    f"Permitted:   {bash_caps}\n"
                    f"Feature:     {feature_id}\n"
                    f"Use /gateway unblock or update ## Files to Touch to add this capability.",
                    tool_name, command, active,
                )

    allow(tool_name, target, active)


if __name__ == "__main__":
    main()
