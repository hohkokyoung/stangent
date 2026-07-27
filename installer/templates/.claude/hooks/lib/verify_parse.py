#!/usr/bin/env python3
"""Turn a review report into checkable claim records. Pure text, no I/O.

Split out of verify_clears.py, which had grown to ~790 lines doing parsing,
sandboxed execution, coverage counting and reporting at once. The seam that
matters: nothing here touches the filesystem or runs anything, so a parsing
change cannot affect what gets executed, and these functions are trivially
testable against a string.

Three citation dialects live here because reviewers write in three shapes:
a bullet under a "cleared" heading, a `**Where:**` line inside a finding, and a
row in a Coverage table. They agree on the evidence forms and differ only in
where the claim sits.
"""
from __future__ import annotations

import re
from pathlib import Path

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
