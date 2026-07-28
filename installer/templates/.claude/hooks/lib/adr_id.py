#!/usr/bin/env python3
"""Allocate the next ADR id: `ADR-NNN` (zero-padded).

Scans `.claude/adrs/ADR-*.md` for the highest existing N, returns N+1. The scan
and format logic is shared with plan_id via id_alloc; only the directory and the
filename shape differ.

Usage:
    python adr_id.py next       # prints ADR-003
    python adr_id.py peek       # prints the most recent existing id, or empty
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from id_alloc import cli, existing, next_id, peek_id  # noqa: E402

REPO_ROOT = Path.cwd().resolve()
ADRS_DIR = REPO_ROOT / ".claude" / "adrs"

PREFIX = "ADR"
PAD = 3
START = 1
# ADRs are `.md` files and carry a slug after the number (ADR-002-use-utc.md).
SHAPE = {"files": True, "slug": True}


def existing_ids() -> list[tuple[int, str]]:
    return existing(ADRS_DIR, PREFIX, **SHAPE)


def cmd_next() -> str:
    return next_id(ADRS_DIR, PREFIX, PAD, START, **SHAPE)


def cmd_peek() -> str:
    return peek_id(ADRS_DIR, PREFIX, **SHAPE)


def main() -> None:
    cli(cmd_next, cmd_peek)


if __name__ == "__main__":
    main()
