#!/usr/bin/env python3
"""Allocate the next plan id, format `<PREFIX>-<N>` (zero-padded).

Reads `.claude/.agentic.yml: plan_id.{prefix,pad,start}`, scans
`.claude/state/plans/<prefix>-*` for the max existing N, returns N+1.

Usage:
    python plan_id.py next       # prints e.g. FEAT-003
    python plan_id.py peek       # prints the most recent existing id, or empty
    python plan_id.py current    # alias for peek (used by /agentic-build default)
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:
    yaml = None  # type: ignore

REPO_ROOT = Path.cwd().resolve()
AGENTIC_YML = REPO_ROOT / ".claude" / ".agentic.yml"
PLANS_DIR = REPO_ROOT / ".claude" / "state" / "plans"


def load_cfg() -> dict:
    defaults = {"prefix": "FEAT", "pad": 3, "start": 1}
    if not AGENTIC_YML.exists() or yaml is None:
        return defaults
    try:
        full = yaml.safe_load(AGENTIC_YML.read_text(encoding="utf-8")) or {}
    except Exception:
        return defaults
    pid = (full.get("plan_id") or {})
    return {**defaults, **{k: pid[k] for k in ("prefix", "pad", "start") if k in pid}}


def existing_ids(prefix: str) -> list[tuple[int, str]]:
    if not PLANS_DIR.exists():
        return []
    pat = re.compile(rf"^{re.escape(prefix)}-(\d+)$")
    out: list[tuple[int, str]] = []
    for p in PLANS_DIR.iterdir():
        if not p.is_dir():
            continue
        m = pat.match(p.name)
        if m:
            out.append((int(m.group(1)), p.name))
    out.sort()
    return out


def fmt(n: int, prefix: str, pad: int) -> str:
    return f"{prefix}-{n:0{pad}d}"


def cmd_next() -> str:
    cfg = load_cfg()
    existing = existing_ids(cfg["prefix"])
    n = (existing[-1][0] + 1) if existing else cfg["start"]
    return fmt(n, cfg["prefix"], cfg["pad"])


def cmd_peek() -> str:
    cfg = load_cfg()
    existing = existing_ids(cfg["prefix"])
    return existing[-1][1] if existing else ""


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
