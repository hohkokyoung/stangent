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
import re
import shlex
import subprocess
import sys
from pathlib import Path

# Commands allowed to be re-run. The citations come from a model-authored file,
# so this is an allowlist of read-only inspection tools, never a denylist — and
# argv[0] must match exactly after shlex parsing. Anything that writes, fetches,
# or installs is refused rather than run.
SAFE_COMMANDS = {
    "grep", "egrep", "fgrep", "rg", "ag", "ack",
    "find", "fd", "ls", "wc", "cat", "head", "tail", "sort", "uniq", "nl",
    "basename", "dirname", "stat", "file", "test", "true", "cut", "awk", "sed",
}
# `xargs <cmd>` runs <cmd>, so it is allowed only when what it runs is itself
# allowlisted — validated recursively rather than trusted.
PIPE_ONLY = {"xargs"}
# Shell metacharacters that chain, redirect, or substitute. A pipe is NOT here:
# `grep … | grep -v …` and `… | wc -l` are how these counts are naturally
# expressed, and every stage is validated independently, so a pipeline is no
# weaker than a single command. What stays banned is sequencing (`;`, `&&`) and
# anything that writes or substitutes — `cd X; grep …`, the construct behind the
# miscount this module exists to catch, is still refused.
SHELL_METACHARS = re.compile(r"[;&><`$\n]|\$\(")

MAX_OUTPUT_BYTES = 4 * 1024 * 1024
COMMAND_TIMEOUT_S = 30

# The citation verb: "cleared by" backs a clear, "enumerated by" backs a
# finding's site list. Both are verified the same way.
VERB_RE = re.compile(
    r"(?:cleared|enumerated|verified|confirmed)\s+(?:by|for|via)", re.IGNORECASE)

# The evidence itself, searched for AFTER the verb rather than bridged to it by a
# fixed pattern. Reviewers interleave backticked prose before the command —
# "enumerated by inspecting all 39 sites of `context.brandGradient` (denominator:
# `grep … | wc -l` -> 39 matches)" — so scanning forward for the span that is
# actually followed by a count is the only shape that survives real writing.
CMD_TAIL = re.compile(
    r"`(?P<cmd>[^`]+)`\s*(?:->|→)\s*"
    r"(?P<count>no|zero|\d+)\s*(?:match|matches|hit|hits|line|lines|site|sites|file|files)",
    re.IGNORECASE,
)
# `path/to/file.py:42 "snippet"` — the path may be wrapped in backticks (markdown
# habit) and the location may be a range (`:38-39`). A range checks the snippet
# anywhere inside it.
REF_TAIL = re.compile(
    r"`?(?P<path>[^\s:`]+)`?:(?P<line>\d+)(?:\s*-\s*(?P<end>\d+))?`?"
    r"\s*[\"“](?P<snippet>[^\"”]+)[\"”]",
)


def find_citation(text: str) -> dict | None:
    """The first verifiable citation in `text`, or None.

    Command form wins over ref form when both appear, since a count is the
    stronger claim."""
    v = VERB_RE.search(text)
    if not v:
        return None
    cmd = CMD_TAIL.search(text, v.end())
    if cmd:
        raw = cmd.group("count").lower()
        return {"kind": "cmd", "command": cmd.group("cmd").strip(),
                "claimed": 0 if raw in ("no", "zero") else int(raw)}
    ref = REF_TAIL.search(text, v.end())
    if ref:
        return {"kind": "ref", "path": ref.group("path"),
                "line": int(ref.group("line")),
                "end": int(ref.group("end")) if ref.group("end") else None,
                "snippet": ref.group("snippet")}
    return None
# Headings whose bullets are claims that something was checked and is fine.
CLEAR_HEADING = re.compile(
    r"cleared|no issues|dimensions with no|sections? clear|categories clear",
    re.IGNORECASE,
)
# An item explicitly not cleared — reported, never counted as a failure.
UNVERIFIED = re.compile(r"\bunverified\b", re.IGNORECASE)

BULLET = re.compile(r"^\s*[-*]\s+(?P<body>.+?)\s*$")


def _item_label(body: str) -> str:
    """Best-effort short name for the cleared item, for the report."""
    m = re.match(r"\*\*(?P<label>[^*]+)\*\*", body)
    if m:
        return m.group("label").strip()
    return body.split("—")[0].split(" - ")[0].strip()[:60] or "(unlabelled)"


HEADING = re.compile(r"^(?P<hashes>#{1,6})\s*(?P<title>.+?)\s*$")

# A markdown task-list item — how a project writes an enforcement checklist.
# Syntax only; the checker never reads what an item means.
CHECKLIST_ITEM = re.compile(r"^\s*[-*]\s*\[[ xX]?\]\s*(?P<text>.+?)\s*$")
COVERAGE_HEADING = re.compile(r"coverage", re.IGNORECASE)
# `12 of 38` in a Coverage row's inspected cell.
INSPECTED = re.compile(r"\b(?P<seen>\d+)\s*(?:of|/)\s*(?P<total>\d+)\b")


def checklist_items(path: Path) -> list[str]:
    """Every `- [ ]` item in a project's spec, in order."""
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    return [m.group("text") for m in
            (CHECKLIST_ITEM.match(l) for l in text.splitlines()) if m]


def coverage_rows(text: str) -> list[str]:
    """Non-header rows of the report's `## Coverage` table.

    Coverage is the one thing citations cannot establish: a rule the review
    never looked at leaves no false claim behind, just silence. Counting rows
    against the project's own checklist turns that silence into a failure."""
    rows = []
    for title, body in split_sections(text):
        if not COVERAGE_HEADING.search(title):
            continue
        for line in body.splitlines():
            s = line.strip()
            if not s.startswith("|"):
                continue
            cells = [c.strip() for c in s.strip("|").split("|")]
            # Skip the header and the |---|---| separator.
            if not cells or set("".join(cells)) <= set("-: "):
                continue
            if cells[0].lower() in ("#", "item", "no", "rule"):
                continue
            rows.append(s)
    return rows


def split_sections(text: str) -> list[tuple[str, str]]:
    """[(heading_title, body)] for the whole document, preamble under ''."""
    out: list[tuple[str, list[str]]] = [("", [])]
    for line in text.splitlines():
        m = HEADING.match(line)
        if m:
            out.append((m.group("title"), []))
        else:
            out[-1][1].append(line)
    return [(h, "\n".join(b)) for h, b in out]


def scan_document(text: str) -> list[dict]:
    """Every checkable citation in the file, tagged with its section.

    Two passes, because the two claim types live in different shapes: clears are
    bullets under a clear-bearing heading (so a bullet with NO citation is itself
    a finding — that is the bare "✓" case), while `enumerated by:` citations sit
    inside prose findings and are checked wherever they appear."""
    records: list[dict] = []
    for title, body in split_sections(text):
        if not body.strip():
            continue
        if COVERAGE_HEADING.search(title):
            # Inside a Coverage table a backticked command followed by a count is
            # unambiguously a claim — the row exists to assert it — so no
            # `cleared by:` verb is required. Most enumeration claims now live
            # here rather than in the cleared bullets, and without this they were
            # counted as rows but never re-run.
            records.extend({**rec, "section": title, "claim": "coverage"}
                           for rec in parse_coverage_citations(body))
        elif CLEAR_HEADING.search(title):
            for rec in parse_citations(body):
                records.append({**rec, "section": title, "claim": "clear"})
        else:
            # Not a clear section: only explicit citations are checked, and a
            # paragraph without one is simply prose, not a failure. Findings cite
            # inline ("**Where:** … enumerated by: `…` -> 3 sites"), not only in
            # bullets, so scan every line rather than requiring bullet syntax.
            cited = parse_inline_citations(body)
            for rec in cited:
                records.append({**rec, "section": title, "claim": "finding"})
            if not cited:
                records.extend(_multi_site_without_enumeration(title, body))
    return records


# A `**Where:**` line, plus the sites listed under it.
WHERE_LINE = re.compile(r"^\s*\*\*Where:?\*\*", re.IGNORECASE)
# `path/to/file.ext:123` — language-agnostic: any path with a line number. The
# stem must contain a letter and the extension must start with one, or ratios and
# versions written in prose ("4.5:1", "3.24:1") parse as file references and a
# finding gets flagged for sites it never listed.
SITE_REF = re.compile(
    r"(?:[\w./-]*[A-Za-z_][\w./-]*)\.[A-Za-z][A-Za-z0-9]{0,9}:\d+")


def _multi_site_without_enumeration(title: str, body: str) -> list[dict]:
    """A finding listing SEVERAL sites but citing no search that produced them.

    One site is a specific defect. Several are a claim about a class of
    occurrences — and the reader takes the list as the work item. Without the
    search behind it there is no denominator, so "3 sites" and "3 of 40 sites"
    are indistinguishable, and fixing the listed ones looks like finishing.

    Deliberately narrow: only fires on 2+ distinct file:line references in the
    same finding, so single-instance findings and prose stay untouched."""
    out = []
    for block in re.split(r"^#{1,6}\s.*$", body, flags=re.MULTILINE):
        where = [l for l in block.splitlines() if WHERE_LINE.match(l)]
        if not where:
            continue
        sites = {m.group(0) for m in SITE_REF.finditer(block)}
        if len(sites) >= 2:
            out.append({
                "kind": "uncited", "claim": "finding", "section": title,
                "item": _item_label(where[0]) or "Where",
                "raw": where[0].strip(), "status": "uncited",
                "requires_citation": True,
                "detail": f"lists {len(sites)} sites with no `enumerated by:` "
                          "search — scope unverifiable",
            })
    return out


def parse_coverage_citations(body: str) -> list[dict]:
    """Command-with-count claims in a Coverage table's rows.

    The row's own existence is the assertion, so the `cleared by:` verb is not
    needed here — requiring it would just add ceremony to a table cell."""
    out = []
    for line in body.splitlines():
        s = line.strip()
        if not s.startswith("|"):
            continue
        cells = [c.strip() for c in s.strip("|").split("|")]
        if not cells or set("".join(cells)) <= set("-: "):
            continue
        m = CMD_TAIL.search(s)
        if not m:
            continue
        raw = m.group("count").lower()
        # In a markdown table a literal pipe is written `\|`, so a piped command
        # arrives escaped. Unescape it or the pipeline parses as one command with
        # `|` as a filename argument — which silently greps far more than
        # intended and reports a wildly wrong count as a mismatch.
        command = m.group("cmd").strip().replace("\\|", "|")
        # The item name is the cell after the index; strip markdown code ticks so
        # a name containing `code` is not skipped in favour of the result cell.
        label = "coverage row"
        for c in cells[1:]:
            plain = c.replace("`", "").strip()
            if plain and not plain.isdigit():
                label = plain
                break
        out.append({"kind": "cmd", "raw": s, "item": label[:44],
                    "command": command,
                    "claimed": 0 if raw in ("no", "zero") else int(raw)})
    return out


def parse_inline_citations(body: str) -> list[dict]:
    """Citations anywhere in a block of prose, one record each."""
    out = []
    for line in body.splitlines():
        if not line.strip():
            continue
        cite = find_citation(line)
        if cite:
            out.append({**cite, "raw": line.strip(),
                        "item": _item_label(line.strip())})
    return out


def parse_citations(section: str) -> list[dict]:
    """One record per bullet in the cleared section.

    Bullets can wrap onto continuation lines, so accumulate until the next
    bullet starts. kind is 'cmd' | 'ref' | 'unverified' | 'uncited'."""
    items: list[dict] = []
    buf: list[str] = []

    def flush():
        if not buf:
            return
        body = " ".join(x.strip() for x in buf).strip()
        if not body:
            return
        rec = {"item": _item_label(body), "raw": body}
        cite = find_citation(body)
        if UNVERIFIED.search(body) and not cite:
            # An honest "I could not check this" is the outcome we want when the
            # agent cannot verify. Reported, never a failure.
            rec["kind"] = "unverified"
        elif cite:
            rec.update(cite)
        else:
            rec["kind"] = "uncited"
        items.append(rec)

    for line in section.splitlines():
        m = BULLET.match(line)
        if m:
            flush()
            buf = [m.group("body")]
        elif buf and line.strip():
            buf.append(line)
        elif not line.strip():
            flush()
            buf = []
    flush()
    return items


def _stage_is_safe(argv: list[str]) -> tuple[bool, str]:
    if not argv:
        return False, "empty command"
    exe = Path(argv[0]).name
    if exe in PIPE_ONLY:
        # xargs runs whatever follows it — validate that, not just xargs.
        rest = [a for a in argv[1:] if not a.startswith("-")]
        if not rest:
            return False, "xargs with no command to validate"
        return _stage_is_safe(rest)
    if exe not in SAFE_COMMANDS:
        return False, f"'{exe}' is not in the read-only command allowlist"
    return True, ""


def _split_top_level_pipes(command: str) -> list[str]:
    """Split on `|` only outside quotes.

    A naive `command.split("|")` corrupts any quoted regex alternation —
    `grep -E "circular\\((11|18)\\)"` is one command, not three stages."""
    parts, buf = [], []
    in_single = in_double = escaped = False
    for ch in command:
        if escaped:
            buf.append(ch)
            escaped = False
            continue
        if ch == "\\" and not in_single:
            buf.append(ch)
            escaped = True
        elif ch == "'" and not in_double:
            in_single = not in_single
            buf.append(ch)
        elif ch == '"' and not in_single:
            in_double = not in_double
            buf.append(ch)
        elif ch == "|" and not in_single and not in_double:
            parts.append("".join(buf))
            buf = []
        else:
            buf.append(ch)
    parts.append("".join(buf))
    return [p for p in parts if p.strip()]


def _shlex_lenient(part: str) -> list[str]:
    """shlex a stage, tolerating regex backslashes.

    POSIX shlex rejects `\\|` and `\\{` inside double quotes, but a real shell
    passes them through to grep untouched — and BRE alternation/repetition is
    written exactly that way. On failure, double the backslashes so posix mode
    yields the literal the shell would have produced."""
    try:
        return shlex.split(part)
    except ValueError:
        return shlex.split(part.replace("\\", "\\\\"))


def split_pipeline(command: str) -> list[list[str]]:
    """Split into pipeline stages and shlex each. Raises ValueError if unfixable."""
    return [_shlex_lenient(p) for p in _split_top_level_pipes(command)]


def command_is_safe(command: str) -> tuple[bool, str]:
    """Gate a cited command before re-running it. Refuse rather than guess."""
    if SHELL_METACHARS.search(command):
        return False, ("contains shell metacharacters — a citation may pipe "
                       "between read-only commands but must not chain, "
                       "redirect, or substitute")
    try:
        stages = split_pipeline(command)
    except ValueError as e:
        return False, f"unparseable: {e}"
    if not stages:
        return False, "empty command"
    for argv in stages:
        ok, why = _stage_is_safe(argv)
        if not ok:
            return False, why
    return True, ""


def _count_lines(out: str) -> int:
    return sum(1 for ln in out.splitlines() if ln.strip())


# Commands whose output IS the number, rather than a list to be counted.
# `grep -rn … | wc -l` prints one line reading "0" — counting its lines gives 1
# and turns a true "no matches" clear into a false mismatch, which is exactly as
# damaging as missing a real one.
_COUNTING_TAIL = re.compile(r"^\s*(?:wc\s+(?:-[lwm]\s*)+|grep\s+.*-[A-Za-z]*c)\b")


def _result_value(command: str, stdout: str) -> int:
    """How many things the citation found: the printed number for a counting
    command, otherwise the number of output lines."""
    last = _split_top_level_pipes(command)[-1] if command else ""
    if _COUNTING_TAIL.search(last):
        nums = re.findall(r"-?\d+", stdout)
        if nums:
            # `wc -l` over several files prints a per-file table plus a total;
            # the last number is the total in that case and the only number in
            # the common one.
            return int(nums[-1])
        return 0
    return _count_lines(stdout)


class _Piped:
    """Minimal stand-in for CompletedProcess from a hand-built pipeline."""
    def __init__(self, stdout: str, stderr: str, returncode: int):
        self.stdout, self.stderr, self.returncode = stdout, stderr, returncode


def _run_pipeline(stages: list[list[str]], cwd: Path) -> _Piped:
    """Run `a | b | c` without a shell, so no metacharacter can be reinterpreted.

    The exit status reported is the LAST stage's, matching shell semantics — but
    an earlier stage dying is what a broken citation looks like, so a non-zero
    upstream status is surfaced through stderr rather than swallowed."""
    procs: list[subprocess.Popen] = []
    prev_stdout = None
    try:
        for argv in stages:
            p = subprocess.Popen(argv, cwd=str(cwd), stdin=prev_stdout,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                 text=True)
            if prev_stdout is not None:
                prev_stdout.close()  # let the upstream stage see EPIPE
            prev_stdout = p.stdout
            procs.append(p)
        out, err = procs[-1].communicate(timeout=COMMAND_TIMEOUT_S)
    except BaseException:
        for p in procs:
            p.kill()
        raise
    upstream_err = []
    for p in procs[:-1]:
        p.wait(timeout=5)
        e = (p.stderr.read() or "").strip() if p.stderr else ""
        if p.returncode and p.returncode > 1 and e:
            upstream_err.append(f"stage '{p.args[0]}' exit {p.returncode}: {e}")
    for p in procs:
        for s in (p.stderr,):
            if s and not s.closed:
                s.close()
    return _Piped(out, "\n".join(upstream_err) or (err or ""),
                  procs[-1].returncode if not upstream_err else 2)


def verify_cmd(rec: dict, cwd: Path) -> dict:
    ok, why = command_is_safe(rec["command"])
    if not ok:
        return {**rec, "status": "unrunnable", "detail": why}
    try:
        p = _run_pipeline(split_pipeline(rec["command"]), cwd)
    except subprocess.TimeoutExpired:
        return {**rec, "status": "unrunnable",
                "detail": f"timed out after {COMMAND_TIMEOUT_S}s"}
    except (OSError, ValueError) as e:
        return {**rec, "status": "unrunnable", "detail": str(e)}

    # grep-likes exit 1 for "no matches" — a legitimate zero, not an error.
    # Anything above that is a real failure, which is exactly what a silently
    # broken citation (bad path, failed cd) looks like.
    if p.returncode > 1:
        detail = (p.stderr or "").strip().splitlines()
        return {**rec, "status": "failed",
                "detail": f"exit {p.returncode}: {detail[0] if detail else 'no stderr'}"}
    if len(p.stdout) > MAX_OUTPUT_BYTES:
        return {**rec, "status": "unrunnable", "detail": "output too large"}

    actual = _result_value(rec["command"], p.stdout)
    if actual != rec["claimed"]:
        return {**rec, "status": "mismatch", "actual": actual,
                "detail": f"claimed {rec['claimed']}, re-running gives {actual}"}
    return {**rec, "status": "reproduced", "actual": actual}


def verify_ref(rec: dict, cwd: Path) -> dict:
    p = (cwd / rec["path"]).resolve()
    # Keep the check inside the project — a citation should never send us out.
    try:
        p.relative_to(cwd.resolve())
    except ValueError:
        return {**rec, "status": "unrunnable", "detail": "path escapes the project"}
    if not p.is_file():
        return {**rec, "status": "failed", "detail": f"no such file: {rec['path']}"}
    try:
        lines = p.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as e:
        return {**rec, "status": "unrunnable", "detail": str(e)}
    if rec["line"] > len(lines):
        return {**rec, "status": "mismatch",
                "detail": f"file has {len(lines)} lines, citation points at {rec['line']}"}

    # Agents write the snippet as markdown code — `"`code`"` — so the backticks
    # are part of the quoted string but not part of the file. Strip them, or
    # every correctly-cited snippet fails to match.
    want = " ".join(rec["snippet"].replace("`", " ").split())
    # An elided snippet — "default: ... return ThemeMode.system;" — is a normal
    # way to cite two anchors around a comment block. Require every part present
    # IN ORDER inside the cited range: still a real check, since the range bounds
    # how far apart the anchors may sit.
    parts = [p for p in (s.strip() for s in re.split(r"\.\.\.|…", want)) if p]
    # Allow a small drift window: code moves, and a citation off by a couple of
    # lines is stale, not fabricated. Absent entirely is the real signal. A cited
    # RANGE (`:38-39`) widens the window to cover it.
    end = rec.get("end") or rec["line"]
    # A cited range is quoted as one string even though the code spans several
    # lines ("width: tapTarget, height: tapTarget," is two lines in the file), so
    # match against the range's joined text, not line by line.
    def _all_in_order(hay: str) -> bool:
        pos = 0
        for p in parts:
            i = hay.find(p, pos)
            if i < 0:
                return False
            pos = i + len(p)
        return True

    if end > rec["line"]:
        joined = " ".join(" ".join(l.split())
                          for l in lines[rec["line"] - 1:end])
        if _all_in_order(joined):
            return {**rec, "status": "reproduced"}

    lo, hi = max(0, rec["line"] - 4), min(len(lines), end + 3)
    for i in range(lo, hi):
        if want in " ".join(lines[i].split()):
            in_range = rec["line"] <= i + 1 <= end
            status = "reproduced" if in_range else "stale"
            detail = "" if in_range else \
                f"found at line {i + 1}, cited as {rec['line']}"
            return {**rec, "status": status, "detail": detail}
    # Last chance: a snippet spanning lines just outside the cited range.
    wlo, whi = max(0, rec["line"] - 3), min(len(lines), end + 3)
    window = " ".join(" ".join(l.split()) for l in lines[wlo:whi])
    if _all_in_order(window):
        return {**rec, "status": "stale",
                "detail": f"found near, but not within, lines {rec['line']}-{end}"}
    return {**rec, "status": "mismatch",
            "detail": f"snippet not found near line {rec['line']}"}


def verify(findings: Path, cwd: Path, checklist: Path | None = None) -> dict:
    """Check every citation in the file.

    `checklist` is a spec whose `- [ ]` items the report must all account for.
    Sections are auto-detected. An earlier `--section` flag let a caller name one
    heading, but every agent's heading differs and no command ever passed it — it
    only ever offered a way to verify the wrong part of a report."""
    text = findings.read_text(encoding="utf-8", errors="replace")
    coverage = _check_coverage(text, checklist) if checklist else None
    records = scan_document(text)
    partial = partial_inspections(text)
    if not records and not coverage and not partial:
        return {"findings": str(findings), "results": [],
                "coverage": coverage, "partial": [],
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
            "coverage": coverage, "partial": partial}


def partial_inspections(text: str) -> list[dict]:
    """Coverage rows whose `inspected` cell reads fewer than the search returned.

    A search returning 38 and a reading of 12 finds whatever is in those 12 and
    reports it in a row shaped exactly like a full sweep. The count is honest;
    the impression is not. Surfaced as a note, never a failure — penalising the
    gap would just push the number up, the same way penalising `unverified`
    would push reviewers back to inventing clears."""
    out = []
    for title, body in split_sections(text):
        if not COVERAGE_HEADING.search(title):
            continue
        for line in body.splitlines():
            s = line.strip()
            if not s.startswith("|"):
                continue
            cells = [c.strip() for c in s.strip("|").split("|")]
            for c in cells:
                m = INSPECTED.search(c)
                if not m:
                    continue
                seen, total = int(m.group("seen")), int(m.group("total"))
                if seen < total:
                    label = next((x for x in cells if x and not x.isdigit()), "?")
                    out.append({"item": label[:44], "seen": seen, "total": total})
                break
    return out


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


def _print(rep: dict) -> None:
    if rep.get("note"):
        print(f"verify-clears: {rep['note']}")
        _print_coverage(rep.get("coverage"))
        _print_partial(rep)
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
    rep = verify(f, Path(args.cwd).resolve(), checklist)
    if args.json:
        print(json.dumps(rep, indent=2))
    else:
        _print(rep)
    bad_coverage = (rep.get("coverage") or {}).get("status") in ("missing", "incomplete")
    return 1 if (bad_coverage or any(is_failing(r) for r in rep["results"])) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
