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
import shutil
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


def _mcp_interpreter() -> tuple[str | None, str]:
    """The Python that will actually run agentic_mcp.py, per .mcp.json.

    Doctor is often invoked by a different interpreter than the MCP server uses —
    a venv is active for one and not the other. Importing into *this* process
    then reports on the wrong environment in both directions: a false FAIL when
    the server is fine, and a false OK when it is not. Returns (command, note);
    command is None when .mcp.json does not pin one."""
    p = REPO_ROOT / ".mcp.json"
    if not p.exists():
        return None, ".mcp.json absent"
    try:
        cfg = json.loads(p.read_text(encoding="utf-8"))
    except Exception as e:
        return None, f".mcp.json unreadable ({e})"
    srv = (cfg.get("mcpServers") or {}).get("agentic_mcp") or {}
    cmd = srv.get("command")
    if not cmd:
        return None, "agentic_mcp has no command in .mcp.json"
    resolved = shutil.which(cmd) or cmd
    return resolved, ""


def check_required_deps() -> list[dict]:
    """Probe the interpreter the MCP server uses, not the one running doctor."""
    mods = ("yaml", "fastembed", "sqlite_vec")
    pkg_of = {"yaml": "pyyaml", "sqlite_vec": "sqlite-vec"}
    # Name what a missing dep actually costs. "missing — pip install pyyaml"
    # reads as a setup nicety; nothing connects it to the fact that the project's
    # own gateway.deny patterns are then not enforced at all. The one-shot CLIs
    # warn at point of use, but pre_tool_use.py runs on every tool call and must
    # not spam stderr, so this is where that consequence gets stated.
    costs_of = {
        "yaml": "the whole of .agentic.yml is ignored — gateway.deny patterns are "
                "NOT enforced, per-role models, complexity_routing, plan_id and "
                "pricing all fall back to built-in defaults",
        "fastembed": "retrieval cannot embed; retrieve() returns nothing",
        "sqlite_vec": "vector search falls back to a slower brute-force scan",
    }

    def _missing(mod: str, how: str) -> dict:
        detail = f"{how}{' — ' + costs_of[mod] if mod in costs_of else ''}"
        return _check(f"dep: {mod}", FAIL, detail)
    interp, note = _mcp_interpreter()

    if interp is None:
        # No pinned command — fall back to this process, and say so, because the
        # result may not describe the environment retrieval actually runs in.
        out = [_check("mcp interpreter", WARN,
                      f"{note}; deps checked against {sys.executable}")]
        for mod in mods:
            try:
                importlib.import_module(mod)
                out.append(_check(f"dep: {mod}", OK))
            except Exception:
                out.append(_missing(mod, f"missing — pip install {pkg_of.get(mod, mod)}"))
        return out

    probe = "import importlib,sys;" + "".join(
        f"\ntry:\n importlib.import_module({m!r})\n print({m!r},'ok')\nexcept Exception:\n print({m!r},'missing')"
        for m in mods)
    try:
        r = subprocess.run([interp, "-c", probe], capture_output=True, text=True, timeout=60)
    except Exception as e:
        return [_check("mcp interpreter", FAIL, f"cannot run {interp}: {e}")]

    status = dict(l.split() for l in r.stdout.split("\n") if len(l.split()) == 2)
    out = [_check("mcp interpreter", OK, interp)]

    # A bare command name in .mcp.json resolves against whatever PATH the process
    # has. Claude Code may launch the MCP server from a shell with a virtualenv
    # active while doctor runs without one, so the same `python3` is two
    # different interpreters and this check silently describes the wrong one.
    # Detect the disagreement rather than picking a side.
    venv_py = REPO_ROOT / ".venv" / "bin" / "python"
    bare = not os.path.isabs(_raw_mcp_command())
    if bare and venv_py.exists() and os.path.realpath(venv_py) != os.path.realpath(interp):
        try:
            rv = subprocess.run([str(venv_py), "-c", probe],
                                capture_output=True, text=True, timeout=60)
            vstatus = dict(l.split() for l in rv.stdout.split("\n") if len(l.split()) == 2)
        except Exception:
            vstatus = {}
        if vstatus and vstatus != status:
            out.append(_check(
                "mcp interpreter ambiguous", WARN,
                f"'.venv/bin/python' has different deps than {interp}; .mcp.json "
                "uses a bare command, so which one runs depends on the launching "
                "shell's PATH — pin the absolute path in .mcp.json"))
            # Report whichever environment actually satisfies the deps, so a
            # working setup is not reported as broken.
            if all(vstatus.get(m) == "ok" for m in mods):
                status = vstatus
                out[0] = _check("mcp interpreter", OK,
                                f"{venv_py} (resolved via project venv)")

    for mod in mods:
        if status.get(mod) == "ok":
            out.append(_check(f"dep: {mod}", OK))
        else:
            out.append(_missing(mod, "missing in the interpreter above — install with its pip"))
    return out


def _raw_mcp_command() -> str:
    p = REPO_ROOT / ".mcp.json"
    try:
        cfg = json.loads(p.read_text(encoding="utf-8"))
        return ((cfg.get("mcpServers") or {}).get("agentic_mcp") or {}).get("command") or ""
    except Exception:
        return ""


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
    "designer", "design-critic",
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

    # check for unfilled placeholders — credentials live in args (e.g. supabase
    # --access-token) OR env (e.g. github GITHUB_PERSONAL_ACCESS_TOKEN), so scan both.
    for name, conf in servers.items():
        args_text = " ".join(str(a) for a in conf.get("args", []))
        env_text = " ".join(str(v) for v in (conf.get("env", {}) or {}).values())
        if "REPLACE_WITH_" in args_text or "REPLACE_WITH_" in env_text:
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


def check_skill_digest() -> dict:
    p = CLAUDE / "state" / "skills_digest.md"
    if not p.exists():
        return _check("skills_digest.md", WARN, "missing — run /agentic-index (planner falls back to whole SKILL.md files)")
    n = sum(1 for line in p.read_text(encoding="utf-8").splitlines() if line.startswith("## "))
    return _check("skills_digest.md", OK, f"{n} skills digested")


def check_hooks_compile() -> list[dict]:
    out = []
    for name in ("pre_tool_use.py", "post_tool_use.py", "log_usage.py"):
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


def check_design_spec() -> dict:
    # The UI design spec is optional — authored by /agentic-design, enforced by
    # /agentic-review-ui. Absent is fine (OK, not WARN): many projects have no
    # frontend. Present means the sketcher + design-critic have a house style.
    spec = REPO_ROOT / "docs" / "design" / "DESIGN-SPEC.md"
    if spec.is_file():
        tokens = (REPO_ROOT / "docs" / "design" / "tokens.md").is_file()
        return _check("design spec", OK,
                      f"docs/design/DESIGN-SPEC.md present{'' if tokens else ' (tokens.md missing)'}")
    return _check("design spec", OK, "none yet (optional — /agentic-design to author one)")


def check_review_enumerations() -> dict:
    """The declared search per checklist item — and whether it still parses.

    Optional (absent means reviews improvise, which is the old behaviour). But a
    file that is present and yields zero declarations is the dangerous state: the
    comparison silently does nothing and every review passes the check by having
    nothing to check against. That reads exactly like health, which is the same
    failure mode the `- [ ]` checklist extraction has.
    """
    path = REPO_ROOT / "docs" / "review" / "enumerations.md"
    if not path.is_file():
        return _check("review enumerations", OK,
                      "none yet (optional — reviews will improvise their searches, "
                      "so runs are not comparable)")
    try:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from verify_parse import declared_enumerations  # type: ignore
        text = path.read_text(encoding="utf-8", errors="replace")
    except Exception as e:
        return _check("review enumerations", WARN, f"could not read: {e}")
    counts = {r: len(declared_enumerations(text, r))
              for r in ("design-critic", "security-reviewer", "auditor")}
    total = sum(counts.values())
    detail = ", ".join(f"{r}: {n}" for r, n in counts.items())
    if total == 0:
        return _check("review enumerations", WARN,
                      "present but no declarations parsed — check the `## <reviewer>` "
                      "headings and that each row is `| <n> | <item> | `<command>` |`; "
                      "as written it silently disables the comparison")
    return _check("review enumerations", OK, detail)


def check_install_manifest() -> list[dict]:
    """Report local edits to system files, and whether the source has moved on.

    Both were unanswerable before. System dirs are mirrored on re-install, so a
    hand-edited agent is destroyed without warning — and "am I running the
    current templates?" required byte-comparing against the source tree by hand,
    because `system_version` is static and never bumped.

    The manifest records two hashes per file: `tpl` as shipped, `cur` as
    installed. They legitimately differ for agents (the installer stamps role
    models into frontmatter), which is exactly why one hash could not serve.
    """
    import hashlib
    mf_path = CLAUDE / ".install.json"
    if not mf_path.is_file():
        return [_check("install manifest", WARN,
                       "absent — installed before manifests existed; re-run the "
                       "installer to enable drift and staleness checks")]
    try:
        mf = json.loads(mf_path.read_text(encoding="utf-8"))
        files = mf.get("files") or {}
    except (json.JSONDecodeError, OSError) as e:
        return [_check("install manifest", WARN, f"unreadable: {e}")]
    if not files:
        return [_check("install manifest", WARN, "empty — re-run the installer")]

    def h(p: Path) -> str | None:
        try:
            return hashlib.sha256(p.read_bytes()).hexdigest()[:16]
        except OSError:
            return None

    out = []
    version = mf.get("system_version", "?")
    when = mf.get("installed_at", "?")
    out.append(_check("install manifest", OK,
                      f"v{version}, installed {when}, {len(files)} files tracked"))

    edited = [rel for rel, hs in files.items()
              if h(CLAUDE / rel) not in (None, hs.get("cur"))]
    # check_agents already reports a missing role agent by name, and the two
    # checks answer different questions — it asserts the system's expected roles
    # exist (catching an install from a template that predates a role), this one
    # asserts what was installed is still there. They overlap on exactly one
    # case: an agent deleted after install. Drop those here so a single deletion
    # is reported once, by the check that names the role.
    covered = {f"agents/{name}.md" for name in EXPECTED_AGENTS}
    missing = [rel for rel in files
               if rel not in covered and not (CLAUDE / rel).is_file()]
    if missing:
        out.append(_check("system files present", FAIL,
                          f"{len(missing)} tracked file(s) gone, e.g. "
                          + ", ".join(sorted(missing)[:3])
                          + " — re-run the installer"))
    if edited:
        out.append(_check("local edits to system files", WARN,
                          f"{len(edited)} edited since install: "
                          + ", ".join(sorted(edited)[:5])
                          + " — these are OVERWRITTEN by the next install; move "
                            "the change into the template repo to keep it"))
    else:
        out.append(_check("local edits to system files", OK, "none"))

    # Staleness needs the source tree the install came from. If it has moved or
    # is gone, say so rather than implying the install is current.
    src = Path(mf.get("source") or "") / "templates" / ".claude"
    if not src.is_dir():
        out.append(_check("up to date with source", WARN,
                          f"source not found at {mf.get('source')} — cannot tell "
                          "whether newer templates exist"))
        return out
    behind = [rel for rel, hs in files.items()
              if h(src / rel) not in (None, hs.get("tpl"))]
    added = [f.relative_to(src).as_posix() for f in src.rglob("*")
             if f.is_file() and "__pycache__" not in f.parts
             and f.relative_to(src).as_posix() not in files
             and f.relative_to(src).parts[0] in ("agents", "commands", "skills",
                                                 "hooks", "mcp", "evals", "templates")]
    if behind or added:
        detail = []
        if behind:
            detail.append(f"{len(behind)} changed ({', '.join(sorted(behind)[:4])})")
        if added:
            detail.append(f"{len(added)} new ({', '.join(sorted(added)[:3])})")
        out.append(_check("up to date with source", WARN,
                          "; ".join(detail) + " — re-run the installer to update"))
    else:
        out.append(_check("up to date with source", OK, "matches source templates"))
    return out


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

def check_verification() -> list[dict]:
    """The review-verification machinery, and the checklists it depends on.

    These fail *silently* when broken, which is why they are checked here. If a
    checklist is reformatted out of `- [ ]` task-list syntax, extraction returns
    zero items and coverage enforcement quietly switches itself off for that
    agent — every review then passes its coverage check by having nothing to
    check. That reads as health, not as breakage."""
    out: list[dict] = []
    lib = REPO_ROOT / ".claude" / "hooks" / "lib"
    vc_path = lib / "verify_clears.py"
    if not vc_path.exists():
        out.append(_check("verify_clears.py", FAIL,
                          "missing — re-run the installer; review clears are unverified"))
        return out
    out.append(_check("verify_clears.py", OK))

    # The command-level verify step is an instruction and can be skipped; this
    # hook is the copy the harness fires regardless. If it is not registered,
    # verification silently becomes optional again.
    hook = REPO_ROOT / ".claude" / "hooks" / "verify_review.py"
    settings = REPO_ROOT / ".claude" / "settings.json"
    if not hook.exists():
        out.append(_check("verify_review.py", FAIL, "missing — re-run the installer"))
    else:
        registered = False
        try:
            cfg = json.loads(settings.read_text(encoding="utf-8"))
            registered = any(
                "verify_review" in (h.get("command") or "")
                for entry in (cfg.get("hooks", {}).get("SubagentStop") or [])
                for h in entry.get("hooks", []))
        except Exception:
            pass
        out.append(_check("verify_review hook", OK if registered else FAIL,
                          "" if registered else
                          "not in settings.json SubagentStop — review verification "
                          "is skippable; re-run the installer to sync managed hooks"))

    try:
        sys.path.insert(0, str(lib))
        from verify_clears import checklist_items  # noqa: E402
    except Exception as e:  # pragma: no cover - import guard
        out.append(_check("verify_clears importable", FAIL, str(e)[:80]))
        return out

    # (file, label, minimum items expected)
    sources = [
        (REPO_ROOT / ".claude" / "agents" / "security-reviewer.md",
         "checklist: attacker categories", 8),
        (REPO_ROOT / ".claude" / "agents" / "architect.md",
         "checklist: design dimensions", 7),
        (REPO_ROOT / "docs" / "design" / "DESIGN-SPEC.md",
         "checklist: design spec §13", 1),
    ]
    for path, label, minimum in sources:
        if not path.exists():
            # A missing design spec is normal on a non-UI project; a missing
            # agent file is already caught by check_agents().
            out.append(_check(label, WARN, f"{path.name} not present"))
            continue
        n = len(checklist_items(path))
        if n >= minimum:
            out.append(_check(label, OK, f"{n} items"))
        else:
            out.append(_check(label, FAIL,
                              f"{n} items found, expected >= {minimum} — "
                              "coverage enforcement is disabled for this source; "
                              "items must use `- [ ]` task-list syntax"))
    return out


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
    results.append(check_skill_digest())
    results.extend(check_hooks_compile())
    results.extend(check_verification())
    results.extend(check_skills())
    results.append(check_adrs())
    results.append(check_design_spec())
    results.append(check_review_enumerations())
    results.append(check_stale_state())
    results.extend(check_install_manifest())
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
