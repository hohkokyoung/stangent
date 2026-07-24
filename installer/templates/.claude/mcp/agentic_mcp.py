#!/usr/bin/env python3
"""agentic_mcp — MCP server exposing the internal code-context tools.

  - retrieve(query, k, skills)  — semantic search over skill references and
    indexed project source. Answers "where does this concept live?".
  - get_symbol(name, file)      — structural fetch of one function/class/method
    by name. Answers "give me exactly this, nothing around it" so an agent need
    not Read a whole file to edit a fragment. Dependency-free (see symbols.py).

Design principle (deliberate, bounded exception):

    MCP is for EXTERNAL systems. The one internal exception is the **code-context
    layer** — read-only retrieval of code and references that Claude Code happens
    to consume as MCP. `retrieve` and `get_symbol` are that layer, and it stops
    there. ORCHESTRATION never goes here: planning, task decomposition, run/task
    state, dispatch, and status stay in commands/hooks/state — not in MCP tools.

Adding a tool here is only justified if it is read-only code/reference context
of the same kind. Anything that mutates workflow state or drives dispatch does
not belong on this server.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()
RETRIEVER = REPO_ROOT / ".claude" / "hooks" / "lib" / "retriever.py"
SYMBOLS = REPO_ROOT / ".claude" / "hooks" / "lib" / "symbols.py"
STATE_DIR = REPO_ROOT / ".claude" / "state"

# Per-task retrieve budget. The v1 contract is "one call per agent per task,
# rarely a second". We hard-cap at 2 here so the budget is enforced by the
# server (which sees every call) rather than trusted to each agent prompt.
# The MCP server outlives a single dispatch, so the key includes the mtime of
# current_task.txt: the dispatcher rewrites that file on every dispatch, so a
# re-run or retry of the same (run, task, role) gets a fresh mtime and a fresh
# budget. Without the mtime, a blocked-then-retried task would be denied its
# very first retrieve because the count from the prior attempt survives.
MAX_RETRIEVE_PER_TASK = 2
_retrieve_counts: dict[tuple, int] = {}


def _read_state(filename: str) -> str | None:
    try:
        return (STATE_DIR / filename).read_text(encoding="utf-8").strip() or None
    except OSError:
        return None


def _task_key() -> tuple:
    try:
        stamp = (STATE_DIR / "current_task.txt").stat().st_mtime_ns
    except OSError:
        stamp = None
    return (
        _read_state("current_run.txt"),
        _read_state("current_task.txt"),
        _read_state("current_role.txt"),
        stamp,
    )


def resolve_python() -> str:
    """Pick a Python interpreter that has the retriever's dependencies.

    The MCP server is launched by whatever ``python3`` is on Claude Code's
    PATH (see .mcp.json). That interpreter is frequently NOT the one the
    project installed ``fastembed`` / ``sqlite-vec`` / ``voyageai`` into —
    deps usually live in a project virtualenv. Running the retriever with
    ``sys.executable`` (the launcher) then fails with ModuleNotFoundError and
    a non-zero exit, even though running retriever.py directly inside the
    venv works fine. Resolve the venv interpreter so the subprocess matches
    the environment the deps were installed into.
    """
    candidates: list[Path] = []
    venv = os.environ.get("VIRTUAL_ENV")
    if venv:
        candidates.append(Path(venv))
    for name in (".venv", "venv", "env"):
        candidates.append(REPO_ROOT / name)
    for base in candidates:
        for sub in ("bin/python", "bin/python3", "Scripts/python.exe"):
            p = base / sub
            if p.exists():
                return str(p)
    # No project venv found — fall back to the launching interpreter.
    return sys.executable


PYTHON = resolve_python()


def _serve():
    """Minimal stdio JSON-RPC MCP server.

    Implements the subset of MCP needed by Claude Code:
      - initialize
      - tools/list
      - tools/call (name="retrieve")
    """
    def write(msg: dict) -> None:
        body = json.dumps(msg)
        sys.stdout.write(body + "\n")
        sys.stdout.flush()

    def reply(req_id, result=None, error=None):
        out = {"jsonrpc": "2.0", "id": req_id}
        if error is not None:
            out["error"] = error
        else:
            out["result"] = result
        write(out)

    def call_retrieve(query: str, k: int, skills: list[str] | None) -> list[dict]:
        cmd = [PYTHON, str(RETRIEVER), "query", query, str(k)]
        for s in skills or []:
            cmd += ["--skill", s]
        try:
            proc = subprocess.run(cmd, cwd=str(REPO_ROOT), text=True, capture_output=True)
        except Exception as e:
            return [{"error": f"could not launch retriever ({PYTHON}): {e}"}]
        if proc.returncode != 0:
            # Surface stderr — without it the failure is just "exit status 1",
            # which is undiagnosable. The usual cause is missing deps in PYTHON.
            tail = " | ".join((proc.stderr or "").strip().splitlines()[-5:])
            return [{"error": f"retriever exited {proc.returncode} (interpreter: {PYTHON}): {tail}"}]
        out = (proc.stdout or "").strip()
        if not out:
            return [{"error": "retriever produced no output"}]
        try:
            return json.loads(out.splitlines()[-1])
        except Exception as e:
            return [{"error": f"could not parse retriever output: {e}"}]

    def call_symbols(name: str, file: str | None, max_matches: int) -> list[dict]:
        cmd = [PYTHON, str(SYMBOLS), "get", name, "--max", str(max_matches)]
        if file:
            cmd += ["--file", file]
        try:
            proc = subprocess.run(cmd, cwd=str(REPO_ROOT), text=True, capture_output=True)
        except Exception as e:
            return [{"error": f"could not launch symbols ({PYTHON}): {e}"}]
        if proc.returncode != 0:
            tail = " | ".join((proc.stderr or "").strip().splitlines()[-5:])
            return [{"error": f"symbols exited {proc.returncode} (interpreter: {PYTHON}): {tail}"}]
        out = (proc.stdout or "").strip()
        if not out:
            return []  # no matches is a valid, non-error result
        try:
            return json.loads(out)
        except Exception as e:
            return [{"error": f"could not parse symbols output: {e}"}]

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            continue

        method = req.get("method")
        req_id = req.get("id")

        if method == "initialize":
            reply(req_id, {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "agentic_mcp", "version": "1.0.0"},
            })
        elif method == "tools/list":
            reply(req_id, {
                "tools": [
                    {
                        "name": "retrieve",
                        "description": "Retrieve top-k reference chunks for the given query. Searches skill references (.claude/skills/*/references/) and, when 'project' is in the skills filter, indexed project source files. v1 contract: one call per agent per task.",
                        "inputSchema": {
                            "type": "object",
                            "properties": {
                                "query": {"type": "string"},
                                "k": {"type": "integer", "default": 6},
                                "skills": {
                                    "type": "array",
                                    "items": {"type": "string"},
                                    "description": "Optional skill-name filter; defaults to enabled_skills. Pass the task's skills_to_load.",
                                },
                            },
                            "required": ["query"],
                        },
                    },
                    {
                        "name": "get_symbol",
                        "description": "Fetch the exact definition(s) of a named function/class/method — signature + full body + file:line — instead of Reading a whole file to see one symbol. Use AFTER you know the name (e.g. from retrieve or a file list). Pass 'file' to scope to one file; omit it to search indexed project source. Returns [] if not found. Best-effort structural extraction (Python + brace languages).",
                        "inputSchema": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string", "description": "Symbol name (function/class/method)."},
                                "file": {"type": "string", "description": "Optional repo-relative file to scope the lookup to."},
                                "max": {"type": "integer", "default": 5, "description": "Max matches to return when searching the project."},
                            },
                            "required": ["name"],
                        },
                    },
                ]
            })
        elif method == "tools/call":
            params = req.get("params") or {}
            tool_name = params.get("name")
            args = params.get("arguments") or {}

            if tool_name == "get_symbol":
                # Structural fetch — deterministic and cheap, so it is NOT
                # charged against the retrieve budget.
                sym = (args.get("name") or "").strip()
                if not sym:
                    reply(req_id, {"content": [{"type": "text", "text": json.dumps(
                        [{"error": "get_symbol requires a non-empty 'name'"}])}]})
                    continue
                try:
                    max_matches = max(1, int(args.get("max", 5)))
                except (TypeError, ValueError):
                    max_matches = 5
                matches = call_symbols(sym, args.get("file") or None, max_matches)
                reply(req_id, {"content": [{"type": "text", "text": json.dumps(
                    matches, ensure_ascii=False, indent=2)}]})
                continue

            if tool_name != "retrieve":
                reply(req_id, error={"code": -32601, "message": f"unknown tool: {tool_name}"})
                continue
            query = args.get("query") or ""
            try:
                k = int(args.get("k", 6))
            except (TypeError, ValueError):
                k = 6  # a malformed k must not crash the long-lived server
            skills = args.get("skills") or None

            # Enforce the per-task retrieve budget — but only when a task
            # context is known (ambient calls with no current_task.txt are not
            # counted, so exploratory use outside a dispatch is unaffected).
            key = _task_key()
            if key[1] is not None:
                _retrieve_counts[key] = _retrieve_counts.get(key, 0) + 1
                if _retrieve_counts[key] > MAX_RETRIEVE_PER_TASK:
                    reply(req_id, {
                        "content": [{"type": "text", "text": json.dumps([{
                            "error": (
                                f"retrieve budget exceeded: max {MAX_RETRIEVE_PER_TASK} "
                                f"calls per task. Stop retrieving and flip status to "
                                f"blocked with blocker: \"insufficient_context\"."
                            )
                        }], ensure_ascii=False, indent=2)}]
                    })
                    continue

            chunks = call_retrieve(query, k, skills)
            reply(req_id, {
                "content": [
                    {"type": "text", "text": json.dumps(chunks, ensure_ascii=False, indent=2)}
                ]
            })
        elif method == "notifications/initialized":
            # no reply required for notifications
            continue
        elif req_id is not None:
            reply(req_id, error={"code": -32601, "message": f"unknown method: {method}"})


if __name__ == "__main__":
    _serve()
