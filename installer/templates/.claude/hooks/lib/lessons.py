#!/usr/bin/env python3
"""Cross-run learning: distilled review findings that persist across runs.

Reviewer findings otherwise die with their run. This module is the mechanical
half of the loop:

    lessons.py collect            # scrape every run's ## Review section → JSON
    lessons.py add "<lesson>"     # append one distilled lesson (dedup + cap)
    lessons.py show               # print lessons.md (for planner injection)

The *distillation* (deciding which raw findings are recurring and worth keeping)
is LLM judgment done by /agentic-lessons — this script only collects the raw
material and manages the capped, deduped lessons file. /agentic-plan feeds
`show` output to the planner so future plans account for past mistakes.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()
PLANS_DIR = REPO_ROOT / ".claude" / "state" / "plans"
LESSONS_FILE = REPO_ROOT / ".claude" / "state" / "lessons.md"

MAX_LESSONS = 30
HEADER = (
    "# Lessons — recurring review findings\n\n"
    "<!-- Distilled by /agentic-lessons from past ## Review sections; injected\n"
    "     into the planner by /agentic-plan. Capped at "
    f"{MAX_LESSONS} entries (oldest dropped). -->\n\n"
)


def _extract_section(text: str, header: str) -> str:
    """Return the body of a `## <header>` section, empty string if absent/blank."""
    lines = text.splitlines()
    start = None
    for i, ln in enumerate(lines):
        if ln.strip() == f"## {header}":
            start = i + 1
            break
    if start is None:
        return ""
    body = []
    for ln in lines[start:]:
        if ln.startswith("## "):
            break
        body.append(ln)
    # Drop template placeholder comments and blank padding.
    kept = [ln for ln in body if ln.strip() and not ln.strip().startswith("<!--")]
    return "\n".join(kept).strip()


def collect() -> list[dict]:
    out = []
    if not PLANS_DIR.is_dir():
        return out
    for run_dir in sorted(PLANS_DIR.iterdir()):
        if not run_dir.is_dir():
            continue
        for f in sorted(run_dir.glob("*.md")):
            if f.name == "_overview.md":
                continue
            review = _extract_section(f.read_text(encoding="utf-8"), "Review")
            if review:
                out.append({"run_id": run_dir.name, "task_id": f.stem, "review": review})
    return out


def _read_lessons() -> list[str]:
    if not LESSONS_FILE.exists():
        return []
    return [ln[2:].strip() for ln in LESSONS_FILE.read_text(encoding="utf-8").splitlines()
            if ln.startswith("- ") and ln[2:].strip()]


def _write_lessons(items: list[str]) -> None:
    LESSONS_FILE.parent.mkdir(parents=True, exist_ok=True)
    body = HEADER + "".join(f"- {it}\n" for it in items)
    LESSONS_FILE.write_text(body, encoding="utf-8")


def add(text: str) -> bool:
    """Append a lesson. Returns False if it duplicates an existing one."""
    text = " ".join(text.split()).strip()
    if not text:
        return False
    items = _read_lessons()
    if any(text.lower() == it.lower() for it in items):
        return False
    items.append(text)
    if len(items) > MAX_LESSONS:
        items = items[-MAX_LESSONS:]  # keep the newest
    _write_lessons(items)
    return True


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("collect")
    a = sub.add_parser("add")
    a.add_argument("text")
    sub.add_parser("show")
    args = ap.parse_args()

    if args.cmd == "collect":
        print(json.dumps(collect(), ensure_ascii=False, indent=2))
    elif args.cmd == "add":
        ok = add(args.text)
        print("added" if ok else "skipped (duplicate or empty)")
    elif args.cmd == "show":
        if LESSONS_FILE.exists():
            sys.stdout.write(LESSONS_FILE.read_text(encoding="utf-8"))
    sys.exit(0)


if __name__ == "__main__":
    main()
