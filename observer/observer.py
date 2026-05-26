#!/usr/bin/env python3
"""
Stangent Observer — PostToolUse hook for Claude Code.

Appends one JSONL line per tool call to {log_dir}/{feature_id}.jsonl.
Captures: file path, char count (for reads), action type, agent, timestamp.

Exit code is ignored by Claude Code for PostToolUse — never raises.
"""
import sys
import json
import subprocess
import datetime
from pathlib import Path

GATEWAY_DIR = Path(".stangent/gateway")
CONFIG_PATH = Path(".stangent/config.json")

TRACKED_TOOLS = {"Read", "Write", "Edit", "Glob", "Grep", "Bash"}

# Branch prefix used by all stangent feature branches.
BRANCH_PREFIX = "stangent/FEAT-"


def _infer_from_branch() -> dict | None:
    """
    Fallback: infer feature_id from the current git branch name.
    Returns a minimal active dict with agent/state as 'unknown'.
    Used when active.json is missing (e.g. orchestrator forgot to write it
    during inline direct planning).
    """
    try:
        result = subprocess.run(
            ["git", "branch", "--show-current"],
            capture_output=True, text=True, timeout=2,
        )
        branch = result.stdout.strip()
        if branch.startswith(BRANCH_PREFIX):
            # e.g. "stangent/FEAT-014-discover-filter-state-provider"
            slug = branch[len("stangent/"):]          # "FEAT-014-discover-..."
            feature_id = slug.split("-")[0] + "-" + slug.split("-")[1]  # "FEAT-014"
            return {"feature_id": feature_id, "state": "unknown", "agent": "unknown"}
    except Exception:
        pass
    return None


def load_active() -> dict | None:
    p = GATEWAY_DIR / "active.json"
    if p.exists():
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except Exception:
            pass
    # active.json missing — try to infer from git branch
    return _infer_from_branch()


def load_log_dir() -> Path | None:
    if not CONFIG_PATH.exists():
        return None
    try:
        cfg = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        return Path(cfg.get("paths", {}).get("log_dir", ".stangent/logs"))
    except Exception:
        return None


def extract_chars(tool_response: object) -> int:
    """
    Best-effort char count from tool response.
    Claude Code PostToolUse passes content in several shapes:
      - str  (legacy / simple text)
      - {"content": "str"}
      - {"content": [{"type": "text", "text": "str"}, ...]}  (content blocks)
    """
    if isinstance(tool_response, str):
        return len(tool_response)
    if isinstance(tool_response, dict):
        content = tool_response.get("content", "")
        if isinstance(content, str):
            return len(content)
        if isinstance(content, list):
            total = 0
            for block in content:
                if isinstance(block, dict):
                    # content block: {"type": "text", "text": "..."}
                    total += len(block.get("text", ""))
                else:
                    total += len(str(block))
            return total
    return 0


def main() -> None:
    try:
        call = json.loads(sys.stdin.read())
    except Exception:
        sys.exit(0)

    tool_name = call.get("tool_name", "")
    if tool_name not in TRACKED_TOOLS:
        sys.exit(0)

    active = load_active()
    if not active:
        sys.exit(0)

    feature_id = active.get("feature_id", "")
    if not feature_id:
        sys.exit(0)

    log_dir = load_log_dir()
    if not log_dir:
        sys.exit(0)

    tool_input    = call.get("tool_input", {})
    # Claude Code may use "tool_response" or "output" depending on version
    tool_response = call.get("tool_response") or call.get("output", "")

    entry: dict = {
        "ts":         datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "feature_id": feature_id,
        "agent":      active.get("agent", ""),
        "state":      active.get("state", ""),
    }

    if tool_name == "Read":
        entry["action"] = "file_read"
        entry["target"] = tool_input.get("file_path", "")
        entry["chars"]  = extract_chars(tool_response)

    elif tool_name in ("Write", "Edit"):
        entry["action"] = "file_write"
        entry["target"] = tool_input.get("file_path", "")

    elif tool_name == "Bash":
        cmd = tool_input.get("command", "")
        entry["action"] = "bash_run"
        entry["target"] = cmd[:300]  # truncate; full commands can be huge

    elif tool_name == "Glob":
        entry["action"] = "glob"
        entry["target"] = tool_input.get("pattern", "")

    elif tool_name == "Grep":
        entry["action"] = "grep"
        entry["target"] = tool_input.get("pattern", "")
        entry["path"]   = tool_input.get("path", "")

    try:
        log_dir.mkdir(parents=True, exist_ok=True)
        log_path = log_dir / f"{feature_id}.jsonl"
        with log_path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(entry) + "\n")
    except Exception:
        pass

    sys.exit(0)


if __name__ == "__main__":
    main()
