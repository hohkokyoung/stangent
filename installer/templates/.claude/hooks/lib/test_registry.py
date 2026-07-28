#!/usr/bin/env python3
"""The regression registry: read, validate and update `.claude/tests/cases/`.

One case is one file, `TC-NNN-slug.md`, with YAML frontmatter. There is no
central manifest on purpose. A manifest would be a second source of truth that
has to agree with the files, and it is the file every branch touches — two
features adding a case is a guaranteed conflict in a shared list and never a
conflict in two separate files. `index` regenerates a human-readable INDEX.md
*from* the cases; nothing reads it back.

The registry is deliberately runner-agnostic. A case says what command to run
and what the run must produce; it does not say whether that command is
`pytest`, `flutter-skill`, `go test` or a shell script. That is what lets the
same regression gate sit over a Flutter app and a Python service.

Usage:
    python test_registry.py next-id
    python test_registry.py list [--status active] [--runner X] [--surface Y]
    python test_registry.py validate
    python test_registry.py record TC-001 --result pass [--commit SHA]
                                          [--evidence a.png,b.png] [--note ...]
    python test_registry.py regressions
    python test_registry.py index
"""
from __future__ import annotations

import argparse
import datetime
import json
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from id_alloc import fmt, existing  # noqa: E402

try:
    import yaml  # type: ignore
except Exception:  # pragma: no cover - reported by doctor's dep check
    yaml = None

REPO_ROOT = Path.cwd().resolve()
TESTS_DIR = REPO_ROOT / ".claude" / "tests"
CASES_DIR = TESTS_DIR / "cases"

PREFIX = "TC"
PAD = 3
START = 1
SHAPE = {"files": True, "slug": True}

KINDS = ("happy", "boundary", "failure", "regression")
SURFACES = ("e2e-mobile", "e2e-web", "e2e-desktop", "api", "integration", "unit")
STATUSES = ("active", "quarantined", "retired")
RESULTS = ("pass", "fail", "error", "skipped")

# Kept short on purpose: a long history is churn in every diff, and anything
# older than this is in git anyway. Enough entries to see a flap, not a log.
HISTORY_CAP = 10

FRONTMATTER_RE = re.compile(r"\A---\n(.*?)\n---\n?(.*)\Z", re.DOTALL)

# Command shapes that make a rerun non-reproducible. Checked as literals against
# the recorded command, because the point is to catch a case that bakes in
# "now" or "random" rather than pinning it in `fixtures`.
NONDETERMINISTIC = ("$(date", "`date", "uuidgen", "$RANDOM", "%RANDOM%", "$(openssl rand")
# An absolute path into somebody's home directory runs on exactly one machine.
MACHINE_LOCAL = ("/Users/", "/home/", "C:\\Users\\")


# --------------------------------------------------------------------------
# reading


def parse_case(path: Path) -> dict:
    """`{...frontmatter, _path, _body}`; raises ValueError on a malformed file."""
    text = path.read_text(encoding="utf-8")
    m = FRONTMATTER_RE.match(text)
    if not m:
        raise ValueError(f"{path.name}: no YAML frontmatter (file must start with '---')")
    if yaml is None:
        raise ValueError("pyyaml is not installed — the registry cannot be read")
    try:
        data = yaml.safe_load(m.group(1)) or {}
    except Exception as e:
        raise ValueError(f"{path.name}: frontmatter is not valid YAML: {e}") from e
    if not isinstance(data, dict):
        raise ValueError(f"{path.name}: frontmatter must be a mapping")
    data["_path"] = str(path.relative_to(REPO_ROOT))
    data["_body"] = m.group(2)
    return data


def load_all() -> tuple[list[dict], list[str]]:
    """Every case, ascending by id, plus the errors of files that would not parse.

    Unreadable cases are returned rather than raised: one broken file must not
    make the whole gate unrunnable, but it must also never be silently dropped
    from the count — a case that vanishes reads exactly like a case that passed.
    """
    cases: list[dict] = []
    errors: list[str] = []
    if not CASES_DIR.is_dir():
        return cases, errors
    for _, stem in existing(CASES_DIR, PREFIX, **SHAPE):
        p = CASES_DIR / f"{stem}.md"
        try:
            cases.append(parse_case(p))
        except ValueError as e:
            errors.append(str(e))
    return cases, errors


def next_id() -> str:
    ex = existing(CASES_DIR, PREFIX, **SHAPE)
    return fmt((ex[-1][0] + 1) if ex else START, PREFIX, PAD)


def find_case(case_id: str) -> dict | None:
    cases, _ = load_all()
    return next((c for c in cases if str(c.get("id", "")).upper() == case_id.upper()), None)


# --------------------------------------------------------------------------
# validation


def validate_case(c: dict) -> list[str]:
    """Schema plus the determinism contract. Returns human-readable problems."""
    p = c.get("_path", "?")
    out: list[str] = []

    def bad(msg: str) -> None:
        out.append(f"{p}: {msg}")

    for field in ("id", "title", "kind", "surface", "runner", "status", "command"):
        if not str(c.get(field) or "").strip():
            bad(f"missing required field '{field}'")

    if c.get("kind") and c["kind"] not in KINDS:
        bad(f"kind={c['kind']!r} — must be one of {', '.join(KINDS)}")
    if c.get("surface") and c["surface"] not in SURFACES:
        bad(f"surface={c['surface']!r} — must be one of {', '.join(SURFACES)}")
    if c.get("status") and c["status"] not in STATUSES:
        bad(f"status={c['status']!r} — must be one of {', '.join(STATUSES)}")

    stem = Path(c.get("_path", "")).stem
    if c.get("id") and not stem.startswith(str(c["id"])):
        bad(f"id {c['id']} does not match its filename — rename the file or fix the id")

    cmd = str(c.get("command") or "")
    for marker in NONDETERMINISTIC:
        if marker in cmd:
            bad(f"command embeds {marker!r} — a rerun cannot reproduce it. Pin the "
                f"value and describe it in `fixtures` instead.")
    for marker in MACHINE_LOCAL:
        if marker in cmd:
            bad(f"command contains the machine-local path {marker!r} — it will not "
                f"run on another checkout. Use a repo-relative path.")

    expect = c.get("expect")
    if not isinstance(expect, dict):
        bad("missing `expect:` — a case with nothing to compare against cannot fail, "
            "so it cannot catch a regression")
    else:
        has_code = "exit_code" in expect
        asserts = expect.get("assertions") or []
        if not has_code and not asserts:
            bad("`expect` must carry at least one of `exit_code` or `assertions`")
        if asserts and not isinstance(asserts, list):
            bad("`expect.assertions` must be a list")

    if c.get("status") == "quarantined" and not str(c.get("flake_reason") or "").strip():
        bad("status=quarantined requires `flake_reason` — a case excluded from the "
            "gate without a stated reason is an untracked coverage hole")

    last = c.get("last_run")
    if isinstance(last, dict) and last.get("result") not in RESULTS:
        bad(f"last_run.result={last.get('result')!r} — must be one of {', '.join(RESULTS)}")

    return out


def validate_all() -> tuple[list[str], int]:
    cases, errors = load_all()
    problems = list(errors)
    seen: dict[str, str] = {}
    for c in cases:
        problems += validate_case(c)
        cid = str(c.get("id") or "")
        if cid in seen:
            problems.append(f"duplicate id {cid}: {seen[cid]} and {c['_path']}")
        elif cid:
            seen[cid] = c["_path"]
    return problems, len(cases)


# --------------------------------------------------------------------------
# regressions


def is_regression(c: dict) -> bool:
    """True when this case passed at some point and does not pass now.

    A case that has never passed is a known gap, not a regression — it goes in
    the report under its own heading so a red bar is not read as "something
    broke today" when it was never green.
    """
    last = c.get("last_run")
    if not isinstance(last, dict) or last.get("result") == "pass":
        return False
    history = c.get("history") or []
    return any(isinstance(h, dict) and h.get("result") == "pass" for h in history)


# --------------------------------------------------------------------------
# writing


def _now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def _head_commit() -> str:
    try:
        r = subprocess.run(["git", "rev-parse", "--short", "HEAD"],
                           capture_output=True, text=True, timeout=10, cwd=REPO_ROOT)
        return r.stdout.strip() if r.returncode == 0 else ""
    except Exception:
        return ""


def record(case_id: str, result: str, commit: str = "", evidence: list[str] | None = None,
           note: str = "") -> str:
    """Write one run outcome onto a case. Only `last_run`/`history` are touched.

    `expect` is never rewritten here. Recording is what a run *did*; changing
    what a run *must* do is a deliberate edit with a revision bump, so an
    accidental green can never come from the same code path that reports one.
    """
    if result not in RESULTS:
        raise SystemExit(f"result must be one of {', '.join(RESULTS)}")
    c = find_case(case_id)
    if c is None:
        raise SystemExit(f"no such case: {case_id}")
    if yaml is None:
        raise SystemExit("pyyaml is not installed — cannot write the registry")

    rel = c["_path"]
    path = REPO_ROOT / rel
    body = c.pop("_body")
    c.pop("_path")

    prev = c.get("last_run")
    history = [h for h in (c.get("history") or []) if isinstance(h, dict)]
    if isinstance(prev, dict):
        history.insert(0, {k: prev[k] for k in ("at", "result", "commit") if k in prev})

    entry: dict = {"at": _now(), "result": result, "commit": commit or _head_commit()}
    if evidence:
        entry["evidence"] = evidence
    if note:
        entry["note"] = note
    c["last_run"] = entry
    c["history"] = history[:HISTORY_CAP]

    fm = yaml.dump(c, default_flow_style=False, allow_unicode=True, sort_keys=False)
    path.write_text(f"---\n{fm}---\n{body}", encoding="utf-8")
    return rel


def write_index() -> str:
    cases, errors = load_all()
    lines = [
        "# Test case index",
        "",
        "<!-- GENERATED by test_registry.py index — edit the case files, not this. -->",
        "",
        "| id | title | kind | surface | runner | status | last run |",
        "|---|---|---|---|---|---|---|",
    ]
    for c in cases:
        last = c.get("last_run") if isinstance(c.get("last_run"), dict) else {}
        mark = f"{last.get('result', '—')} ({last.get('at', '')[:10]})" if last else "never run"
        lines.append(
            f"| [{c.get('id', '?')}](cases/{Path(c['_path']).name})"
            f" | {c.get('title', '')} | {c.get('kind', '')} | {c.get('surface', '')}"
            f" | {c.get('runner', '')} | {c.get('status', '')} | {mark} |"
        )
    if not cases:
        lines.append("| — | no cases registered yet | | | | | |")
    if errors:
        lines += ["", "## Unreadable case files", ""] + [f"- {e}" for e in errors]
    lines.append("")
    TESTS_DIR.mkdir(parents=True, exist_ok=True)
    out = TESTS_DIR / "INDEX.md"
    out.write_text("\n".join(lines), encoding="utf-8")
    return str(out.relative_to(REPO_ROOT))


# --------------------------------------------------------------------------
# CLI


def main() -> None:
    ap = argparse.ArgumentParser(prog="test_registry.py")
    sub = ap.add_subparsers(dest="cmd", required=True)

    sub.add_parser("next-id")
    sub.add_parser("validate")
    sub.add_parser("regressions")
    sub.add_parser("index")

    p_list = sub.add_parser("list")
    p_list.add_argument("--status", default="")
    p_list.add_argument("--runner", default="")
    p_list.add_argument("--surface", default="")

    p_rec = sub.add_parser("record")
    p_rec.add_argument("case_id")
    p_rec.add_argument("--result", required=True)
    p_rec.add_argument("--commit", default="")
    p_rec.add_argument("--evidence", default="")
    p_rec.add_argument("--note", default="")

    a = ap.parse_args()

    if a.cmd == "next-id":
        print(next_id())

    elif a.cmd == "list":
        cases, errors = load_all()
        for c in cases:
            if a.status and c.get("status") != a.status:
                continue
            if a.runner and c.get("runner") != a.runner:
                continue
            if a.surface and c.get("surface") != a.surface:
                continue
            c.pop("_body", None)
            print(json.dumps(c))
        for e in errors:
            sys.stderr.write(f"[registry] unreadable: {e}\n")

    elif a.cmd == "validate":
        problems, n = validate_all()
        for p in problems:
            print(f"FAIL {p}")
        print(f"[registry] {n} case(s), {len(problems)} problem(s)")
        sys.exit(1 if problems else 0)

    elif a.cmd == "regressions":
        cases, _ = load_all()
        active = [c for c in cases if c.get("status") == "active"]
        regressed = [c for c in active if is_regression(c)]
        never = [c for c in active
                 if not is_regression(c)
                 and (c.get("last_run") or {}).get("result") not in (None, "pass")]
        for c in regressed:
            print(f"REGRESSION {c['id']} {c['title']} — was passing, now "
                  f"{(c.get('last_run') or {}).get('result')}")
        for c in never:
            print(f"FAILING {c['id']} {c['title']} — has never passed")
        print(f"[registry] {len(active)} active, {len(regressed)} regression(s), "
              f"{len(never)} never-passing")
        sys.exit(1 if regressed else 0)

    elif a.cmd == "record":
        ev = [e.strip() for e in a.evidence.split(",") if e.strip()]
        path = record(a.case_id, a.result, a.commit, ev, a.note)
        print(f"[registry] {a.case_id} -> {a.result} ({path})")

    elif a.cmd == "index":
        print(f"[registry] wrote {write_index()}")


if __name__ == "__main__":
    main()
