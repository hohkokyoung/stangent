#!/usr/bin/env python3
"""agentic_mcp — MCP server exposing the single tool: retrieve(query, k).

This is the v1 internal retrieval layer. It is the ONE exception to the
"MCP = external systems" rule; it lives here because Claude Code consumes
it as MCP. It must NOT be used for planning or task decomposition.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()
RETRIEVER = REPO_ROOT / ".claude" / "hooks" / "lib" / "retriever.py"


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
        cmd = [sys.executable, str(RETRIEVER), "query", query, str(k)]
        for s in skills or []:
            cmd += ["--skill", s]
        try:
            out = subprocess.check_output(cmd, cwd=str(REPO_ROOT), text=True)
            return json.loads(out.strip().splitlines()[-1])
        except subprocess.CalledProcessError as e:
            return [{"error": f"retriever failed: {e}"}]
        except Exception as e:
            return [{"error": str(e)}]

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
                        "description": "Retrieve top-k reference chunks for the given query. Internal knowledge only (sqlite-vec over .claude/skills/*/references). v1 contract: one call per agent per task.",
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
                    }
                ]
            })
        elif method == "tools/call":
            params = req.get("params") or {}
            name = params.get("name")
            args = params.get("arguments") or {}
            if name != "retrieve":
                reply(req_id, error={"code": -32601, "message": f"unknown tool: {name}"})
                continue
            query = args.get("query") or ""
            k = int(args.get("k", 6))
            skills = args.get("skills") or None
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
