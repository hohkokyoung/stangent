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
    python <repo>/installer/agentic.py --upgrade-config  # merge new template defaults into existing config
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
    disappear here too.

    Seed files (.agentic.yml, settings.json, adrs/) are copied only on first
    install. On re-install they are left untouched — they are user config, not
    system code. The user is expected to update them manually when upgrading.

    Everything outside the system-owned set is untouched, so state/ and any
    user-added subdirs survive a re-install.
    """
    src = TEMPLATES_DIR / ".claude"
    if not src.exists():
        raise SystemExit(f"templates not found at {src}")
    dst = target / ".claude"
    dst.mkdir(parents=True, exist_ok=True)

    mirror_dirs = {"agents", "commands", "skills", "hooks", "mcp", "evals", "templates"}
    # Seed files: copied on first install only. These are user config — never
    # overwritten so project-specific settings (enabled_skills, MCP servers,
    # hook list) survive a re-install / upgrade.
    seed_files = {".agentic.yml", "settings.json"}
    seed_dirs = {"adrs"}  # copied only on first install; user-managed thereafter

    for name in mirror_dirs:
        src_d = src / name
        if not src_d.exists():
            continue
        dst_d = dst / name
        if dst_d.exists():
            shutil.rmtree(dst_d)
        shutil.copytree(src_d, dst_d)

    for name in seed_files:
        src_f = src / name
        dst_f = dst / name
        if not src_f.exists():
            continue
        if dst_f.exists():
            info(f"{name} already present — leaving as-is (edit manually to pick up new defaults)")
        else:
            shutil.copy2(src_f, dst_f)
            info(f"seeded {name}")

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


# ---------- upgrade-config ----------

def _upgrade_settings_json(target: Path) -> None:
    """Add new enabledMcpjsonServers entries from template; leave existing entries alone."""
    tpl_path = TEMPLATES_DIR / ".claude" / "settings.json"
    dst_path = target / ".claude" / "settings.json"
    if not tpl_path.exists() or not dst_path.exists():
        return
    try:
        tpl = json.loads(tpl_path.read_text(encoding="utf-8"))
        dst = json.loads(dst_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        info(f"warning: could not parse settings.json: {e} — skipping")
        return

    changed = False

    tpl_servers = tpl.get("enabledMcpjsonServers", [])
    dst_servers = dst.get("enabledMcpjsonServers", [])
    new_servers = [s for s in tpl_servers if s not in dst_servers]
    if new_servers:
        dst["enabledMcpjsonServers"] = dst_servers + new_servers
        changed = True
        info(f"settings.json: added to enabledMcpjsonServers: {new_servers}")

    if changed:
        dst_path.write_text(json.dumps(dst, indent=2) + "\n", encoding="utf-8")
    else:
        info("settings.json: already up to date")


def _upgrade_mcp_json(target: Path) -> None:
    """Add new mcpServers entries from template; leave existing entries alone."""
    tpl_path = TEMPLATES_DIR / ".mcp.json"
    dst_path = target / ".mcp.json"
    if not tpl_path.exists() or not dst_path.exists():
        return
    try:
        tpl = json.loads(tpl_path.read_text(encoding="utf-8"))
        dst = json.loads(dst_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        info(f"warning: could not parse .mcp.json: {e} — skipping")
        return

    tpl_servers = tpl.get("mcpServers", {})
    dst_servers = dst.get("mcpServers", {})

    added = []
    for name, config in tpl_servers.items():
        # Skip JSON comment/divider entries (string values, not server objects).
        if not isinstance(config, dict):
            continue
        if name not in dst_servers:
            dst_servers[name] = config
            added.append(name)

    if added:
        dst["mcpServers"] = dst_servers
        # Refresh the top-level comment so it stays accurate.
        if "_comment" in tpl:
            dst["_comment"] = tpl["_comment"]
        dst_path.write_text(json.dumps(dst, indent=2) + "\n", encoding="utf-8")
        info(f".mcp.json: added servers: {added}")
    else:
        info(".mcp.json: already up to date")


def _upgrade_agentic_yml(target: Path) -> None:
    """Replace only the available-skills comment block in .agentic.yml.

    Everything else (enabled_skills, embedding, gateway, etc.) is untouched
    because those are user configuration, not documentation.
    """
    tpl_path = TEMPLATES_DIR / ".claude" / ".agentic.yml"
    dst_path = target / ".claude" / ".agentic.yml"
    if not tpl_path.exists() or not dst_path.exists():
        return

    # Match from any comment line containing "Available built-in skills:" through
    # all subsequent comment lines, stopping just before "enabled_skills:".
    block_re = re.compile(r"(#[^\n]*Available built-in skills:[^\n]*\n(?:#[^\n]*\n)*)(?=enabled_skills:)", re.DOTALL)

    tpl_text = tpl_path.read_text(encoding="utf-8")
    dst_text = dst_path.read_text(encoding="utf-8")

    tpl_m = block_re.search(tpl_text)
    dst_m = block_re.search(dst_text)

    if not tpl_m:
        return
    if not dst_m:
        info(".agentic.yml: skills comment block not found (file may be customized) — skipping; add rest-openapi to the backend list manually if needed")
    elif tpl_m.group(1) == dst_m.group(1):
        info(".agentic.yml: skills block already up to date")
    else:
        dst_text = dst_text[: dst_m.start(1)] + tpl_m.group(1) + dst_text[dst_m.end(1):]
        dst_path.write_text(dst_text, encoding="utf-8")
        info(".agentic.yml: updated available-skills comment block")

    # Append the models: section if it doesn't exist yet (independent of the skills block).
    dst_text = dst_path.read_text(encoding="utf-8")
    if "models:" not in dst_text:
        models_block_re = re.compile(r"^models:.*?(?=^\w|\Z)", re.DOTALL | re.MULTILINE)
        tpl_m2 = models_block_re.search(tpl_text)
        if tpl_m2:
            dst_path.write_text(dst_text.rstrip() + "\n\n" + tpl_m2.group(0).rstrip() + "\n", encoding="utf-8")
            info(".agentic.yml: added models: section")


def upgrade_config(target: Path) -> None:
    _upgrade_settings_json(target)
    _upgrade_mcp_json(target)
    _upgrade_agentic_yml(target)
    info(f"upgrade-config complete at {target}")


# ---------- cli ----------

def main() -> None:
    ap = argparse.ArgumentParser(description="Agentic Development Workflow installer.")
    ap.add_argument("--target", default=os.getcwd(), help="Project directory (default: cwd).")
    ap.add_argument("--uninstall", action="store_true", help="Remove managed entries.")
    ap.add_argument(
        "--upgrade-config",
        action="store_true",
        help="Merge new template defaults into existing config files "
             "(settings.json, .mcp.json, .agentic.yml). "
             "Adds new keys; never overwrites existing values.",
    )
    args = ap.parse_args()

    target = Path(args.target).resolve()
    if args.uninstall:
        uninstall(target)
    elif getattr(args, "upgrade_config"):
        upgrade_config(target)
    else:
        install(target)


if __name__ == "__main__":
    main()
