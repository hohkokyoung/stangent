#!/usr/bin/env python3
"""Tests for the post_tool_use logger: ambient gating + size rotation.

Runs the hook as it really does — a subprocess with a controlled repo root.

    python3 -m unittest discover installer/tests
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
HOOK = REPO / "templates" / ".claude" / "hooks" / "post_tool_use.py"
PAYLOAD = {"tool_name": "Bash", "tool_input": {"command": "echo hi"},
           "tool_response": {}}


class LoggerCase(unittest.TestCase):
    def run_hook(self, run_id=None, env=None, pre=None):
        """Run the hook and snapshot logs/ BEFORE the tempdir is cleaned up.
        Returns {filename: line_count}. `pre`: {logname: bytes} to seed first."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            hooks = root / ".claude" / "hooks"
            logs = root / ".claude" / "state" / "logs"
            hooks.mkdir(parents=True)
            (root / ".claude" / "state").mkdir(exist_ok=True)
            shutil.copy(HOOK, hooks / HOOK.name)
            if run_id is not None:
                (root / ".claude" / "state" / "current_run.txt").write_text(run_id)
            if pre:
                logs.mkdir(parents=True, exist_ok=True)
                for name, data in pre.items():
                    (logs / name).write_bytes(data)
            runenv = {**os.environ, **(env or {})}
            subprocess.run([sys.executable, str(hooks / HOOK.name)],
                           input=json.dumps(PAYLOAD), cwd=str(root),
                           capture_output=True, text=True, env=runenv)
            snapshot = {}
            if logs.exists():
                for f in logs.iterdir():
                    if f.is_file():
                        snapshot[f.name] = len(f.read_text(errors="replace").splitlines())
            return snapshot

    def test_ambient_not_logged_by_default(self):
        logs = self.run_hook(run_id=None)
        self.assertNotIn("_no-run.jsonl", logs,
                         "ambient tool call must not be logged without a run")

    def test_ambient_logged_when_opted_in(self):
        logs = self.run_hook(run_id=None, env={"AGENTIC_LOG_AMBIENT": "1"})
        self.assertEqual(logs.get("_no-run.jsonl"), 1)

    def test_run_activity_logged(self):
        logs = self.run_hook(run_id="FEAT-001")
        self.assertEqual(logs.get("FEAT-001.jsonl"), 1)

    def test_large_log_rotates(self):
        big = b"x" * (5 * 1024 * 1024 + 8)  # just over MAX_LOG_BYTES
        logs = self.run_hook(run_id="FEAT-001", pre={"FEAT-001.jsonl": big})
        self.assertIn("FEAT-001.1.jsonl", logs, "oversized log should roll to .1.jsonl")
        self.assertEqual(logs.get("FEAT-001.jsonl"), 1)  # fresh log holds only the new line


if __name__ == "__main__":
    unittest.main()
