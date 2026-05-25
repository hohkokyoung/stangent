#!/usr/bin/env python3
"""
Stangent Symbol Index Builder v2

Builds .stangent/symbol_index.json — a fast-lookup index of exported symbols
(classes, functions, API routes) with code snippets. Allows agents to get
relevant code context without reading full files.

Usage:
    # Build or refresh the index
    python build_index.py <project_root> <config_path>

    # Query: return file paths for a symbol (backward compat)
    python build_index.py --query <symbol> <project_root> <config_path>

    # Snippet query: return code snippets matching keywords (use this for planning)
    python build_index.py --snippet <keywords> <project_root> <config_path>

    # Check if index is fresh (exit 0 = fresh, exit 1 = stale/missing)
    python build_index.py --check <project_root> <config_path>

The --snippet mode is the primary interface for agents. Instead of globbing
and reading full files, agents query the index and get relevant snippets
directly — typically 3-5k tokens vs 30-50k for full file reads.
"""
import sys
import re
import json
import subprocess
from pathlib import Path

INDEX_VERSION = 2
SNIPPET_LINES = 25  # lines captured per symbol definition

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


# ── Symbol extraction (exports/imports lists — unchanged from v1) ─────────────

_PY_CLASS   = re.compile(r"^class\s+(\w+)", re.MULTILINE)
_PY_FUNC    = re.compile(r"^(?:async\s+)?def\s+(\w+)", re.MULTILINE)
_PY_FROM    = re.compile(r"^from\s+(\S+)\s+import", re.MULTILINE)
_PY_IMPORT  = re.compile(r"^import\s+(\S+)", re.MULTILINE)
_PY_ROUTE   = re.compile(r'@\w+\.(get|post|put|delete|patch)\(["\']([^"\']+)["\']', re.MULTILINE)

_DART_CLASS  = re.compile(r"^(?:abstract\s+)?(?:class|mixin|enum)\s+(\w+)", re.MULTILINE)
_DART_METHOD = re.compile(
    r"(?:Future|void|String|int|double|bool|List|Map|Widget|[A-Z]\w+)\??\s+(\w+)\s*\(",
    re.MULTILINE,
)
_DART_IMPORT = re.compile(r"""^import\s+['"]([^'"]+)['"]""", re.MULTILINE)


def _extract_python(content: str) -> dict:
    classes = _PY_CLASS.findall(content)
    funcs   = [f for f in _PY_FUNC.findall(content) if not f.startswith("_")]
    imports = list({*_PY_FROM.findall(content), *_PY_IMPORT.findall(content)})
    routes  = [f"{m.upper()} {p}" for m, p in _PY_ROUTE.findall(content)]
    exports = classes + funcs
    return {"exports": exports, "imports": imports, "symbols": exports + routes}


def _extract_dart(content: str) -> dict:
    classes = _DART_CLASS.findall(content)
    methods = [m for m in _DART_METHOD.findall(content) if not m.startswith("_") and m[0].islower()]
    imports = _DART_IMPORT.findall(content)
    return {"exports": classes, "imports": imports, "symbols": classes + methods}


# ── Snippet extraction (v2) ───────────────────────────────────────────────────

# Keywords that look like method names but are control-flow — skip them.
_DART_SKIP_NAMES = frozenset({
    "if", "for", "while", "switch", "return", "await", "async",
    "build", "get", "set",
})


def _snippets_python(lines: list[str]) -> list[dict]:
    results = []
    for i, line in enumerate(lines):
        # Class
        m = re.match(r"^class\s+(\w+)", line)
        if m:
            results.append({
                "symbol": m.group(1),
                "line": i + 1,
                "snippet": "\n".join(lines[i: i + SNIPPET_LINES]),
            })
            continue
        # Public function / method
        m = re.match(r"^(?:async\s+)?def\s+(\w+)", line)
        if m and not m.group(1).startswith("_"):
            results.append({
                "symbol": m.group(1),
                "line": i + 1,
                "snippet": "\n".join(lines[i: i + SNIPPET_LINES]),
            })
            continue
        # API route decorator — pair with the function on the next non-blank line
        m = re.match(r'^\s*@\w+\.(get|post|put|delete|patch)\(["\']([^"\']+)["\']', line)
        if m:
            method, path = m.group(1).upper(), m.group(2)
            results.append({
                "symbol": f"{method} {path}",
                "line": i + 1,
                "snippet": "\n".join(lines[i: i + SNIPPET_LINES]),
            })
    return results


def _snippets_dart(lines: list[str]) -> list[dict]:
    results = []
    for i, line in enumerate(lines):
        stripped = line.strip()
        # Class / mixin / enum
        m = re.match(r"^(?:abstract\s+)?(?:class|mixin|enum)\s+(\w+)", stripped)
        if m:
            results.append({
                "symbol": m.group(1),
                "line": i + 1,
                "snippet": "\n".join(lines[i: i + SNIPPET_LINES]),
            })
            continue
        # Public method (indented, return-type prefix)
        m = re.match(
            r"(?:Future|void|String|int|double|bool|List|Map|Widget|[A-Z]\w+)\??\s+(\w+)\s*[(<]",
            stripped,
        )
        if m:
            name = m.group(1)
            if (
                not name.startswith("_")
                and name[0].islower()
                and name not in _DART_SKIP_NAMES
                and line.startswith(("  ", "\t"))  # must be indented (class member)
            ):
                results.append({
                    "symbol": name,
                    "line": i + 1,
                    "snippet": "\n".join(lines[i: i + SNIPPET_LINES]),
                })
    return results


# ── Dispatcher ───────────────────────────────────────────────────────────────

def extract_symbols(file_path: Path, project_root: Path) -> dict | None:
    suffix = file_path.suffix.lower()
    if suffix not in (".py", ".dart"):
        return None
    try:
        content = file_path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return None
    lines = content.splitlines()
    if suffix == ".py":
        base = _extract_python(content)
        base["snippets"] = _snippets_python(lines)
    else:
        base = _extract_dart(content)
        base["snippets"] = _snippets_dart(lines)
    return base


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
    git_hash    = get_git_hash(project_root)
    files: dict[str, dict]            = {}
    symbol_map: dict[str, dict]       = {}   # sym → {files: [...], snippets: [...]}

    profile_roots = config.get("profile_roots", {})
    scan_roots    = list(profile_roots.values()) if profile_roots else ["."]

    for root_rel in scan_roots:
        scan_dir = project_root / root_rel
        if not scan_dir.exists():
            continue

        for file_path in scan_dir.rglob("*"):
            if not file_path.is_file():
                continue
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

            # Build symbol_map with file list + snippets
            for sym in entry["exports"]:
                if sym not in symbol_map:
                    symbol_map[sym] = {"files": [], "snippets": []}
                if rel not in symbol_map[sym]["files"]:
                    symbol_map[sym]["files"].append(rel)

            for snip in entry.get("snippets", []):
                sym = snip["symbol"]
                if sym not in symbol_map:
                    symbol_map[sym] = {"files": [], "snippets": []}
                if rel not in symbol_map[sym]["files"]:
                    symbol_map[sym]["files"].append(rel)
                symbol_map[sym]["snippets"].append({
                    "file":    rel,
                    "line":    snip["line"],
                    "snippet": snip["snippet"],
                })

    return {
        "version":    INDEX_VERSION,
        "git_hash":   git_hash,
        "file_count": len(files),
        "files":      files,
        "symbol_map": symbol_map,
    }


# ── Query — file paths (backward compat) ─────────────────────────────────────

def query_index(index: dict, symbol: str) -> list[str]:
    """Return file paths matching symbol. Handles both v1 (list) and v2 (dict) format."""
    def _files(data) -> list[str]:
        if isinstance(data, list):
            return data          # v1 format
        return data.get("files", [])  # v2 format

    exact = index.get("symbol_map", {}).get(symbol)
    if exact:
        return list(_files(exact))

    lower   = symbol.lower()
    matched: set[str] = set()

    for sym, data in index.get("symbol_map", {}).items():
        if lower in sym.lower():
            matched.update(_files(data))

    for rel, entry in index.get("files", {}).items():
        for sym in entry.get("symbols", []):
            if lower in sym.lower():
                matched.add(rel)

    return sorted(matched)


# ── Query — snippets (v2, primary planning interface) ─────────────────────────

def query_snippets(index: dict, keywords: str, max_results: int = 12) -> list[dict]:
    """
    Return ranked code snippets matching the given keyword string.
    Scoring: symbol name match = 3pts, file path match = 2pts, snippet body match = 1pt.
    """
    terms = [t for t in keywords.lower().split() if len(t) > 2]
    if not terms:
        return []

    scored: list[tuple[int, dict]] = []

    for sym, data in index.get("symbol_map", {}).items():
        if isinstance(data, list):
            continue  # v1 index — no snippets stored
        for snip in data.get("snippets", []):
            score = 0
            sym_lower     = sym.lower()
            file_lower    = snip.get("file", "").lower()
            snippet_lower = snip.get("snippet", "").lower()
            for term in terms:
                if term in sym_lower:
                    score += 3
                if term in file_lower:
                    score += 2
                if term in snippet_lower:
                    score += 1
            if score > 0:
                scored.append((score, {**snip, "symbol": sym}))

    # Sort by score desc, deduplicate by (file, line)
    scored.sort(key=lambda x: -x[0])
    seen:   set[tuple] = set()
    result: list[dict] = []
    for _, s in scored:
        key = (s["file"], s["line"])
        if key not in seen:
            seen.add(key)
            result.append(s)
        if len(result) >= max_results:
            break
    return result


def format_snippets(snippets: list[dict]) -> str:
    parts = []
    for s in snippets:
        header = f"=== {s['file']}:{s['line']} [{s['symbol']}] ==="
        parts.append(f"{header}\n{s['snippet']}")
    return "\n\n".join(parts)


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
        print("Usage: build_index.py [--query <symbol> | --snippet <keywords> | --check] <project_root> <config_path>")
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
    elif args[0] == "--snippet":
        if len(args) < 2:
            print("--snippet requires a keywords argument")
            sys.exit(1)
        mode   = "snippet"
        symbol = args[1]
        args   = args[2:]
    elif args[0] == "--check":
        mode = "check"
        args = args[1:]

    if len(args) < 2:
        print("Usage: build_index.py [--query <symbol> | --snippet <keywords> | --check] <project_root> <config_path>")
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
        results = query_index(index, symbol)   # type: ignore[arg-type]
        if results:
            for r in results:
                print(r)
        else:
            print(f"[Index] No matches for {symbol!r}", file=sys.stderr)
        sys.exit(0)

    if mode == "snippet":
        snippets = query_snippets(index, symbol)  # type: ignore[arg-type]
        if snippets:
            print(format_snippets(snippets))
        else:
            print(f"[Index] No snippets for {symbol!r}", file=sys.stderr)
        sys.exit(0)

    # build mode
    print(f"[Index] Symbol index up to date — {index['file_count']} files indexed.")
    sys.exit(0)


if __name__ == "__main__":
    main()
