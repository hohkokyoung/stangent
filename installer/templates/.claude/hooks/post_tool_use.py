#!/usr/bin/env python3
"""JSONL logger for every tool call.

One line per tool call with:
  {ts, run_id, task_id, agent_role, tool, args, ok, res_chars, deny_reason?}

`args` is a compact dict of short, human-readable values (NOT type names).
Multi-line strings are flattened. Long strings are truncated mid-value with
an ellipsis so they stay JSONL-friendly.

`res_chars` is the size of the tool's RESULT — the thing that actually lands in
the agent's context and is then re-read on every subsequent turn. Agentic cost is
dominated by cache_read (context resident × turns), so without this the log can
say a run cost $8 but not which calls inflated it. Args alone don't tell you: a
one-line `grep -r` can return 200 lines. Chars, not tokens, because the hook
can't tokenize — divide by ~4 for a rough token estimate.

Log file path:
  .claude/state/logs/<run_id>.jsonl    when a workflow context is active
  .claude/state/logs/_no-run.jsonl     only if AGENTIC_LOG_AMBIENT=1 (opt-in)

`run_id` is the WORKFLOW/LOG CONTEXT — set by every agentic command around its
agent dispatch, not just /agentic-build: build → FEAT-NNN, reviews → SEC-/DR-/
UIR-/PR-, debug → DBG-, design → DS-, baseline tests → baseline-<flow>. This is
what makes logging uniform: any command that dispatches an agent writes
current_run.txt, so its tool calls are captured under that context. Ambient
tool use (no command active) has no context and is skipped by default.

run_id / task_id / agent_role / agent_model are read from state files written by
the command before each subagent call:
  .claude/state/current_run.txt   → run_id (workflow/log context)
  .claude/state/current_task.txt  → task_id
  .claude/state/current_role.txt  → agent_role
  .claude/state/current_model.txt → agent_model (the selected model after complexity routing)
Env vars AGENTIC_RUN_ID / AGENTIC_TASK_ID / AGENTIC_AGENT_ROLE / AGENTIC_AGENT_MODEL are
checked first as an override but are not set by any dispatcher in practice.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import re
import sys
from pathlib import Path

# Derive the repo root from THIS file's location, not the process cwd — the hook
# runs with an unreliable cwd, so a cwd-based path wrote state into whatever
# subdirectory happened to be current. __file__ is
# <repo>/.claude/hooks/post_tool_use.py → parents[2].
REPO_ROOT = Path(__file__).resolve().parents[2]
STATE_DIR = REPO_ROOT / ".claude" / "state"
LOG_DIR = STATE_DIR / "logs"

MAX_VALUE_LEN = 120
MAX_KEYS = 6
# A single log past this is rotated to `<name>.1.jsonl` so no log grows without
# bound (a long build's run log, or an opted-in ambient log).
MAX_LOG_BYTES = 5 * 1024 * 1024
# Tool-call counts at which a single task gets a `budget` warning in its run log.
# A task that keeps editing the same file re-reads every prior result on every
# turn, so cost grows superlinearly and nothing announces it: FEAT-025 t4 spent
# $16.40 — 34% of the whole run — across 361 turns and 160 Edits to one file,
# and no signal surfaced until the run was over. These thresholds do not block
# (a long task can be legitimate); they make it visible while it is happening.
BUDGET_WARN_CALLS = (150, 300, 500)
# Cumulative result bytes at which a single task gets a `budget` warning. This is
# the axis that tracks the bill: call count cannot separate a task making many
# cheap calls from one making many expensive ones. FEAT-025 t8 did 34 edits for
# $3.68 while t5 did 89 for $12.28 — because t5's edits each echoed a ~25 KB file
# back into context, where every later turn re-read it. Summing res_chars sees
# that; counting calls does not. Thresholds are set off that run: healthy tasks
# landed at 192k–463k chars, the three runaway migrations at 1503k/2175k/3523k.
BUDGET_WARN_RESULT_CHARS = (800_000, 1_500_000, 3_000_000)
# Normalized secret markers: matched as substrings of the separator-stripped,
# lowercased key, so `access_token`, `apiKey`, `x-api-key`, and
# `GITHUB_PERSONAL_ACCESS_TOKEN` all redact — not just the bare words. Kept
# specific (e.g. `apikey`/`privatekey`, never a bare `key`) so common,
# non-sensitive keys like `file_path` are not over-redacted.
SECRET_MARKERS = (
    "password", "passwd", "token", "secret", "apikey", "authorization",
    "bearer", "credential", "privatekey", "clientsecret", "sessionkey",
)


def _is_secret_key(key: str) -> bool:
    norm = re.sub(r"[^a-z0-9]", "", key.lower())
    return any(marker in norm for marker in SECRET_MARKERS)


def _short(value, key: str = "") -> object:
    """Return a JSON-safe short representation of `value`."""
    if value is None or isinstance(value, (bool, int, float)):
        return value
    if isinstance(value, str):
        if _is_secret_key(key):
            return "***"
        flat = re.sub(r"\s+", " ", value).strip()
        if len(flat) > MAX_VALUE_LEN:
            return flat[:MAX_VALUE_LEN - 1] + "…"
        return flat
    if isinstance(value, list):
        if not value:
            return []
        head = [_short(v, key) for v in value[:3]]
        if len(value) > 3:
            head.append(f"…(+{len(value) - 3})")
        return head
    if isinstance(value, dict):
        out = {}
        for k in list(value.keys())[:MAX_KEYS]:
            out[k] = _short(value[k], k)
        if len(value) > MAX_KEYS:
            out["…"] = f"+{len(value) - MAX_KEYS} keys"
        return out
    # fall back to safe repr
    return str(type(value).__name__)


def summarize(tool_input) -> dict:
    if not isinstance(tool_input, dict):
        return {}
    return {k: _short(v, k) for k, v in list(tool_input.items())[:MAX_KEYS]}


# Result keys carrying the payload an agent actually reads, in the shapes the
# different tools return. Checked in order; the first present one wins.
_RESULT_KEYS = ("content", "stdout", "stderr", "output", "result", "text",
                "file", "data")


def result_chars(tool_response) -> int:
    """Character count of what the tool put into the agent's context.

    Deliberately measures the RESULT, not our truncated `args` view — the whole
    point is to see the calls that returned far more than their command implies.
    Returns 0 for anything unmeasurable; this must never raise, since it runs on
    every tool call."""
    try:
        return _measure(tool_response)
    except Exception:
        return 0


def _measure(v) -> int:
    if v is None or isinstance(v, bool):
        return 0
    if isinstance(v, str):
        return len(v)
    if isinstance(v, (int, float)):
        return len(str(v))
    if isinstance(v, list):
        return sum(_measure(i) for i in v)
    if isinstance(v, dict):
        if not v:
            return 0  # `{}` would otherwise serialize to 2 chars of nothing
        # Sum every payload-bearing key rather than taking the first match: Bash
        # returns stdout AND stderr, and a command whose noise lands on stderr
        # costs the agent the same context as one that prints to stdout.
        present = [k for k in _RESULT_KEYS if k in v]
        if present:
            return sum(_measure(v[k]) for k in present)
        # Unknown shape (a tool we have no key for) — price the whole serialized
        # blob so it is not silently recorded as free.
        return len(json.dumps(v, ensure_ascii=False, default=str))
    return 0


# Field scanners for _budget_warning's re-read of the task log (see its docstring
# for why this is regex and not json.loads).
_RES_CHARS_RE = re.compile(r'"res_chars":\s*(\d+)')
_AXIS_RE = re.compile(r'"axis":\s*"(\w+)"')
_THRESHOLD_RE = re.compile(r'"threshold":\s*(\d+)')


def _ambient_logging_enabled() -> bool:
    return os.environ.get("AGENTIC_LOG_AMBIENT", "").strip().lower() in ("1", "true", "yes")


def _budget_warning(log: Path, task_id, run_id) -> list[dict]:
    """Emit `budget` events when a task crosses a call-count or result-bytes threshold.

    Two axes, because they catch different failures. Calls catch a task that will
    not terminate. Result bytes catch a task whose individual calls are expensive
    — the one that actually shows up on the bill (see BUDGET_WARN_RESULT_CHARS).

    Counts this task's own lines in the run log — cheap, and needs no extra state
    file to go stale. Emitted once per threshold PER AXIS: the event is itself the
    marker, so re-crossing does not re-warn. Dedup is on the highest threshold
    already warned, not on a count of warnings: calls only ever tick up by one, but
    res_chars can clear several thresholds in a single call (one 4 MB result), and
    a count-based check would then re-warn on every later call until the count
    caught up. Scanned with regex rather than json.loads because this runs on every
    tool call and re-reads the whole task log each time; parsing would make a long
    task quadratic in real time, not just in line count."""
    if not task_id:
        return []
    try:
        calls = res_chars = 0
        warned_max = {"calls": 0, "result_chars": 0}
        with log.open("r", encoding="utf-8", errors="replace") as f:
            for ln in f:
                if f'"task_id": "{task_id}"' not in ln:
                    continue
                if '"event": "budget"' in ln:
                    # Events written before the result_chars axis existed carry no
                    # "axis" key; they were always call-count warnings.
                    a = _AXIS_RE.search(ln)
                    axis = a.group(1) if a and a.group(1) in warned_max else "calls"
                    t = _THRESHOLD_RE.search(ln)
                    if t:
                        warned_max[axis] = max(warned_max[axis], int(t.group(1)))
                elif '"tool":' in ln:
                    calls += 1
                    m = _RES_CHARS_RE.search(ln)
                    if m:
                        res_chars += int(m.group(1))

        now = dt.datetime.now(dt.timezone.utc).isoformat(
            timespec="seconds").replace("+00:00", "Z")
        out = []
        for axis, value, thresholds, note in (
            ("calls", calls, BUDGET_WARN_CALLS,
             "task is long-running; check /agentic-logs for a single file "
             "being edited repeatedly before it compounds further"),
            ("result_chars", res_chars, BUDGET_WARN_RESULT_CHARS,
             "tool results are filling this task's context and every later turn "
             "re-reads them; if this is the same transformation applied site by "
             "site, script it instead of editing each site"),
        ):
            crossed = [t for t in thresholds if value >= t]
            if not crossed or crossed[-1] <= warned_max[axis]:
                continue
            out.append({
                "ts": now,
                "event": "budget",
                "axis": axis,
                "run_id": run_id,
                "task_id": task_id,
                "calls": calls,
                "res_chars": res_chars,
                "threshold": crossed[-1],
                "note": note,
            })
        return out
    except Exception:
        return []  # telemetry must never break a run


def _emit_budget_context(warnings: list[dict]) -> None:
    """Tell the agent running up the bill, not just the log.

    A PostToolUse hook that writes this JSON on stdout gets `additionalContext`
    injected as a system reminder beside the tool result, so the agent sees it on
    its next turn — while it still has calls left to change course. Writing the
    event to the run log alone made it forensics: nothing reads that log until
    someone runs /agentic-logs afterwards, which is precisely when it can no
    longer help. On FEAT-025 the bytes threshold is crossed at call 43 of 247.

    Deliberately never blocks. Blocking is exit 2, and a hook that halts real work
    on a heuristic is worse than the cost it saves — a long task can be
    legitimate, which is why the text says so.

    Emits NOTHING unless a threshold was crossed. This hook runs on every tool
    call; unconditional stdout would put a reminder beside every single result.
    """
    try:
        notes = []
        for w in warnings:
            if w.get("axis") == "result_chars":
                notes.append(
                    f"{(w.get('res_chars') or 0) // 1000}k characters of tool "
                    f"results are now in this task's context, over "
                    f"{w.get('calls')} calls. Every one is re-read on each later "
                    "turn, so cost grows superlinearly from here. If you are "
                    "applying one transformation site by site, stop and script it "
                    "(a single codemod run, then verify with the project's own "
                    "checks) instead of editing the remaining sites by hand.")
            else:
                notes.append(
                    f"This task has made {w.get('calls')} tool calls. If you are "
                    "repeating one edit across many sites, script it instead.")
        text = ("Agentic budget notice — " + " ".join(notes)
                + " If this task is genuinely long-running, carry on.")
        print(json.dumps({"hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            # The documented cap is 10k chars; this text is bounded by
            # construction (at most two fixed-shape notes), but clamp anyway
            # rather than rely on that staying true.
            "additionalContext": text[:9000],
        }}))
    except Exception:
        pass  # telemetry must never break a run


def _rotate_if_large(path: Path) -> None:
    """Keep any single log bounded: past MAX_LOG_BYTES, roll it to one prior
    generation (`<name>.1.jsonl`, overwriting an older roll) and start fresh."""
    try:
        if path.exists() and path.stat().st_size > MAX_LOG_BYTES:
            path.replace(path.parent / (path.stem + ".1.jsonl"))
    except OSError:
        pass


def main() -> None:
    raw = sys.stdin.buffer.read().decode("utf-8", errors="replace") or "{}"
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        sys.exit(0)

    tool = payload.get("tool_name") or payload.get("tool") or ""
    tool_input = payload.get("tool_input") or payload.get("input") or {}
    tool_response = payload.get("tool_response") or payload.get("output") or {}

    ok = True
    deny_reason = None
    error_text = None
    if isinstance(tool_response, dict):
        if tool_response.get("is_error") or tool_response.get("error"):
            ok = False
            # Capture a short reason so a failed run is diagnosable from the log,
            # not just "ok:false". Prefer an explicit error string; fall back to a
            # stringified content/response snippet.
            err = tool_response.get("error")
            if not isinstance(err, str):
                err = tool_response.get("content") or tool_response.get("stderr") or ""
            error_text = _short(err, "error") if err else None
        if "deny_reason" in tool_response:
            deny_reason = tool_response.get("deny_reason")

    # Kept inline (not shared via lib/common) on purpose: this hook fires on
    # EVERY tool call and its test runs it copied alone into a temp dir, so it
    # stays self-contained with no lib import to break.
    def _read_state(filename: str) -> str | None:
        p = STATE_DIR / filename
        if not p.exists():
            return None
        try:
            return p.read_text(encoding="utf-8").strip() or None
        except Exception:
            return None

    run_id = os.environ.get("AGENTIC_RUN_ID") or _read_state("current_run.txt")

    # Audit agentic work, not the whole project. Every agentic command sets a
    # workflow context (current_run.txt) around its dispatch, so a missing run_id
    # means no command is active — ambient general dev. Logging that made
    # `_no-run.jsonl` an unbounded record of ALL Claude Code use in the repo, so
    # skip it unless the user opts in with AGENTIC_LOG_AMBIENT=1.
    if not run_id and not _ambient_logging_enabled():
        sys.exit(0)

    task_id = os.environ.get("AGENTIC_TASK_ID") or _read_state("current_task.txt")
    agent_role = os.environ.get("AGENTIC_AGENT_ROLE") or _read_state("current_role.txt")
    agent_model = os.environ.get("AGENTIC_AGENT_MODEL") or _read_state("current_model.txt")
    line = {
        "ts": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "run_id": run_id,
        "task_id": task_id,
        "agent_role": agent_role,
        "model": agent_model,
        "tool": tool,
        "ok": ok,
        "args": summarize(tool_input),
        "res_chars": result_chars(tool_response),
    }
    if deny_reason:
        line["deny_reason"] = deny_reason
    if error_text:
        line["error"] = error_text

    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log_name = f"{run_id}.jsonl" if run_id else "_no-run.jsonl"
    out = LOG_DIR / log_name
    _rotate_if_large(out)
    warns: list[dict] = []
    with out.open("a", encoding="utf-8") as f:
        f.write(json.dumps(line, ensure_ascii=False) + "\n")
        f.flush()  # _budget_warning re-reads the file; the line above must be on disk
        warns = _budget_warning(out, task_id, run_id)
        for warn in warns:
            f.write(json.dumps(warn, ensure_ascii=False) + "\n")

    # Only after the log is closed, and only when something actually crossed.
    if warns:
        _emit_budget_context(warns)

    sys.exit(0)


if __name__ == "__main__":
    main()
