#!/usr/bin/env python3
"""symbols — dependency-free structural code fetch for agentic_mcp.

The retrieve tool answers "where does this concept live?" (fuzzy, semantic,
returns chunks). This answers the complementary structural question: "give me
exactly this function/class, nothing around it" — so an agent that already
knows a symbol's name does not have to Read a whole 1,400-line file to edit 15.

Extraction is pure-stdlib (no tree-sitter, no grammars): two families cover the
common stacks —
  - indent family (Python): capture the def/class line, its decorators, and the
    following block until indentation returns to the declaration's level.
  - brace family (JS/TS/Go/Rust/Dart/Java/C#/Swift/...): find the declaration,
    then string/comment-aware brace-match to the closing '}'.

This is best-effort by design (block comments, regex literals, and unusual
macro forms can fool the brace scanner). It ships with zero new dependencies;
tree-sitter could later replace the scanners for full precision without
changing the tool contract.

CLI:
  python symbols.py get <name> [--file <relpath>] [--max N]
      Print a JSON list of {file, line, end_line, kind, text}. Empty list means
      the symbol was not found (not an error).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Reuse the retriever's config + project-walk helpers so "which files are
# project source" is resolved identically to indexing/retrieval. retriever's
# heavy deps are imported lazily, so importing it here needs no embedding libs.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from retriever import (  # noqa: E402
    REPO_ROOT,
    iter_project_files,
    load_config,
)

INDENT_EXTS = {".py"}
BRACE_EXTS = {
    ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
    ".go", ".rs", ".java", ".kt", ".kts", ".cs",
    ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp",
    ".dart", ".swift", ".scala", ".php",
}


def _family(ext: str) -> str | None:
    if ext in INDENT_EXTS:
        return "indent"
    if ext in BRACE_EXTS:
        return "brace"
    return None


def _decl_patterns(name: str, family: str) -> list[tuple[re.Pattern, str]]:
    """(compiled regex, kind) pairs that match a line STARTING a def of `name`."""
    n = re.escape(name)
    if family == "indent":
        return [
            (re.compile(rf"^(?P<indent>[ \t]*)(?:async[ \t]+)?def[ \t]+{n}\b"), "function"),
            (re.compile(rf"^(?P<indent>[ \t]*)class[ \t]+{n}\b"), "class"),
        ]
    # brace family — keyword declarations first (specific), method form last.
    return [
        (re.compile(rf"\bfunction\b[\w\s*]*\b{n}\b"), "function"),
        (re.compile(rf"\bfn[ \t]+{n}\b"), "function"),
        (re.compile(rf"\bfunc[ \t]+(?:\([^)]*\)[ \t]*)?{n}\b"), "function"),
        (re.compile(rf"\b(?:class|interface|enum|struct|trait|impl|protocol)[ \t]+{n}\b"), "type"),
        (re.compile(rf"\btype[ \t]+{n}\b"), "type"),
        (re.compile(rf"\b(?:const|let|var)[ \t]+{n}\b[ \t]*=\s*(?:async\s*)?(?:function|\(|<|[\w$]+\s*=>)"), "function"),
        # method / free function with a signature ending in '{' (not a call ';').
        (re.compile(rf"^[ \t]*(?:[\w<>\[\],?.&*:@ \t]+[ \t]+)?{n}[ \t]*\([^;{{]*\)[ \t]*(?:[:\-\w<>\[\],?.&* \t]+)?\{{"), "method"),
    ]


def _find_decl_lines(lines: list[str], name: str, family: str) -> list[tuple[int, str]]:
    """Return (line_index, kind) for each line that starts a def of `name`."""
    pats = _decl_patterns(name, family)
    hits: list[tuple[int, str]] = []
    for i, line in enumerate(lines):
        for pat, kind in pats:
            if pat.search(line):
                hits.append((i, kind))
                break
    return hits


def _indent_of(line: str) -> int:
    return len(line) - len(line.lstrip(" \t"))


def _indent_span(lines: list[str], start: int) -> tuple[int, int]:
    """Body span for an indent-family (Python) declaration at `start`.

    Extends upward over contiguous decorator lines and downward until a
    non-blank line de-indents to at or below the declaration's own indent.
    Returns (first_index, last_index) inclusive.
    """
    base = _indent_of(lines[start])
    first = start
    j = start - 1
    while j >= 0 and lines[j].lstrip().startswith("@"):
        first = j
        j -= 1
    end = start
    k = start + 1
    while k < len(lines):
        line = lines[k]
        if line.strip() == "":
            k += 1
            continue
        if _indent_of(line) <= base:
            break
        end = k
        k += 1
    return first, end


def _brace_span(lines: list[str], start: int) -> tuple[int, int] | None:
    """Body span for a brace-family declaration at `start`.

    Scans forward from the declaration line for the first '{' and matches to its
    closing '}', ignoring braces inside strings and // line comments. Returns
    (start, end_index) inclusive, or None if no balanced block is found (e.g. an
    arrow with an expression body — handled by the caller's fallback).
    """
    depth = 0
    started = False
    in_str: str | None = None
    escaped = False
    i = start
    while i < len(lines):
        line = lines[i]
        j = 0
        while j < len(line):
            c = line[j]
            if in_str is not None:
                if escaped:
                    escaped = False
                elif c == "\\":
                    escaped = True
                elif c == in_str:
                    in_str = None
            elif c in ("'", '"', "`"):
                in_str = c
            elif c == "/" and j + 1 < len(line) and line[j + 1] == "/":
                break  # rest of line is a // comment
            elif c == "{":
                depth += 1
                started = True
            elif c == "}":
                depth -= 1
                if started and depth == 0:
                    return start, i
            j += 1
        i += 1
    return None


def _expr_span(lines: list[str], start: int, limit: int = 40) -> tuple[int, int]:
    """Fallback span for a brace-less form (arrow expression body): capture from
    `start` until a line ending in ';' (within `limit` lines), else one line."""
    for k in range(start, min(len(lines), start + limit)):
        if lines[k].rstrip().endswith(";"):
            return start, k
    return start, start


def extract_symbols(source: str, name: str, ext: str) -> list[dict]:
    """All definitions of `name` in one file's `source`. Best-effort.

    Returns [] for an unsupported extension rather than guessing a family —
    e.g. Ruby/Elixir are `def…end`/`do…end`, not brace- or indent-delimited,
    so a brace-family guess would silently mis-extract. Callers that want an
    explicit "unsupported language" signal should check `_family(ext)` first.
    """
    family = _family(ext)
    if family is None:
        return []
    lines = source.splitlines()
    out: list[dict] = []
    for idx, kind in _find_decl_lines(lines, name, family):
        if family == "indent":
            first, last = _indent_span(lines, idx)
        else:
            span = _brace_span(lines, idx)
            first, last = span if span is not None else _expr_span(lines, idx)
        out.append({
            "line": first + 1,          # 1-indexed
            "end_line": last + 1,
            "kind": kind,
            "text": "\n".join(lines[first:last + 1]),
        })
    return out


def get_symbol(name: str, file: str | None, max_matches: int) -> list[dict]:
    results: list[dict] = []
    if file:
        p = (REPO_ROOT / file).resolve()
        try:
            p.relative_to(REPO_ROOT)  # keep lookups inside the repo
        except ValueError:
            return [{"error": f"file outside repo: {file}"}]
        if not p.is_file():
            return [{"error": f"file not found: {file}"}]
        if _family(p.suffix) is None:
            return [{"error": (
                f"unsupported language for get_symbol: '{p.suffix or p.name}'. "
                f"Supported: Python (indent) and brace languages "
                f"(JS/TS/Go/Rust/Java/Kotlin/C#/C/C++/Dart/Swift/Scala/PHP). "
                f"Use Read for other languages."
            )}]
        try:
            text = p.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError) as e:
            return [{"error": f"could not read {file}: {e}"}]
        for m in extract_symbols(text, name, p.suffix):
            m["file"] = file
            results.append(m)
        return results[:max_matches]

    cfg = load_config()
    files = iter_project_files(cfg)
    if not files:
        return [{"error": "no project globs configured — run /agentic-index, or pass --file"}]
    for p in files:
        if _family(p.suffix) is None:
            continue
        try:
            text = p.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        if name not in text:  # cheap pre-filter before regex/parse
            continue
        rel = str(p.relative_to(REPO_ROOT))
        for m in extract_symbols(text, name, p.suffix):
            m["file"] = rel
            results.append(m)
            if len(results) >= max_matches:
                return results
    return results


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(prog="symbols.py")
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("get")
    g.add_argument("name")
    g.add_argument("--file", default=None)
    g.add_argument("--max", type=int, default=5)
    args = ap.parse_args(argv[1:])

    matches = get_symbol(args.name, args.file, max(1, args.max))
    print(json.dumps(matches, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
