#!/usr/bin/env python3
"""Re-run the evidence a review cited for the items it cleared.

A reviewing agent's most damaging output is not a missed finding — it is a
checklist item reported as **cleared**. A miss leaves the reader still looking; a
clear tells them the item was checked and stops them. Requiring the agent to cite
evidence (see the agent specs) improves the format of a clear but cannot make it
true: an agent can cite a command it believes it ran, and report a count it
believes it saw.

So this verifies the citations mechanically. It knows nothing about the project,
the language, or what any rule means — it only checks that re-running the cited
evidence still produces what the review claimed:

  cmd form  `cleared by: \x60<command>\x60 -> <N> matches`
            Re-runs <command> and compares the line count to <N>.

  ref form  `cleared by: <path>:<line> "<snippet>"`
            Checks the file exists, has that line, and the snippet is there.

The same machinery checks a FINDING's site list, cited as `enumerated by:`. A
finding that says "three files" when the search returns twenty is not wrong about
the defect, but it is wrong about the scope — and a developer who fixes the three
listed sites reasonably believes they are done. Under-enumeration is the quieter
half of the same problem a false clear causes.

Sections are auto-detected: any heading matching "cleared" / "no issues" /
"dimensions with no" is treated as clear-bearing (so bullets there must cite
evidence), and `enumerated by:` citations are checked wherever they appear. That
keeps one invocation working for every agent — design-critic's "Sections cleared",
security-reviewer's "Categories cleared", architect's "Dimensions with no issues",
auditor's "No <type> issues found." — without each caller passing a heading.

Both forms are language-agnostic by construction — the checker never interprets
the command or the code, only whether the claim reproduces. That is what makes
this work on a Flutter app, a Django service, or a Rust CLI without configuration.

Observed failure this exists to catch (Snuggle, 2026-07-26): a review cleared
"brandGradient never sits under a white label" citing "enumerated all ~38 call
sites". The real count was 51, the two violations were among them, and the
command behind the count was `cd <dir> echo ...; grep ...` — a missing `&&` meant
the cd failed silently and the grep ran elsewhere. Re-running the citation
catches all three: wrong count, failed command, false clear.

Exit codes: 0 all citations reproduced (or nothing to check), 1 at least one did
not. Advisory by default — the caller decides whether a mismatch blocks.

Usage:
    verify_clears.py <findings.md> [--cwd DIR] [--checklist SPEC] [--json]
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Re-exported so callers and tests keep one import surface: this module is
# the entry point, parse/exec are its internals.
from verify_parse import (  # noqa: F401
    CHECKLIST_ITEM, CLEAR_HEADING, CMD_TAIL, COVERAGE_HEADING, INSPECTED,
    REF_TAIL, SITE_REF, UNVERIFIED, VERB_RE, checklist_items, coverage_rows,
    find_citation, parse_citations, parse_coverage_citations,
    parse_inline_citations, partial_inspections, scan_document, split_sections,
    declared_enumerations, undeclared_searches)
from verify_exec import (  # noqa: F401
    command_is_safe, split_pipeline, verify_cmd, verify_ref)

def verify(findings: Path, cwd: Path, checklist: Path | None = None,
           enumerations: Path | None = None, reviewer: str = "") -> dict:
    """Check every citation in the file.

    `checklist` is a spec whose `- [ ]` items the report must all account for.
    Sections are auto-detected. An earlier `--section` flag let a caller name one
    heading, but every agent's heading differs and no command ever passed it — it
    only ever offered a way to verify the wrong part of a report."""
    text = findings.read_text(encoding="utf-8", errors="replace")
    coverage = _check_coverage(text, checklist) if checklist else None
    # An improvised search is invisible in the report — every citation still
    # reproduces and the row still reads `24 of 24` — while silently redefining
    # which code the item was checked against. Only comparing against the
    # declaration catches it.
    undeclared = []
    if enumerations and reviewer and enumerations.is_file():
        undeclared = undeclared_searches(text, declared_enumerations(
            enumerations.read_text(encoding="utf-8", errors="replace"), reviewer))
    records = scan_document(text)
    partial = partial_inspections(text)
    if not records and not coverage and not partial and not undeclared:
        return {"findings": str(findings), "results": [],
                "coverage": coverage, "partial": [], "undeclared": [],
                "note": "no cleared sections and no citations found"}

    results = []
    for rec in records:
        if rec["kind"] == "cmd":
            results.append(verify_cmd(rec, cwd))
        elif rec["kind"] == "ref":
            results.append(verify_ref(rec, cwd))
        elif rec["kind"] == "unverified":
            results.append({**rec, "status": "unverified"})
        else:
            # Keep a detail the scanner already supplied (it says WHY this one
            # needed a citation); only fill in the generic reason otherwise.
            results.append({**rec, "status": "uncited",
                            "detail": rec.get("detail")
                            or "no re-runnable evidence cited"})
    return {"findings": str(findings), "results": results,
            "coverage": coverage, "partial": partial, "undeclared": undeclared}


def _check_coverage(text: str, checklist: Path) -> dict:
    """Compare the report's Coverage rows against the spec's checklist items."""
    items = checklist_items(checklist)
    rows = coverage_rows(text)
    if not items:
        return {"status": "no-checklist", "expected": 0, "rows": len(rows),
                "detail": f"no `- [ ]` items found in {checklist.name}"}
    if not rows:
        return {"status": "missing", "expected": len(items), "rows": 0,
                "detail": f"no `## Coverage` table; {len(items)} checklist "
                          "items unaccounted for"}
    if len(rows) < len(items):
        return {"status": "incomplete", "expected": len(items), "rows": len(rows),
                "detail": f"{len(items) - len(rows)} of {len(items)} checklist "
                          "items have no Coverage row"}
    return {"status": "complete", "expected": len(items), "rows": len(rows)}


# A clear that does not reproduce is worse than no clear, so these fail the run.
# `uncited` fails only for clears: a clear with no evidence is the bare-✓ case
# this module exists to stop, whereas a finding is allowed to be prose — it
# points at a defect rather than asserting one is absent.
FAILING = ("mismatch", "failed", "uncited")


def is_failing(r: dict) -> bool:
    # A Coverage row's search is a live claim about scope: if re-running it gives
    # a different count, the row's conclusion rests on a number that is no longer
    # true. Same weight as a clear that will not reproduce.
    if r.get("claim") == "coverage":
        return r["status"] in ("mismatch", "failed")
    if r["status"] == "uncited":
        # Clears always need evidence. Findings normally do not — prose is a
        # legitimate way to report a defect — except when the finding lists
        # several sites, where the missing search hides the denominator.
        return r.get("claim", "clear") == "clear" or r.get("requires_citation", False)
    return r["status"] in FAILING


def _print_coverage(cov: dict | None) -> None:
    if not cov:
        return
    if cov["status"] == "complete":
        print(f"  coverage: all {cov['expected']} checklist items accounted for")
        return
    mark = "FAIL" if cov["status"] in ("missing", "incomplete") else "note"
    print(f"  [{mark}] coverage    {cov['detail']}")
    if cov["status"] in ("missing", "incomplete"):
        print("         A rule the review never examined leaves no false claim "
              "behind, only\n         silence — which reads exactly like a rule "
              "that passed. Items with no\n         row are unreviewed, not clean.")


def _print_partial(rep: dict) -> None:
    if not rep.get("partial"):
        return
    print("  partially inspected (search returned more than was judged):")
    for p in rep["partial"]:
        print(f"    [note] {p['seen']} of {p['total']:<6} {p['item']}")
    print("           Findings from these rows are real, but their scope is not "
          "established —\n           the unread remainder may hold more of the same.")


def _print_undeclared(rep: dict) -> None:
    if not rep.get("undeclared"):
        return
    print("  searches that are not the declared enumeration:")
    for u in rep["undeclared"]:
        print(f"    [FAIL] item {u['index']:<3} {u['item']}")
        print(f"           declared: {u['declared']}")
        print(f"           used:     {u['used'] or '(none cited)'}")
    print("           A different search is a different population, so this run "
          "is not\n           comparable with the last and the item cannot be "
          "shown closed. Use the\n           declared command, or change the "
          "declaration deliberately.")


def _print(rep: dict) -> None:
    if rep.get("note"):
        print(f"verify-clears: {rep['note']}")
        _print_coverage(rep.get("coverage"))
        _print_partial(rep)
        _print_undeclared(rep)
        return
    order = {"mismatch": 0, "failed": 1, "uncited": 2, "unrunnable": 3,
             "stale": 4, "unverified": 5, "reproduced": 6}
    rows = sorted(rep["results"], key=lambda r: order.get(r["status"], 9))
    counts: dict[str, int] = {}
    for r in rows:
        counts[r["status"]] = counts.get(r["status"], 0) + 1

    print(f"verify-clears: {Path(rep['findings']).name} — "
          + "  ".join(f"{k}: {v}" for k, v in sorted(counts.items())))
    _print_coverage(rep.get("coverage"))
    _print_undeclared(rep)
    _print_partial(rep)
    for r in rows:
        if r["status"] == "reproduced":
            continue
        mark = "FAIL" if is_failing(r) else "note"
        tag = "finding" if r.get("claim") == "finding" else "clear"
        detail = r.get("detail", "")
        print(f"  [{mark}] {r['status']:<11} ({tag:<7}) {r['item'][:44]:<44} {detail}")

    bad_clear = [r for r in rows if is_failing(r) and r.get("claim") != "finding"]
    bad_find = [r for r in rows if is_failing(r) and r.get("claim") == "finding"]
    if bad_clear:
        print(f"\n  {len(bad_clear)} cleared item(s) did not reproduce. A clear "
              "that cannot be re-derived is\n  worse than no clear — it tells the "
              "reader the item was checked. Treat these as\n  unreviewed.")
    if bad_find:
        print(f"\n  {len(bad_find)} finding(s) have an unverifiable site list. "
              "The defect is likely real,\n  but its scope is not established — "
              "fixing only the sites listed may leave\n  others untouched. "
              "Re-derive the list before treating it as the work item.")


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(prog="verify_clears.py")
    ap.add_argument("findings", help="path to the review's findings.md")
    ap.add_argument("--cwd", default=".", help="project root to re-run against")
    ap.add_argument("--checklist", default=None,
                    help="spec file whose `- [ ]` items the report's Coverage "
                         "table must all account for")
    ap.add_argument("--enumerations", default=None,
                    help="docs/review/enumerations.md — the declared search per "
                         "checklist item; compared against the Coverage table")
    ap.add_argument("--reviewer", default="",
                    help="section of the enumerations file to compare against "
                         "(e.g. design-critic, security-reviewer, auditor)")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)

    f = Path(args.findings)
    if not f.is_file():
        print(f"verify-clears: no such file: {f}", file=sys.stderr)
        return 2
    checklist = Path(args.checklist) if args.checklist else None
    if checklist and not checklist.is_file():
        print(f"verify-clears: no such checklist: {checklist}", file=sys.stderr)
        return 2
    rep = verify(f, Path(args.cwd).resolve(), checklist,
                 Path(args.enumerations) if args.enumerations else None,
                 args.reviewer)
    if args.json:
        print(json.dumps(rep, indent=2))
    else:
        _print(rep)
    bad_coverage = (rep.get("coverage") or {}).get("status") in ("missing", "incomplete")
    return 1 if (bad_coverage or rep.get("undeclared")
                 or any(is_failing(r) for r in rep["results"])) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
