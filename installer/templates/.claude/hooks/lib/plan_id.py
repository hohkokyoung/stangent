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

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import read_agentic_config, config_section  # noqa: E402
from id_alloc import cli, existing, next_id, peek_id  # noqa: E402

REPO_ROOT = Path.cwd().resolve()
PLANS_DIR = REPO_ROOT / ".claude" / "state" / "plans"


def load_cfg() -> dict:
    defaults = {"prefix": "FEAT", "pad": 3, "start": 1}
    # A configured `prefix` or `pad` silently reverting to FEAT-### mints ids
    # that do not match the ones already on disk.
    cfg = read_agentic_config(REPO_ROOT, "plan_id",
                              "`plan_id:` settings are ignored and defaults used")
    return config_section(cfg, "plan_id", defaults)


# Runs are directories named exactly `FEAT-003` — no slug, and a *file* named
# FEAT-999.md is not a run.
SHAPE = {"files": False, "slug": False}


def existing_ids(prefix: str) -> list[tuple[int, str]]:
    return existing(PLANS_DIR, prefix, **SHAPE)


def cmd_next() -> str:
    cfg = load_cfg()
    return next_id(PLANS_DIR, cfg["prefix"], cfg["pad"], cfg["start"], **SHAPE)


def cmd_peek() -> str:
    return peek_id(PLANS_DIR, load_cfg()["prefix"], **SHAPE)


def main() -> None:
    cli(cmd_next, cmd_peek)


if __name__ == "__main__":
    main()
