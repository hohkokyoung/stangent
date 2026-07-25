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
