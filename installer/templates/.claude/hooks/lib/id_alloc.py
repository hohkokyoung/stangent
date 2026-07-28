#!/usr/bin/env python3
"""Shared sequential-id allocation for `PREFIX-NNN` artifacts.

`plan_id` (run directories) and `adr_id` (ADR files) were the same algorithm
written twice: scan a directory, match `PREFIX-<n>`, take the highest, add one,
zero-pad. They differ only in what they scan and whether a `-slug` may follow
the number — two parameters, not two implementations.

The id decides the directory every artifact of a run is filed under, so an
allocator that disagrees with itself does not produce a cosmetic problem: it
collides with a directory that already exists.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path


def existing(directory: Path, prefix: str, *, files: bool,
             slug: bool) -> list[tuple[int, str]]:
    """Ascending `(number, display_name)` for entries named `<prefix>-<n>`.

    `files=False` scans directories (a run dir, `FEAT-003`); `files=True` scans
    `.md` files and reports the stem (`ADR-002-use-utc`). `slug=True` allows a
    trailing `-slug`/`_slug` after the number.

    A directory named like a file's id — or vice versa — is deliberately not a
    match: `ADR-002-dir/` is not an ADR, and treating it as one would let a
    stray directory push every future id up by one.
    """
    if not directory.exists():
        return []
    tail = r"(?:[-_].*)?" if slug else ""
    ext = r"\.md" if files else ""
    pat = re.compile(rf"^{re.escape(prefix)}-(\d+){tail}{ext}$")
    out: list[tuple[int, str]] = []
    for p in directory.iterdir():
        if p.is_file() != files:
            continue
        m = pat.match(p.name)
        if m:
            out.append((int(m.group(1)), p.stem if files else p.name))
    out.sort()
    return out


def fmt(n: int, prefix: str, pad: int) -> str:
    return f"{prefix}-{n:0{pad}d}"


def next_id(directory: Path, prefix: str, pad: int, start: int, **kw) -> str:
    """The next free id. Takes max+1 rather than counting entries: with a gap
    (001, 003) a count would mint 003 and collide the moment it is created."""
    ex = existing(directory, prefix, **kw)
    return fmt((ex[-1][0] + 1) if ex else start, prefix, pad)


def peek_id(directory: Path, prefix: str, **kw) -> str:
    """The most recent existing id, or "" when there are none."""
    ex = existing(directory, prefix, **kw)
    return ex[-1][1] if ex else ""


def cli(next_fn, peek_fn, argv: list[str] | None = None) -> None:
    """`next` / `peek` (alias `current`) — the CLI both allocators expose."""
    args = sys.argv[1:] if argv is None else argv
    cmd = args[0] if args else "next"
    if cmd == "next":
        print(next_fn())
    elif cmd in ("peek", "current"):
        print(peek_fn())
    else:
        sys.stderr.write(f"unknown subcommand: {cmd}\n")
        sys.exit(1)
