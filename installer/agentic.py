#!/usr/bin/env python3
"""Agentic Development Workflow System — installer.

Single-file, cross-platform. Copies the templated `.claude/` tree into the
target project, manages the `# >>> agentic` block in `.gitignore`, and is
fully idempotent. Strips only `_agentic_managed`-marked entries on
`--uninstall`.

Usage (from any directory):
    python <repo>/installer/agentic.py              # install into $PWD
    python <repo>/installer/agentic.py --target <p> # install into <p>
    python <repo>/installer/agentic.py --uninstall  # remove managed entries
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
from pathlib import Path

# Windows consoles default to cp1252 and choke on unicode glyphs in print().
try:
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

SCRIPT_DIR = Path(__file__).resolve().parent
TEMPLATES_DIR = SCRIPT_DIR / "templates"

GITIGNORE_BLOCK = """# >>> agentic
.claude/state/vectors.db
.claude/state/logs/
.mcp.json
# <<< agentic
"""
GITIGNORE_RE = re.compile(r"# >>> agentic.*?# <<< agentic\r?\n?", re.DOTALL)

SYSTEM_OWNED = ["agents", "commands", "skills", "hooks", "mcp", "evals", "templates", "state", ".agentic.yml"]
# Note: adrs/ is intentionally NOT in SYSTEM_OWNED. ADRs are user-authored
# project decisions; uninstall preserves them. Delete the dir manually if you
# really want them gone.


def info(msg: str) -> None:
    print(f"[agentic] {msg}")


# ---------- install ----------

def copy_templates(target: Path) -> None:
    """Install templates with mirror semantics for system-owned paths.

    System-owned directories (agents/, commands/, skills/, hooks/, mcp/) are
    fully replaced — any stale files removed in a newer template version
    disappear here too. The single-file system-owned items (.agentic.yml,
    settings.json) are overwritten in place.

    Everything outside the system-owned set is untouched, so state/ and any
    user-added subdirs survive a re-install.
    """
    src = TEMPLATES_DIR / ".claude"
    if not src.exists():
        raise SystemExit(f"templates not found at {src}")
    dst = target / ".claude"
    dst.mkdir(parents=True, exist_ok=True)

    mirror_dirs = {"agents", "commands", "skills", "hooks", "mcp", "evals", "templates"}
    overwrite_files = {".agentic.yml", "settings.json"}
    seed_dirs = {"adrs"}  # copied only on first install; user-managed thereafter

    for name in mirror_dirs:
        src_d = src / name
        if not src_d.exists():
            continue
        dst_d = dst / name
        if dst_d.exists():
            shutil.rmtree(dst_d)
        shutil.copytree(src_d, dst_d)

    for name in overwrite_files:
        src_f = src / name
        if src_f.exists():
            shutil.copy2(src_f, dst / name)

    for name in seed_dirs:
        src_d = src / name
        dst_d = dst / name
        if not src_d.exists():
            continue
        if dst_d.exists():
            info(f"seed dir {name}/ already present — leaving as-is")
            continue
        shutil.copytree(src_d, dst_d)

    # .mcp.json — Claude Desktop reads this at project root (not under .claude/).
    # Seeded only on first install so user-added credentials survive re-install.
    mcp_src = TEMPLATES_DIR / ".mcp.json"
    mcp_dst = target / ".mcp.json"
    if mcp_src.exists():
        if mcp_dst.exists():
            info(".mcp.json already present at project root — leaving as-is")
        else:
            shutil.copy2(mcp_src, mcp_dst)
            info("seeded .mcp.json at project root — fill in DSN + PAT to enable dbhub/supabase")

    info(f"copied templates to {dst}")


def add_gitignore_block(target: Path) -> None:
    gi = target / ".gitignore"
    if gi.exists():
        content = gi.read_text(encoding="utf-8")
        if "# >>> agentic" in content:
            info(".gitignore already has agentic block")
            return
        sep = "" if content.endswith("\n") else "\n"
        gi.write_text(content + sep + "\n" + GITIGNORE_BLOCK, encoding="utf-8")
    else:
        gi.write_text(GITIGNORE_BLOCK, encoding="utf-8")
    info("wrote .gitignore agentic block")


def install(target: Path) -> None:
    target.mkdir(parents=True, exist_ok=True)
    copy_templates(target)
    add_gitignore_block(target)
    info(f"install complete at {target}")
    info("next: pip install pyyaml fastembed sqlite-vec")
    info("then in Claude Code: /agentic-index  ->  /agentic-plan <goal>  ->  /agentic-build all")


# ---------- uninstall ----------

def strip_managed_settings(target: Path) -> None:
    p = target / ".claude" / "settings.json"
    if not p.exists():
        return
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        info(f"warning: could not parse {p}; leaving untouched")
        return

    hooks = data.get("hooks", {})
    for ev in list(hooks.keys()):
        kept = []
        for entry in hooks[ev]:
            new_hooks = [h for h in entry.get("hooks", []) if not h.get("_agentic_managed")]
            if new_hooks:
                entry["hooks"] = new_hooks
                kept.append(entry)
        if kept:
            hooks[ev] = kept
        else:
            del hooks[ev]
    if not hooks:
        data.pop("hooks", None)

    mcp = data.get("mcpServers", {})
    for name in list(mcp.keys()):
        if mcp[name].get("_agentic_managed"):
            del mcp[name]
    if not mcp:
        data.pop("mcpServers", None)

    if not data:
        p.unlink()
        info("removed empty settings.json")
    else:
        p.write_text(json.dumps(data, indent=2), encoding="utf-8")
        info("stripped managed entries from settings.json")


def remove_mcp_json_if_template(target: Path) -> None:
    """Remove .mcp.json only if it still looks like the unedited template
    (contains the REPLACE_WITH_... placeholders). If the user has filled
    in real credentials, we leave it alone."""
    p = target / ".mcp.json"
    if not p.exists():
        return
    content = p.read_text(encoding="utf-8")
    if "REPLACE_WITH_" in content:
        p.unlink()
        info("removed .mcp.json (was unedited template)")
    else:
        info(".mcp.json kept — contains real credentials. Delete manually if you want it gone.")


def remove_system_dirs(target: Path) -> None:
    claude = target / ".claude"
    if not claude.exists():
        return
    for name in SYSTEM_OWNED:
        p = claude / name
        if p.is_file():
            p.unlink()
            info(f"removed {name}")
        elif p.is_dir():
            shutil.rmtree(p)
            info(f"removed {name}/")
    if not any(claude.iterdir()):
        claude.rmdir()
        info("removed empty .claude/")


def remove_gitignore_block(target: Path) -> None:
    gi = target / ".gitignore"
    if not gi.exists():
        return
    new = GITIGNORE_RE.sub("", gi.read_text(encoding="utf-8"))
    if new.strip() == "":
        gi.unlink()
        info("removed empty .gitignore")
    else:
        gi.write_text(new, encoding="utf-8")
        info("stripped agentic block from .gitignore")


def uninstall(target: Path) -> None:
    strip_managed_settings(target)
    remove_system_dirs(target)
    remove_mcp_json_if_template(target)
    remove_gitignore_block(target)
    info(f"uninstall complete at {target}")


# ---------- cli ----------

def main() -> None:
    ap = argparse.ArgumentParser(description="Agentic Development Workflow installer.")
    ap.add_argument("--target", default=os.getcwd(), help="Project directory (default: cwd).")
    ap.add_argument("--uninstall", action="store_true", help="Remove managed entries.")
    args = ap.parse_args()

    target = Path(args.target).resolve()
    (uninstall if args.uninstall else install)(target)


if __name__ == "__main__":
    main()
