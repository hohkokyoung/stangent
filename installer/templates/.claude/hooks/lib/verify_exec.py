#!/usr/bin/env python3
"""Re-run a cited command, or re-read a cited file:line. All the I/O lives here.

Citations come from a model-authored report, so re-running them executes
untrusted content. Everything that touches the filesystem or spawns a process is
in this one module, behind an allowlist: argv[0] of every pipeline stage must be
a read-only inspection tool, and sequencing/redirection/substitution are refused
rather than sanitised.

Split out of verify_clears.py so the dangerous surface is small enough to audit
in one sitting, and so a parsing change cannot widen it.
"""
from __future__ import annotations

import re
import shlex
import subprocess
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
