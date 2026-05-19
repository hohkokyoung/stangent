#!/usr/bin/env python3
"""
Stangent Symbol Index Builder

Builds .stangent/symbol_index.json — a fast-lookup index of exported symbols
(classes, functions, API routes) across the project. Allows agents to find
relevant files by symbol name without scanning the entire codebase.

Usage:
    # Build or refresh the index
    python build_index.py <project_root> <config_path>

    # Query: return matching file paths (one per line) for a symbol
    python build_index.py --query <symbol> <project_root> <config_path>

    # Check if index is fresh (exit 0 = fresh, exit 1 = stale/missing)
    python build_index.py --check <project_root> <config_path>
"""
import sys
import re
import json
import subprocess
from pathlib import Path

INDEX_VERSION = 1

SKIP_DIRS = {
    "__pycache__", ".git", ".dart_tool", "node_modules",
    "build", ".stangent", ".claude", ".venv", "venv",
    "env", ".env", "dist", "coverage", ".mypy_cache",
    ".pytest_cache", ".ruff_cache", ".idea", ".vs",
}


# ── Git ───────────────────────────────────────────────────────────────────────

def get_git_hash(project_root: Path) -> str:
    try:
        r = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=project_root, timeout=10,
        )
        return r.stdout.strip() if r.returncode == 0 else ""
    except Exception:
        return ""


# ── Python extraction ─────────────────────────────────────────────────────────

_PY_CLASS   = re.compile(r"^class\s+(\w+)", re.MULTILINE)
_PY_FUNC    = re.compile(r"^(?:async\s+)?def\s+(\w+)", re.MULTILINE)
_PY_FROM    = re.compile(r"^from\s+(\S+)\s+import", re.MULTILINE)
_PY_IMPORT  = re.compile(r"^import\s+(\S+)", re.MULTILINE)
_PY_ROUTE   = re.compile(r'@\w+\.(get|post|put|delete|patch)\(["\']([^"\']+)["\']', re.MULTILINE)


def _extract_python(content: str) -> dict:
    classes  = _PY_CLASS.findall(content)
    funcs    = [f for f in _PY_FUNC.findall(content) if not f.startswith("_")]
    imports  = list({*_PY_FROM.findall(content), *_PY_IMPORT.findall(content)})
    routes   = [f"{m.upper()} {p}" for m, p in _PY_ROUTE.findall(content)]
    exports  = classes + funcs
    return {"exports": exports, "imports": imports, "symbols": exports + routes}


# ── Dart extraction ───────────────────────────────────────────────────────────

_DART_CLASS  = re.compile(r"^(?:abstract\s+)?(?:class|mixin|enum)\s+(\w+)", re.MULTILINE)
_DART_METHOD = re.compile(
    r"(?:Future|void|String|int|double|bool|List|Map|Widget|[A-Z]\w+)\??\s+(\w+)\s*\(",
    re.MULTILINE,
)
_DART_IMPORT = re.compile(r"""^import\s+['"]([^'"]+)['"]""", re.MULTILINE)


def _extract_dart(content: str) -> dict:
    classes  = _DART_CLASS.findall(content)
    methods  = [m for m in _DART_METHOD.findall(content) if not m.startswith("_") and m[0].islower()]
    imports  = _DART_IMPORT.findall(content)
    exports  = classes
    return {"exports": exports, "imports": imports, "symbols": exports + methods}


# ── Dispatcher ───────────────────────────────────────────────────────────────

def extract_symbols(file_path: Path, project_root: Path) -> dict | None:
    suffix = file_path.suffix.lower()
    if suffix not in (".py", ".dart"):
        return None
    try:
        content = file_path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return None
    if suffix == ".py":
        return _extract_python(content)
    return _extract_dart(content)


def _is_test_file(file_path: Path) -> bool:
    parts = {p.lower() for p in file_path.parts}
    name  = file_path.name
    return (
        "test" in parts or "tests" in parts
        or name.startswith("test_")
        or name.endswith("_test.dart")
        or name.endswith("_test.py")
    )


def _skip_dir(name: str) -> bool:
    return name in SKIP_DIRS or name.startswith(".")


# ── Build ─────────────────────────────────────────────────────────────────────

def build_index(project_root: Path, config: dict) -> dict:
    git_hash   = get_git_hash(project_root)
    files: dict[str, dict]         = {}
    symbol_map: dict[str, list[str]] = {}

    profile_roots = config.get("profile_roots", {})
    scan_roots    = list(profile_roots.values()) if profile_roots else ["."]

    for root_rel in scan_roots:
        scan_dir = project_root / root_rel
        if not scan_dir.exists():
            continue

        for file_path in scan_dir.rglob("*"):
            if not file_path.is_file():
                continue
            # Skip ignored directories anywhere in the path
            rel_parts = file_path.relative_to(project_root).parts
            if any(_skip_dir(p) for p in rel_parts[:-1]):
                continue
            if _is_test_file(file_path):
                continue

            entry = extract_symbols(file_path, project_root)
            if not entry:
                continue

            rel = str(file_path.relative_to(project_root)).replace("\\", "/")
            files[rel] = entry
            for sym in entry["exports"]:
                symbol_map.setdefault(sym, [])
                if rel not in symbol_map[sym]:
                    symbol_map[sym].append(rel)

    return {
        "version":    INDEX_VERSION,
        "git_hash":   git_hash,
        "file_count": len(files),
        "files":      files,
        "symbol_map": symbol_map,
    }


# ── Query ─────────────────────────────────────────────────────────────────────

def query_index(index: dict, symbol: str) -> list[str]:
    # Exact match first
    exact = index.get("symbol_map", {}).get(symbol, [])
    if exact:
        return list(exact)

    # Partial match across exports and all symbols
    lower   = symbol.lower()
    matched: set[str] = set()

    for sym, paths in index.get("symbol_map", {}).items():
        if lower in sym.lower():
            matched.update(paths)

    for rel, entry in index.get("files", {}).items():
        for sym in entry.get("symbols", []):
            if lower in sym.lower():
                matched.add(rel)

    return sorted(matched)


# ── Index I/O ─────────────────────────────────────────────────────────────────

def load_index(index_path: Path) -> dict:
    if not index_path.exists():
        return {}
    try:
        return json.loads(index_path.read_text(encoding="utf-8"))
    except Exception:
        return {}


def is_fresh(existing: dict, current_hash: str) -> bool:
    return (
        bool(existing)
        and existing.get("version") == INDEX_VERSION
        and existing.get("git_hash") == current_hash
    )


def refresh_if_stale(project_root: Path, config: dict, index_path: Path) -> dict:
    current_hash = get_git_hash(project_root)
    existing     = load_index(index_path)

    if is_fresh(existing, current_hash):
        return existing

    print("[Index] Building symbol index...", file=sys.stderr)
    index = build_index(project_root, config)
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, indent=2), encoding="utf-8")
    print(f"[Index] Indexed {index['file_count']} files.", file=sys.stderr)
    return index


# ── Main ─────────────────────────────────────────────────────────────────────

def main() -> None:
    args = list(sys.argv[1:])

    if not args:
        print("Usage: build_index.py [--query <symbol> | --check] <project_root> <config_path>")
        sys.exit(1)

    mode   = "build"
    symbol = None

    if args[0] == "--query":
        if len(args) < 2:
            print("--query requires a symbol argument")
            sys.exit(1)
        mode   = "query"
        symbol = args[1]
        args   = args[2:]
    elif args[0] == "--check":
        mode = "check"
        args = args[1:]

    if len(args) < 2:
        print("Usage: build_index.py [--query <symbol> | --check] <project_root> <config_path>")
        sys.exit(1)

    project_root = Path(args[0])
    config_path  = Path(args[1])
    index_path   = project_root / ".stangent" / "symbol_index.json"

    config: dict = {}
    if config_path.exists():
        try:
            config = json.loads(config_path.read_text(encoding="utf-8"))
        except Exception:
            pass

    if mode == "check":
        current_hash = get_git_hash(project_root)
        existing     = load_index(index_path)
        if is_fresh(existing, current_hash):
            print("fresh")
            sys.exit(0)
        else:
            print("stale")
            sys.exit(1)

    index = refresh_if_stale(project_root, config, index_path)

    if mode == "query":
        results = query_index(index, symbol)  # type: ignore[arg-type]
        if results:
            for r in results:
                print(r)
        else:
            print(f"[Index] No matches for {symbol!r}", file=sys.stderr)
        sys.exit(0)

    # build mode
    print(f"[Index] Symbol index up to date — {index['file_count']} files indexed.")
    sys.exit(0)


if __name__ == "__main__":
    main()
