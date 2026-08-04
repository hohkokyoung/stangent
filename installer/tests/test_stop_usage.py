#!/usr/bin/env python3
"""Tests for stop_usage.py — the Stop hook attributing the MAIN session's cost.

The orchestrator's own thread was previously unlogged, so `/agentic-logs` showed
subagent cost only and read as a complete bill. This hook closes that, and its one
real hazard is arithmetic: Stop fires at the end of EVERY main-agent response, so
a hook that re-sums the transcript each time inflates the very number it exists to
report. The cursor is what most of this module exercises.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
HOOK = REPO / "templates" / ".claude" / "hooks" / "stop_usage.py"
LIB = REPO / "templates" / ".claude" / "hooks" / "lib"

_spec = importlib.util.spec_from_file_location("stop_usage_mod", HOOK)
su = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(su)


def assistant(inp=0, out=0, cr=0, cw=0, model="claude-sonnet-4-6", sidechain=False):
    r = {"type": "assistant", "message": {
        "model": model,
        "usage": {"input_tokens": inp, "output_tokens": out,
                  "cache_read_input_tokens": cr, "cache_creation_input_tokens": cw}}}
    if sidechain:
        r["isSidechain"] = True
    return r


class MainTurnsCase(unittest.TestCase):
    """Pure selection — no filesystem."""

    def test_excludes_sidechain_records(self):
        # Some Claude Code versions inline subagent turns into the main transcript
        # as isSidechain; log_usage.py already bills those. Counting them here too
        # would double-charge every dispatched agent.
        turns = su.main_turns([
            assistant(inp=1),
            assistant(inp=999, sidechain=True),
            assistant(inp=2),
        ])
        self.assertEqual(len(turns), 2)
        self.assertEqual(su.sum_usage(turns)[0]["input"], 3)

    def test_ignores_non_assistant_and_usageless_records(self):
        turns = su.main_turns([
            {"type": "user", "message": {"usage": {"input_tokens": 999}}},
            {"type": "assistant", "message": {}},
            {"type": "system"},
            assistant(inp=5),
        ])
        self.assertEqual(len(turns), 1)

    def test_sums_all_four_axes(self):
        tokens, model = su.sum_usage(su.main_turns([
            assistant(inp=1, out=2, cr=3, cw=4),
            assistant(inp=10, out=20, cr=30, cw=40),
        ]))
        self.assertEqual(tokens, {"input": 11, "output": 22,
                                  "cache_read": 33, "cache_write": 44})
        self.assertEqual(model, "claude-sonnet-4-6")


class HookCase(unittest.TestCase):
    """End-to-end against a persistent root, so the cursor survives between fires."""

    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.addCleanup(self._td.cleanup)
        self.root = Path(self._td.name)
        hooks = self.root / ".claude" / "hooks"
        (hooks / "lib").mkdir(parents=True)
        shutil.copy(HOOK, hooks / HOOK.name)
        for mod in ("common.py", "token_cost.py"):
            shutil.copy(LIB / mod, hooks / "lib" / mod)
        self.hook = hooks / HOOK.name
        self.state = self.root / ".claude" / "state"
        self.state.mkdir(parents=True)
        (self.state / "current_run.txt").write_text("FEAT-001")

    def transcript(self, records, name="session.jsonl"):
        tx = self.root / name
        tx.write_text("\n".join(json.dumps(r) for r in records) + "\n")
        return tx

    def fire(self, tx):
        subprocess.run(
            [sys.executable, str(self.hook)],
            input=json.dumps({"transcript_path": str(tx)}),
            cwd=str(self.root), capture_output=True, text=True)

    def events(self):
        log = self.state / "logs" / "FEAT-001.jsonl"
        if not log.exists():
            return []
        return [json.loads(ln) for ln in log.read_text().splitlines() if ln.strip()]

    def test_attributes_to_the_orchestrator_not_a_task(self):
        self.fire(self.transcript([assistant(inp=100, out=50)]))
        ev = self.events()
        self.assertEqual(len(ev), 1)
        self.assertEqual(ev[0]["event"], "usage")
        self.assertEqual(ev[0]["agent_role"], "orchestrator")
        self.assertIsNone(ev[0]["task_id"], "the orchestrator is not a task")
        self.assertEqual(ev[0]["tokens"]["input"], 100)
        self.assertEqual(ev[0]["turns"], 1)

    def test_repeated_fires_do_not_double_count(self):
        # THE bug this hook could introduce. Stop fires per main-agent response;
        # each fire must bill only the turns added since the last one.
        tx = self.transcript([assistant(inp=100)])
        self.fire(tx)
        self.fire(tx)  # nothing new since last time
        self.assertEqual(len(self.events()), 1, "an unchanged transcript bills once")

        self.transcript([assistant(inp=100), assistant(inp=7)])
        self.fire(tx)
        ev = self.events()
        self.assertEqual(len(ev), 2)
        self.assertEqual(ev[1]["tokens"]["input"], 7,
                         "second event bills only the new turn, not the total")
        self.assertEqual(sum(e["tokens"]["input"] for e in ev), 107)

    def test_a_new_session_starts_a_new_count(self):
        self.fire(self.transcript([assistant(inp=100)], name="a.jsonl"))
        # Different transcript: its turns have never been billed, so all of them
        # count even though the cursor from the previous session is still on disk.
        self.fire(self.transcript([assistant(inp=5), assistant(inp=5)], name="b.jsonl"))
        ev = self.events()
        self.assertEqual(len(ev), 2)
        self.assertEqual(ev[1]["turns"], 2)
        self.assertEqual(ev[1]["tokens"]["input"], 10)

    def test_sidechain_turns_are_not_billed_here(self):
        self.fire(self.transcript([
            assistant(inp=10),
            assistant(inp=999, sidechain=True),  # log_usage.py's territory
        ]))
        self.assertEqual(self.events()[0]["tokens"]["input"], 10)

    def test_no_run_means_no_event(self):
        # Ambient sessions outside a build have no run log to land in; inventing
        # one would make /agentic-logs list non-runs.
        (self.state / "current_run.txt").unlink()
        self.fire(self.transcript([assistant(inp=100)]))
        self.assertEqual(self.events(), [])

    def test_corrupt_cursor_does_not_break_the_hook(self):
        (self.state / "main_usage_cursor.json").write_text("{not json")
        self.fire(self.transcript([assistant(inp=100)]))
        self.assertEqual(len(self.events()), 1)

    def test_empty_transcript_writes_nothing(self):
        self.fire(self.transcript([]))
        self.assertEqual(self.events(), [])


if __name__ == "__main__":
    unittest.main()
