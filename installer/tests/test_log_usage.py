#!/usr/bin/env python3
"""Tests for log_usage.py — the SubagentStop cost-attribution hook.

This module produces every per-task cost figure `/agentic-logs` reports, and it
had no tests: a silent error here does not break a run (the hook swallows
everything by contract), it just makes the numbers wrong, and nothing downstream
can tell. `token_cost.py` covers the pricing arithmetic; what is exercised here
is finding the right transcript, summing it, and attributing it to the right task.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
HOOK = REPO / "templates" / ".claude" / "hooks" / "log_usage.py"
LIB = REPO / "templates" / ".claude" / "hooks" / "lib"

_spec = importlib.util.spec_from_file_location("log_usage_mod", HOOK)
lu = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(lu)


def assistant(inp=0, out=0, cr=0, cw=0, model="claude-sonnet-4-6"):
    return {"type": "assistant", "message": {
        "model": model,
        "usage": {"input_tokens": inp, "output_tokens": out,
                  "cache_read_input_tokens": cr, "cache_creation_input_tokens": cw}}}


class SubagentUsageCase(unittest.TestCase):
    """Pure summing — no filesystem."""

    def test_sums_across_turns_and_keeps_model(self):
        tokens, model, turns = lu.subagent_usage([
            assistant(inp=1, out=2, cr=3, cw=4),
            assistant(inp=10, out=20, cr=30, cw=40),
        ])
        self.assertEqual(tokens, {"input": 11, "output": 22,
                                  "cache_read": 33, "cache_write": 44})
        self.assertEqual(model, "claude-sonnet-4-6")
        self.assertEqual(turns, 2)

    def test_ignores_non_assistant_and_usageless_records(self):
        tokens, _, turns = lu.subagent_usage([
            {"type": "user", "message": {"usage": {"input_tokens": 999}}},
            {"type": "assistant", "message": {}},           # no usage
            {"type": "system"},
            assistant(inp=5),
        ])
        self.assertEqual(turns, 1, "only the one assistant turn with usage counts")
        self.assertEqual(tokens["input"], 5)

    def test_empty_transcript_reports_zero_turns(self):
        self.assertEqual(lu.subagent_usage([])[2], 0)

    def test_last_model_wins_when_a_run_switches_model(self):
        _, model, _ = lu.subagent_usage([
            assistant(inp=1, model="claude-haiku-4-5-20251001"),
            assistant(inp=1, model="claude-opus-4-8"),
        ])
        self.assertEqual(model, "claude-opus-4-8")


class ResolveTranscriptCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        self.main_tx = self.root / "session.jsonl"
        self.main_tx.write_text("")
        self.subs = self.root / "session" / "subagents"
        self.subs.mkdir(parents=True)

    def tearDown(self):
        self._td.cleanup()

    def write_sub(self, name, mtime):
        p = self.subs / name
        p.write_text("{}\n")
        os.utime(p, (mtime, mtime))
        return p

    def test_picks_the_newest_subagent_file(self):
        # Dispatch is sequential, so the newest file is the one that just finished.
        self.write_sub("agent-old.jsonl", 1_000_000)
        newest = self.write_sub("agent-new.jsonl", 2_000_000)
        self.assertEqual(lu.resolve_subagent_transcript(str(self.main_tx)), newest)

    def test_path_already_inside_subagents_is_used_directly(self):
        p = self.write_sub("agent-1.jsonl", 1_000_000)
        self.assertEqual(lu.resolve_subagent_transcript(str(p)), p)

    def test_missing_subagents_dir_returns_none(self):
        other = self.root / "no-subdir.jsonl"
        other.write_text("")
        self.assertIsNone(lu.resolve_subagent_transcript(str(other)))

    def test_empty_subagents_dir_returns_none(self):
        self.assertIsNone(lu.resolve_subagent_transcript(str(self.main_tx)))


class HookCase(unittest.TestCase):
    """End-to-end: run the hook as the harness does, with a controlled repo root."""

    def run_hook(self, transcript, state=None, main_records=None, sub_records=None):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            hooks = root / ".claude" / "hooks"
            (hooks / "lib").mkdir(parents=True)
            shutil.copy(HOOK, hooks / HOOK.name)
            for mod in ("common.py", "token_cost.py"):
                shutil.copy(LIB / mod, hooks / "lib" / mod)
            state_dir = root / ".claude" / "state"
            state_dir.mkdir(parents=True)
            logs = state_dir / "logs"

            for name, text in (state or {}).items():
                if name.endswith(".jsonl"):
                    logs.mkdir(parents=True, exist_ok=True)
                    (logs / name).write_text(text)
                else:
                    (state_dir / name).write_text(text)

            tx = root / transcript
            tx.parent.mkdir(parents=True, exist_ok=True)
            tx.write_text("\n".join(json.dumps(r) for r in (main_records or [])) + "\n")
            if sub_records is not None:
                sd = tx.with_suffix("") / "subagents"
                sd.mkdir(parents=True, exist_ok=True)
                (sd / "agent-1.jsonl").write_text(
                    "\n".join(json.dumps(r) for r in sub_records) + "\n")

            subprocess.run(
                [sys.executable, str(hooks / HOOK.name)],
                input=json.dumps({"transcript_path": str(tx)}),
                cwd=str(root), capture_output=True, text=True)

            self.events = []
            if logs.is_dir():
                for f in logs.iterdir():
                    for ln in f.read_text().splitlines():
                        if ln.startswith("{"):
                            self.events.append(json.loads(ln))
            return [e for e in self.events if e.get("event") == "usage"]

    def test_writes_a_usage_event_with_correct_cost(self):
        # 1M of each at Sonnet rates (3 / 15 / 0.30 / 3.75 per 1M) = $22.05.
        # Asserting the money, not just that an event appeared: this number is
        # what every cost report ultimately shows.
        got = self.run_hook(
            "session.jsonl",
            state={"current_run.txt": "FEAT-001", "current_task.txt": "t4",
                   "current_role.txt": "implementer"},
            sub_records=[assistant(inp=1_000_000, out=1_000_000,
                                   cr=1_000_000, cw=1_000_000)])
        self.assertEqual(len(got), 1)
        e = got[0]
        self.assertEqual(e["run_id"], "FEAT-001")
        self.assertEqual(e["task_id"], "t4")
        self.assertEqual(e["agent_role"], "implementer")
        self.assertEqual(e["turns"], 1)
        self.assertAlmostEqual(e["cost_usd"], 22.05, places=2)

    def test_falls_back_to_sidechain_records_in_the_main_transcript(self):
        # Older/other harness versions inline subagent turns as isSidechain
        # instead of writing a subagents/ dir.
        rec = assistant(inp=100)
        rec["isSidechain"] = True
        got = self.run_hook("session.jsonl",
                            state={"current_run.txt": "FEAT-001",
                                   "current_task.txt": "t1",
                                   "current_role.txt": "implementer"},
                            main_records=[rec])
        self.assertEqual(len(got), 1)
        self.assertEqual(got[0]["tokens"]["input"], 100)

    def test_attributes_to_last_logged_task_when_state_was_cleared(self):
        # The FEAT-025 t9 case: SubagentStop can fire after the command removed
        # current_task/role, which stranded a whole task's cost under task_id=null.
        prior = json.dumps({"ts": "2026-07-27T00:00:00Z", "run_id": "FEAT-001",
                            "task_id": "t9", "agent_role": "implementer",
                            "tool": "Edit", "ok": True, "res_chars": 10})
        got = self.run_hook("session.jsonl",
                            state={"current_run.txt": "FEAT-001",
                                   "FEAT-001.jsonl": prior + "\n"},
                            sub_records=[assistant(inp=10)])
        self.assertEqual(len(got), 1)
        self.assertEqual(got[0]["task_id"], "t9")
        self.assertEqual(got[0]["agent_role"], "implementer")

    def test_no_run_context_writes_nothing(self):
        self.assertEqual(
            self.run_hook("session.jsonl", sub_records=[assistant(inp=10)]), [])

    def test_zero_turns_writes_nothing(self):
        # Nothing to attribute is not the same as a $0 task; no event at all.
        self.assertEqual(
            self.run_hook("session.jsonl",
                          state={"current_run.txt": "FEAT-001"},
                          sub_records=[{"type": "user"}]), [])

    def test_malformed_stdin_does_not_crash(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            hooks = root / ".claude" / "hooks"
            (hooks / "lib").mkdir(parents=True)
            shutil.copy(HOOK, hooks / HOOK.name)
            for mod in ("common.py", "token_cost.py"):
                shutil.copy(LIB / mod, hooks / "lib" / mod)
            r = subprocess.run([sys.executable, str(hooks / HOOK.name)],
                               input="not json", cwd=str(root),
                               capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, "telemetry must never break a run")


if __name__ == "__main__":
    unittest.main()
