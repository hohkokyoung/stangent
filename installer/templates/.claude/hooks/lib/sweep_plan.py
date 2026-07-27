#!/usr/bin/env python3
"""Deterministic batch planner for an exhaustive review sweep.

A normal review reads what it chooses to read. That is where completeness dies:
the codebase does not fit in one context, so the agent samples, and sampling is
unrepeatable — two runs over identical code examine different subsets and each
reports findings the other missed. No amount of reviewer discipline fixes it,
because the choosing is the problem.

So the choosing moves out of the agent, exactly as `dispatch_plan.py` did for
task order. This computes the batches; the command executes them; the agent only
judges what it is handed. Coverage then stops being a claim and becomes
arithmetic: every in-scope file is in exactly one batch, and every batch produced
a report.

Usage:
    sweep_plan.py plan <scope> [--max-files N] [--max-chars N] [--json]
    sweep_plan.py verify <scope> <batch-dir> [--json]

`plan` emits the batches. `verify` checks a finished sweep: every batch reported,
every file accounted for, nothing examined twice.

Exit codes: 0 ok · 2 usage/scope error · 1 (verify) incomplete sweep.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO_ROOT = Path.cwd().resolve()

# Batch size. Files-per-batch keeps the agent's attention on a small set; the
# char cap is what actually protects the context window, since one 60 KB widget
# file can outweigh fifteen small ones. Whichever limit is reached first closes
# the batch — in practice the char cap binds first on real code.
#
# The cap trades cost against attention, and both directions cost something. Each
# batch re-pays the fixed overhead (role prompt, design spec, evidence policy —
# call it 15-20k tokens), so halving the cap nearly doubles the sweep's total
# spend without any file being read more carefully. Going the other way, a batch
# large enough to fill the window means checking every rule against 50k tokens of
# code in one pass, which is where an agent starts skimming — reintroducing the
# sampling this module exists to remove, just inside a batch instead of across
# the codebase.
#
# 80k chars (~20k tokens of code) sits between: a real project's UI comes out
# around 8-10 files per batch, which is small enough to hold in view and large
# enough that the overhead is not the dominant cost. Override per run when the
# codebase is unusually shaped.
DEFAULT_MAX_FILES = 20
DEFAULT_MAX_CHARS = 80_000

# Machine-written and vendored code. Reviewing generated output wastes a pass and
# produces findings nobody can act on — the fix belongs in the generator. Kept
# deliberately in sync with retriever.py's exclusions, which answer the same
# question ("what is this project's own source?") for indexing.
DEFAULT_EXCLUDES = (
    ".git/", ".claude/", "node_modules/", "build/", "dist/", ".next/",
    "__pycache__/", ".venv/", "venv/", "target/", "vendor/", "Pods/",
    ".dart_tool/", ".gradle/", ".idea/", "obj/", "bin/", "DerivedData/",
    ".pub-cache/", "coverage/",
)
DEFAULT_EXCLUDE_SUFFIXES = (
    ".g.dart", ".freezed.dart", ".gr.dart", ".config.dart", ".mocks.dart",
    ".pb.dart", ".pbenum.dart", ".pbjson.dart", ".pbserver.dart",
    ".pb.go", "_pb2.py", "_pb2_grpc.py", ".d.ts", ".map", ".min.js",
    ".designer.cs", ".g.cs", ".snap",
)


def _excluded(rel: str, excludes, suffixes) -> bool:
    p = rel.replace("\\", "/")
    if any(seg in f"/{p}" for seg in (f"/{e.rstrip('/')}/" for e in excludes)):
        return True
    return any(p.endswith(s) for s in suffixes)


def iter_files(scope: Path, patterns: list[str], excludes=DEFAULT_EXCLUDES,
               suffixes=DEFAULT_EXCLUDE_SUFFIXES) -> list[Path]:
    """Every in-scope source file, in a stable order.

    Sorted by path so two runs over the same tree produce byte-identical batches.
    Without that the sweep would be reproducible in size but not in composition,
    and a finding could move between batches for no reason.
    """
    out: list[Path] = []
    for pat in patterns:
        for f in scope.rglob(pat):
            if not f.is_file():
                continue
            try:
                rel = f.relative_to(REPO_ROOT).as_posix()
            except ValueError:
                rel = f.as_posix()
            if _excluded(rel, excludes, suffixes):
                continue
            out.append(f)
    return sorted(set(out), key=lambda p: p.as_posix())


def plan_batches(files: list[Path], max_files: int = DEFAULT_MAX_FILES,
                 max_chars: int = DEFAULT_MAX_CHARS) -> list[dict]:
    """Group files into batches, keeping a directory together where it fits.

    Directory grouping is not cosmetic: a widget and the screen that uses it are
    usually siblings, and a rule like "this component is styled two ways" is only
    visible when both are in front of the reviewer at once. Splitting purely by
    size would scatter them arbitrarily.
    """
    by_dir: dict[str, list[Path]] = {}
    for f in files:
        by_dir.setdefault(f.parent.as_posix(), []).append(f)

    batches: list[dict] = []
    cur: list[Path] = []
    cur_chars = 0

    def flush():
        nonlocal cur, cur_chars
        if cur:
            def _rel(x: Path) -> str:
                try:
                    return x.relative_to(REPO_ROOT).as_posix()
                except ValueError:
                    return x.as_posix()
            batches.append({"index": len(batches) + 1,
                            "files": [_rel(p) for p in cur],
                            "chars": cur_chars})
            cur, cur_chars = [], 0

    for d in sorted(by_dir):
        for f in by_dir[d]:
            try:
                size = f.stat().st_size
            except OSError:
                size = 0
            # A single file over the cap still gets its own batch rather than
            # being dropped — skipping it would silently break coverage, which
            # is the one property this module exists to guarantee.
            if cur and (len(cur) >= max_files or cur_chars + size > max_chars):
                flush()
            cur.append(f)
            cur_chars += size
    flush()
    return batches


def build_plan(scope: str, patterns: list[str], max_files: int,
               max_chars: int) -> tuple[dict, int]:
    root = (REPO_ROOT / scope).resolve() if scope else REPO_ROOT
    if not root.exists():
        return {"error": f"scope not found: {scope}"}, 2
    files = iter_files(root, patterns)
    if not files:
        return {"error": f"no files under {scope} matching {patterns}"}, 2
    batches = plan_batches(files, max_files, max_chars)
    return {
        "scope": scope or ".",
        "patterns": patterns,
        "total_files": len(files),
        "total_chars": sum(b["chars"] for b in batches),
        "batches": batches,
    }, 0


def verify_sweep(plan: dict, batch_dir: Path) -> tuple[dict, int]:
    """Check a finished sweep actually covered what it planned.

    Three distinct failures, all of which look like a clean review otherwise:
    a batch that was never dispatched, a batch whose report was never written,
    and a file that no batch claimed.
    """
    expected = {b["index"] for b in plan["batches"]}
    present = set()
    for p in sorted(batch_dir.glob("b*.md")) if batch_dir.is_dir() else []:
        digits = "".join(ch for ch in p.stem if ch.isdigit())
        if digits:
            present.add(int(digits))
    missing = sorted(expected - present)
    planned_files = [f for b in plan["batches"] for f in b["files"]]
    dupes = sorted({f for f in planned_files if planned_files.count(f) > 1})
    covered = sorted({f for b in plan["batches"] if b["index"] in present
                      for f in b["files"]})
    rep = {
        "batches_planned": len(expected),
        "batches_reported": len(present & expected),
        "missing_batches": missing,
        "files_planned": len(planned_files),
        "files_covered": len(covered),
        "duplicated_files": dupes,
        "complete": not missing and not dupes and len(covered) == len(planned_files),
    }
    return rep, (0 if rep["complete"] else 1)


def main() -> None:
    ap = argparse.ArgumentParser(prog="sweep_plan.py")
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("plan", "verify"):
        s = sub.add_parser(name)
        s.add_argument("scope", nargs="?", default="")
        if name == "verify":
            s.add_argument("batch_dir")
        s.add_argument("--pattern", action="append", default=None,
                       help="glob to sweep, repeatable (default: *.dart, *.tsx, "
                            "*.ts, *.jsx, *.js, *.vue, *.svelte, *.html, *.css)")
        s.add_argument("--max-files", type=int, default=DEFAULT_MAX_FILES)
        s.add_argument("--max-chars", type=int, default=DEFAULT_MAX_CHARS)
        s.add_argument("--json", action="store_true")
    args = ap.parse_args()

    patterns = args.pattern or ["*.dart", "*.tsx", "*.ts", "*.jsx", "*.js",
                                "*.vue", "*.svelte", "*.html", "*.css"]
    plan, code = build_plan(args.scope, patterns, args.max_files, args.max_chars)
    if code != 0:
        print(json.dumps(plan, indent=2)); sys.exit(code)

    if args.cmd == "plan":
        if args.json:
            print(json.dumps(plan, indent=2))
        else:
            print(f"sweep: {plan['total_files']} files, "
                  f"{plan['total_chars'] // 1000}KB, {len(plan['batches'])} batches")
            for b in plan["batches"]:
                head = Path(b["files"][0]).parent
                try:
                    head = head.relative_to(REPO_ROOT).as_posix()
                except ValueError:
                    head = head.as_posix()
                print(f"  b{b['index']:02d}  {len(b['files']):2d} files  "
                      f"{b['chars'] // 1000:3d}KB  {head}")
        sys.exit(0)

    rep, code = verify_sweep(plan, Path(args.batch_dir))
    if args.json:
        print(json.dumps(rep, indent=2))
    else:
        print(f"sweep-verify: {rep['batches_reported']}/{rep['batches_planned']} "
              f"batches reported, {rep['files_covered']}/{rep['files_planned']} "
              f"files covered")
        if rep["missing_batches"]:
            print(f"  [FAIL] batches never reported: {rep['missing_batches']}")
            print("         Those files were never examined. The sweep's coverage "
                  "claim is void\n         until they are, and a partial sweep "
                  "reads exactly like a complete one.")
        if rep["duplicated_files"]:
            print(f"  [FAIL] files in more than one batch: "
                  f"{rep['duplicated_files'][:3]}")
        if rep["complete"]:
            print("  every planned file was covered by a reported batch")
    sys.exit(code)


if __name__ == "__main__":
    main()
