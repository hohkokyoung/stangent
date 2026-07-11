#!/usr/bin/env python3
"""/agentic-doctor — install health diagnostic.

Runs a battery of fast, deterministic checks against the current project's
agentic install. Exits non-zero if any check fails.

Usage:
    python .claude/hooks/lib/doctor.py
    python .claude/hooks/lib/doctor.py --json     # machine-readable
"""
from __future__ import annotations

import argparse
import importlib
import json
import os
import sqlite3
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()
CLAUDE = REPO_ROOT / ".claude"

OK, WARN, FAIL = "ok", "warn", "fail"
ICON = {OK: "[ok]  ", WARN: "[warn]", FAIL: "[FAIL]"}


def _check(name: str, status: str, detail: str = "") -> dict:
    return {"name": name, "status": status, "detail": detail}


# ────────────────── individual checks ──────────────────

def check_python_version() -> dict:
    v = sys.version_info
    if v.major == 3 and v.minor >= 10:
        return _check("python >= 3.10", OK, f"{v.major}.{v.minor}.{v.micro}")
    return _check("python >= 3.10", FAIL, f"found {v.major}.{v.minor}; need 3.10+")


def check_required_deps() -> list[dict]:
    out = []
    for mod in ("yaml", "fastembed", "sqlite_vec"):
        try:
            importlib.import_module(mod)
            out.append(_check(f"dep: {mod}", OK))
        except Exception as e:
            pkg = {"yaml": "pyyaml", "sqlite_vec": "sqlite-vec"}.get(mod, mod)
            out.append(_check(f"dep: {mod}", FAIL, f"missing — pip install {pkg}"))
    return out


def check_optional_voyage() -> dict:
    try:
        importlib.import_module("voyageai")
        if os.environ.get("VOYAGE_API_KEY"):
            return _check("voyage embedder", OK, "voyageai installed + VOYAGE_API_KEY set")
        return _check("voyage embedder", WARN, "voyageai installed but VOYAGE_API_KEY unset; falls back to fastembed")
    except Exception:
        return _check("voyage embedder", WARN, "voyageai not installed; using fastembed (fine, slightly weaker recall)")


def check_dir_tree() -> list[dict]:
    out = []
    required = ["agents", "commands", "skills", "hooks", "mcp", "evals", "templates", "adrs", "state"]
    for d in required:
        p = CLAUDE / d
        if p.is_dir():
            out.append(_check(f"dir: .claude/{d}/", OK))
        else:
            sev = WARN if d in ("state", "adrs") else FAIL
            out.append(_check(f"dir: .claude/{d}/", sev, "missing"))
    return out


EXPECTED_AGENTS = [
    "planner", "sketcher", "implementer", "reviewer", "tester",
    "debugger", "refactor", "auditor", "architect", "security-reviewer",
]


def check_agents() -> list[dict]:
    out = []
    d = CLAUDE / "agents"
    for name in EXPECTED_AGENTS:
        p = d / f"{name}.md"
        if p.is_file():
            out.append(_check(f"agent: {name}", OK))
        else:
            out.append(_check(f"agent: {name}", FAIL, "missing — re-run the installer to refresh agents/"))
    return out


def check_config_files() -> list[dict]:
    out = []
    # .agentic.yml
    p = CLAUDE / ".agentic.yml"
    if not p.exists():
        out.append(_check("file: .agentic.yml", FAIL, "missing"))
    else:
        try:
            import yaml  # type: ignore
            cfg = yaml.safe_load(p.read_text(encoding="utf-8")) or {}
            skills = cfg.get("enabled_skills") or []
            out.append(_check("file: .agentic.yml", OK, f"enabled_skills={skills}"))
            # risk_profile drives architect/security-reviewer calibration. Absent
            # on installs seeded before it existed (config is seed-once) — warn so
            # the developer knows those reviews fall back to generic assumptions.
            if "risk_profile" not in cfg:
                out.append(_check("config: risk_profile", WARN,
                                  "absent — architect/security-reviewer will use conservative generic "
                                  "assumptions; add the block from the template to calibrate"))
            else:
                rp = cfg.get("risk_profile") or {}
                sens = rp.get("data_sensitivity") or []
                comp = rp.get("compliance") or []
                out.append(_check("config: risk_profile", OK,
                                  f"data_sensitivity={sens} compliance={comp}"))
        except Exception as e:
            out.append(_check("file: .agentic.yml", FAIL, f"parse error: {e}"))
    # settings.json
    p = CLAUDE / "settings.json"
    if not p.exists():
        out.append(_check("file: settings.json", FAIL, "missing"))
    else:
        try:
            data = json.loads(p.read_text(encoding="utf-8"))
            hooks = data.get("hooks", {})
            n = sum(len(e.get("hooks", [])) for ev in hooks.values() for e in ev)
            if "mcpServers" in data:
                out.append(_check("file: settings.json", WARN,
                                  f"contains dead mcpServers block — MCP belongs in .mcp.json"))
            else:
                out.append(_check("file: settings.json", OK, f"{n} hook(s) registered"))
        except json.JSONDecodeError as e:
            out.append(_check("file: settings.json", FAIL, f"invalid JSON: {e}"))
    return out


def check_mcp_json() -> list[dict]:
    out = []
    p = REPO_ROOT / ".mcp.json"
    if not p.exists():
        return [_check("file: .mcp.json", WARN, "missing — MCP servers won't load. Re-run installer.")]
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        return [_check("file: .mcp.json", FAIL, f"invalid JSON: {e}")]

    raw = data.get("mcpServers", {})
    # The template uses string-valued "_"-prefixed keys (e.g. "_docs_and_research")
    # as section markers inside mcpServers. Skip any non-dict value so those
    # markers don't get counted as servers or dereferenced with .get().
    servers = {k: v for k, v in raw.items() if isinstance(v, dict)}
    if not servers:
        return [_check("file: .mcp.json", FAIL, "no mcpServers defined")]

    out.append(_check("file: .mcp.json", OK, f"{len(servers)} server(s): {', '.join(servers)}"))

    # check for unfilled placeholders
    for name, conf in servers.items():
        args_text = " ".join(str(a) for a in conf.get("args", []))
        if "REPLACE_WITH_" in args_text:
            out.append(_check(f"mcp:{name} credentials", WARN,
                              "still contains REPLACE_WITH_ placeholder"))
        else:
            out.append(_check(f"mcp:{name} credentials", OK))

    # if test_framework requires playwright or maestro, the matching MCP entry must be present
    proj_yml = CLAUDE / "state" / "project.yml"
    if proj_yml.exists():
        try:
            import yaml  # type: ignore
            proj = yaml.safe_load(proj_yml.read_text(encoding="utf-8")) or {}
            tf = proj.get("test_framework", "")
            if tf in ("playwright", "maestro") and tf not in servers:
                out.append(_check(
                    f"mcp:{tf} for screenshot",
                    WARN,
                    f"test_framework={tf} but '{tf}' is not in .mcp.json — "
                    f"/agentic-screenshot will stop at the MCP probe step",
                ))
        except Exception:
            pass  # yaml unavailable or parse error — already caught by check_config_files

    return out


def check_vectors_db() -> dict:
    p = CLAUDE / "state" / "vectors.db"
    if not p.exists():
        return _check("vectors.db", WARN, "missing — run /agentic-index")
    try:
        conn = sqlite3.connect(p)
        n = conn.execute("SELECT COUNT(*) FROM chunks").fetchone()[0]
        skills = conn.execute("SELECT DISTINCT skill FROM chunks").fetchall()
        conn.close()
        return _check("vectors.db", OK, f"{n} chunks across {len(skills)} skills: {', '.join(s[0] for s in skills)}")
    except Exception as e:
        return _check("vectors.db", FAIL, f"unreadable: {e}")


def check_hooks_compile() -> list[dict]:
    out = []
    for name in ("pre_tool_use.py", "post_tool_use.py"):
        p = CLAUDE / "hooks" / name
        if not p.exists():
            out.append(_check(f"hook: {name}", FAIL, "missing"))
            continue
        try:
            import py_compile
            py_compile.compile(str(p), doraise=True)
            out.append(_check(f"hook: {name}", OK))
        except py_compile.PyCompileError as e:
            out.append(_check(f"hook: {name}", FAIL, f"syntax error: {e.msg.splitlines()[0] if e.msg else 'unknown'}"))
    return out


def check_skills() -> list[dict]:
    out = []
    skills_dir = CLAUDE / "skills"
    if not skills_dir.is_dir():
        return [_check("skills/", FAIL, "missing")]
    found = sorted(p.name for p in skills_dir.iterdir() if p.is_dir())
    out.append(_check("skills present", OK, f"{len(found)}: {', '.join(found)}"))
    for name in found:
        sf = skills_dir / name / "SKILL.md"
        if not sf.exists():
            out.append(_check(f"skill: {name}/SKILL.md", FAIL, "missing"))
            continue
        token_estimate = int(len(sf.read_text(encoding="utf-8").split()) * 1.3)
        if token_estimate > 3000:
            out.append(_check(f"skill: {name} size", WARN,
                              f"~{token_estimate} tokens (>3000 soft limit — consider splitting)"))
        else:
            out.append(_check(f"skill: {name} size", OK, f"~{token_estimate} tokens"))
    return out


def check_adrs() -> dict:
    adrs_dir = CLAUDE / "adrs"
    if not adrs_dir.is_dir():
        return _check("adrs/", WARN, "missing")
    files = sorted(p for p in adrs_dir.glob("ADR-*.md"))
    if not files:
        return _check("adrs/", OK, "no ADRs yet (empty is fine; /agentic-adr bootstrap to propose some)")
    statuses = {"proposed": 0, "accepted": 0, "superseded": 0}
    for f in files:
        text = f.read_text(encoding="utf-8")
        for st in statuses:
            if f"status: {st}" in text:
                statuses[st] += 1
                break
    return _check("adrs/", OK, f"{len(files)} ADRs (accepted={statuses['accepted']}, proposed={statuses['proposed']}, superseded={statuses['superseded']})")


def check_stale_state() -> dict:
    # Reuse state.py's definition of "stale" so the file list and age threshold
    # live in exactly one place (doctor is run with the lib dir on sys.path).
    try:
        import state  # type: ignore
        stale = state.find_stale()
    except Exception as e:
        return _check("dispatch state", WARN, f"could not check: {e}")
    if stale:
        detail = ", ".join(f"{s['file']} ({s['age_seconds']}s)" for s in stale)
        return _check("dispatch state", WARN,
                      "leftover from an interrupted build: " + detail
                      + " — auto-cleared on next /agentic-build, or run "
                        "`python .claude/hooks/lib/state.py clear`")
    return _check("dispatch state", OK, "no stale state")


def check_git() -> list[dict]:
    out = []
    try:
        r = subprocess.run(["git", "rev-parse", "--is-inside-work-tree"],
                           cwd=REPO_ROOT, capture_output=True, text=True)
        if r.returncode != 0:
            return [_check("git repo", WARN, "not a git repo — auto-branch disabled, that's fine")]
    except FileNotFoundError:
        return [_check("git available", FAIL, "git not on PATH")]

    branch = subprocess.run(["git", "branch", "--show-current"], cwd=REPO_ROOT,
                            capture_output=True, text=True).stdout.strip()
    out.append(_check("git repo", OK, f"current branch: {branch or 'detached HEAD'}"))

    dirty = subprocess.run(["git", "status", "--porcelain"], cwd=REPO_ROOT,
                           capture_output=True, text=True).stdout.strip()
    out.append(_check("working tree", OK if not dirty else WARN,
                      "clean" if not dirty else "has uncommitted changes (next /agentic-plan will refuse if fail_on_wip=true)"))
    return out


# ────────────────── runner ──────────────────

def run_all() -> list[dict]:
    results: list[dict] = []
    results.append(check_python_version())
    results.extend(check_required_deps())
    results.append(check_optional_voyage())
    results.extend(check_dir_tree())
    results.extend(check_agents())
    results.extend(check_config_files())
    results.extend(check_mcp_json())
    results.append(check_vectors_db())
    results.extend(check_hooks_compile())
    results.extend(check_skills())
    results.append(check_adrs())
    results.append(check_stale_state())
    results.extend(check_git())
    return results


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    args = ap.parse_args()

    results = run_all()
    counts = {OK: 0, WARN: 0, FAIL: 0}
    for r in results:
        counts[r["status"]] += 1

    if args.json:
        print(json.dumps({"results": results, "counts": counts}, indent=2))
    else:
        # human-readable
        name_w = max(len(r["name"]) for r in results) + 2
        for r in results:
            line = f"{ICON[r['status']]}  {r['name']:<{name_w}}"
            if r["detail"]:
                line += f"  {r['detail']}"
            print(line)
        print()
        print(f"summary: {counts[OK]} ok, {counts[WARN]} warn, {counts[FAIL]} fail")
        if counts[FAIL] == 0 and counts[WARN] == 0:
            print("system is healthy. Have a nice day.")

    sys.exit(0 if counts[FAIL] == 0 else 1)


if __name__ == "__main__":
    main()
