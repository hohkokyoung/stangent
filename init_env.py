"""
Stangent environment detection and validation:
provider detection, credential checks, git check, tool checks,
profile detection, and src-root detection.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

from init_constants import (
    REQUIRED_PYTHON, PROFILES, PROVIDERS, TOOL_INSTALL_HINTS,
    ok, fail, warn, info,
)


def check_python_version() -> bool:
    if sys.version_info < REQUIRED_PYTHON:
        fail(f"Python {REQUIRED_PYTHON[0]}.{REQUIRED_PYTHON[1]}+ required. "
             f"Found {sys.version_info.major}.{sys.version_info.minor}")
        return False
    ok(f"Python {sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}")
    return True


def _load_dotenv() -> dict[str, str]:
    """Read .env file and return {KEY: value} without modifying os.environ."""
    env_file = Path(".env")
    if not env_file.exists():
        return {}
    result = {}
    for line in env_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        result[k.strip()] = v.strip().strip("\"'")
    return result


def _get_env(key: str, dotenv: dict[str, str]) -> str:
    """Return env var from os.environ, falling back to .env file."""
    return os.environ.get(key, dotenv.get(key, ""))


def detect_provider() -> str | None:
    """
    Auto-detect provider from environment variables.
    Checks ALL providers in priority order: anthropic, groq, openrouter,
    openai, bedrock, vertex. ollama is excluded (no env var to detect).
    Returns provider name or None if nothing is set.
    """
    PRIORITY = ["anthropic", "groq", "openrouter", "openai", "bedrock", "vertex"]
    dotenv = _load_dotenv()
    for name in PRIORITY:
        prov = PROVIDERS.get(name, {})
        if prov.get("detect_env") and any(_get_env(e, dotenv) for e in prov["detect_env"]):
            return name
    return None


def check_credentials(provider_name: str) -> bool:
    """
    Verify that all required env vars for the given provider are present.
    Reads from os.environ and .env file.
    Providers with no required_env (e.g. Ollama) always pass.
    """
    dotenv  = _load_dotenv()
    prov    = PROVIDERS[provider_name]
    all_ok  = True

    if not prov["required_env"]:
        ok(f"{prov['display']} — no API key required")
        return True

    for key in prov["required_env"]:
        val = _get_env(key, dotenv)
        if not val:
            fail(f"{key} not set (required for {prov['display']})")
            all_ok = False
        else:
            masked = val[:6] + "..." + val[-3:] if len(val) > 9 else "***"
            ok(f"{key} ({masked})")

    for key in prov["optional_env"]:
        val = _get_env(key, dotenv)
        if val:
            ok(f"{key} (optional, set)")
        # silently skip missing optionals

    return all_ok


def check_git() -> bool:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--git-dir"],
            capture_output=True, text=True, cwd=Path.cwd()
        )
        if result.returncode == 0:
            ok("git repository detected")
            return True
        fail("Not a git repository.  git init")
        print("  The pipeline agents (implement, review) require git to create")
        print("  branches and commits. Scaffolding will continue but the pipeline")
        print("  will fail until you initialise git.")
        return False
    except FileNotFoundError:
        fail("git not found in PATH.  https://git-scm.com/downloads")
        return False


def tool_in_path(tool: str) -> bool:
    return shutil.which(tool) is not None


def check_tools(
    profile_name: str,
    already_checked: set[str] | None = None,
) -> tuple[bool, list[str]]:
    """
    Check required and optional tools for a profile.
    already_checked: shared set across multiple profile calls — tools already
    verified (pass or fail) are silently skipped so they aren't reported twice.
    Prints install hints inline for any missing tool.
    Returns (all_required_found, list_of_missing_required_tools).
    """
    if already_checked is None:
        already_checked = set()

    profile   = PROFILES[profile_name]
    all_ok    = True
    missing_required: list[str] = []
    missing_pip: list[str] = []       # pip tools missing — for combined one-liner

    for tool in profile["required_tools"]:
        if tool in already_checked:
            continue
        already_checked.add(tool)
        if tool_in_path(tool):
            ok(f"{tool}")
        else:
            hint = TOOL_INSTALL_HINTS.get(tool, "")
            fail(f"{tool} — not found (required).  {hint}")
            if hint.startswith("pip install"):
                missing_pip.append(tool)
            all_ok = False
            missing_required.append(tool)

    for tool in profile.get("optional_tools", []):
        if tool in already_checked:
            continue
        already_checked.add(tool)
        if tool_in_path(tool):
            ok(f"{tool} (optional)")
        else:
            hint = TOOL_INSTALL_HINTS.get(tool, "")
            warn(f"{tool} — not found (optional).  {hint}")

    # If multiple pip tools are missing, show a single combined install command
    if len(missing_pip) > 1:
        combined = "pip install " + " ".join(missing_pip)
        print(f"\n  Install all missing {profile_name} tools at once:")
        print(f"    {combined}\n")

    return all_ok, missing_required


def detect_profiles(project_root: Path) -> list[str]:
    """
    Return all profiles whose detection files are present.
    Checks project root first, then one level of subdirectories (monorepo support).
    """
    found = []
    # Collect all candidate directories: root + immediate subdirs
    candidates = [project_root] + [
        d for d in sorted(project_root.iterdir())
        if d.is_dir() and not d.name.startswith(".")
    ]
    for profile_name, profile in PROFILES.items():
        for detect_file in profile["detect_files"]:
            if any((d / detect_file).exists() for d in candidates):
                found.append(profile_name)
                break
    return found


def detect_src_root(project_root: Path, profile_name: str) -> str:
    """
    Best-guess src root for a single profile.

    Strategy:
    1. Find where the profile's detection file lives (root or one level deep).
       This handles monorepos where e.g. pubspec.yaml is in mobile/ not root.
    2. Inside that directory, look for the profile's standard src_root.
    3. Fall back to the raw default if nothing else matches.

    Returns a path string relative to project_root (e.g. "mobile/lib/").
    """
    profile   = PROFILES[profile_name]
    default   = profile["src_root"]          # e.g. "lib/" or "src/"
    det_files = profile["detect_files"]

    # Step 1 — find the directory that contains the profile's detection file
    anchor_dir: Path | None = None

    # Check project root first
    for det in det_files:
        if (project_root / det).exists():
            anchor_dir = project_root
            break

    # If not at root, search one level of subdirectories (monorepo support)
    if anchor_dir is None:
        for subdir in sorted(project_root.iterdir()):
            if not subdir.is_dir() or subdir.name.startswith("."):
                continue
            for det in det_files:
                if (subdir / det).exists():
                    anchor_dir = subdir
                    break
            if anchor_dir:
                break

    if anchor_dir is None:
        # Nothing found — return default unchanged
        return default

    # Step 2 — inside anchor_dir, find the src root
    if (anchor_dir / default).exists():
        src = anchor_dir / default
    else:
        src = None
        for guess in ["src/", "lib/", "app/"]:
            candidate = anchor_dir / guess
            if candidate.exists():
                src = candidate
                break
        if src is None:
            src = anchor_dir  # use the anchor dir itself as the src root

    # Step 3 — return relative to project_root, always with trailing slash
    rel = src.relative_to(project_root).as_posix()
    return rel.rstrip("/") + "/"


def detect_src_roots(project_root: Path, profile_names: list[str]) -> dict[str, str]:
    """Return {profile_name: src_root} for every profile in the list."""
    return {name: detect_src_root(project_root, name) for name in profile_names}
