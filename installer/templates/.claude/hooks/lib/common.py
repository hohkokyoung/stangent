#!/usr/bin/env python3
"""Tiny shared helpers for the hooks and log tools.

Deliberately minimal and pure-stdlib: the hooks that import this fire on every
tool call, so this file must stay trivially correct and dependency-free.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

try:
    import yaml  # type: ignore
except ImportError:  # documented, supported state — every caller falls back
    yaml = None  # type: ignore


def read_agentic_config(root, tag: str, ignored: str = "") -> dict:
    """Parse `<root>/.claude/.agentic.yml`, or return {} if it cannot be read.

    Six modules had their own copy of this, each with its own `yaml = None`
    dance, its own `AGENTIC_YML` constant, and its own wording for the same
    warning — one of which (retriever) warned even when there was no config to
    ignore. Behaviour that is written six times drifts six ways.

    `root` is taken per call rather than frozen into a module constant. Every
    caller derives its root at import, so a constant computed from it went stale
    the moment anything rebound the root — which is exactly what the tests do,
    and why `test_git_branch` had to patch `AGENTIC_YML` separately to be
    correct. Resolving here removes that whole class.

    Silence when there is nothing to ignore, and a warning when there is: a
    config that exists but cannot be parsed means every setting in it is being
    dropped — a deliberate `checkpoint_commits: false`, a per-role model — and
    the setting looks honoured while it is not. `ignored` names what the caller
    loses, since "config ignored" is not actionable on its own.
    """
    path = Path(root) / ".claude" / ".agentic.yml"
    if yaml is None:
        if path.exists():
            detail = f" — {ignored}" if ignored else ""
            sys.stderr.write(
                f"[{tag}] PyYAML not installed; .agentic.yml is being "
                f"IGNORED{detail}\n")
        return {}
    if not path.exists():
        return {}
    try:
        return yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    except Exception:
        return {}


def config_section(cfg: dict, name: str, defaults: dict) -> dict:
    """`defaults` overlaid with the keys `cfg[name]` actually defines.

    Only keys present in `defaults` are taken, so an unknown key in a project's
    config cannot inject a setting the caller never declared.
    """
    block = cfg.get(name) or {}
    if not isinstance(block, dict):
        return dict(defaults)
    return {**defaults, **{k: block[k] for k in defaults if k in block}}


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


def read_state(state_dir, name: str) -> str | None:
    """A `current_*.txt` dispatch-state value, or None if absent/empty."""
    return read_text_or_none(Path(state_dir) / name)


def note_hook_error(log_dir, state_dir, hook_name: str, exc: Exception) -> None:
    """Record that a hook failed, without letting the failure escape.

    `except Exception: pass` keeps the contract — telemetry must never break a
    run — but it also makes a broken hook indistinguishable from an idle one. A
    run whose usage events silently stopped looks exactly like a run that
    produced none, so the cost table quietly under-reports and nothing says why.

    Best-effort by necessity: if the log is what failed, there is nowhere to
    write, and the caller still swallows.
    """
    import datetime as dt
    try:
        run_id = read_state(state_dir, "current_run.txt")
        if not run_id:
            return
        log_dir = Path(log_dir)
        log_dir.mkdir(parents=True, exist_ok=True)
        with (log_dir / f"{run_id}.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps({
                "ts": dt.datetime.now(dt.timezone.utc).isoformat(
                    timespec="seconds").replace("+00:00", "Z"),
                "event": "hook_error",
                "hook": hook_name,
                "error": f"{type(exc).__name__}: {exc}"[:300],
            }, ensure_ascii=False) + "\n")
    except Exception:
        pass


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
