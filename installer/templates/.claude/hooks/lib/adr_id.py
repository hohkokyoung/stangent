#!/usr/bin/env python3
"""Allocate the next ADR id: `ADR-NNN` (zero-padded).

Scans `.claude/adrs/ADR-*.md` for the highest existing N, returns N+1.

Usage:
    python adr_id.py next       # prints ADR-003
    python adr_id.py peek       # prints the most recent existing id, or empty
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()
ADRS_DIR = REPO_ROOT / ".claude" / "adrs"

PREFIX = "ADR"
PAD = 3
START = 1
PAT = re.compile(rf"^{PREFIX}-(\d+)(?:[-_].*)?\.md$")


def existing() -> list[tuple[int, str]]:
    if not ADRS_DIR.exists():
        return []
    out: list[tuple[int, str]] = []
    for p in ADRS_DIR.iterdir():
        if not p.is_file():
            continue
        m = PAT.match(p.name)
        if m:
            out.append((int(m.group(1)), p.stem))
    out.sort()
    return out


def fmt(n: int) -> str:
    return f"{PREFIX}-{n:0{PAD}d}"


def cmd_next() -> str:
    ex = existing()
    n = (ex[-1][0] + 1) if ex else START
    return fmt(n)


def cmd_peek() -> str:
    ex = existing()
    return ex[-1][1] if ex else ""


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "next"
    if cmd == "next":
        print(cmd_next())
    elif cmd in ("peek", "current"):
        print(cmd_peek())
    else:
        sys.stderr.write(f"unknown subcommand: {cmd}\n")
        sys.exit(1)


if __name__ == "__main__":
    main()
