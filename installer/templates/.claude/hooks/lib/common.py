#!/usr/bin/env python3
"""Tiny shared helpers for the hooks and log tools.

Deliberately minimal and pure-stdlib: the hooks that import this fire on every
tool call, so this file must stay trivially correct and dependency-free.
"""
from __future__ import annotations

import json
from pathlib import Path


def read_text_or_none(path) -> str | None:
    """Stripped file contents, or None if missing/empty/unreadable."""
    try:
        return Path(path).read_text(encoding="utf-8").strip() or None
    except OSError:
        return None


def extract_section(text: str, heading: str) -> str | None:
    """Body under a level-2 `## <heading>`, or None if the section is absent.

    Runs to the next `## ` (deeper `###` sub-headings are kept), matches the
    heading case-insensitively, and strips surrounding blank lines. Callers
    wanting an empty string for "absent" use `or ""`.

    One implementation on purpose: this was written three times with quietly
    different semantics — one case-sensitive, one returning "" instead of None,
    one stopping at any heading depth — which is the kind of divergence nobody
    notices until a section silently fails to match.
    """
    target = heading.strip().lower()
    body: list[str] = []
    capturing = False
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("## ") and not stripped.startswith("###"):
            if capturing:
                break
            if stripped[3:].strip().lower() == target:
                capturing = True
            continue
        if capturing:
            body.append(line)
    if not capturing:
        return None
    return "\n".join(body).strip() or None


def last_logged_context(log_path) -> dict:
    """`task_id` / `agent_role` from a run log's most recent tool-call line.

    SubagentStop can fire after the command has cleared its per-task state, so a
    hook reading those files gets nothing and the event lands unattributed. The
    subagent that just finished is the one that wrote the last tool-call line, so
    its context is the correct fallback and is already on disk.
    """
    for r in reversed(read_jsonl(log_path)):
        if r.get("event") or not r.get("tool"):
            continue
        return {"task_id": r.get("task_id"), "agent_role": r.get("agent_role")}
    return {}


def read_jsonl(path) -> list[dict]:
    """Parse a .jsonl file into a list of dicts, skipping malformed lines.
    Streams line-by-line so large transcripts don't load whole into memory."""
    out: list[dict] = []
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line:
                    try:
                        out.append(json.loads(line))
                    except json.JSONDecodeError:
                        pass
    except OSError:
        pass
    return out
