#!/usr/bin/env python3
"""Tests for the verify_review SubagentStop hook.

The command-level verification step is an instruction to the orchestrator, so it
can be skipped silently on exactly the run where it mattered. This hook is the
version the harness fires regardless. These tests run it as it really runs — a
subprocess against a controlled repo root.

    python3 -m unittest discover installer/tests
"""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
TPL = REPO / "templates" / ".claude"


class HookCase(unittest.TestCase):
    def setUp(self):
        self.td = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.td, ignore_errors=True)
        self.hooks = self.td / ".claude" / "hooks"
        (self.hooks / "lib").mkdir(parents=True)
        self.state = self.td / ".claude" / "state"
        (self.state / "logs").mkdir(parents=True)
        (self.td / ".claude" / "agents").mkdir(parents=True)
        shutil.copy(TPL / "hooks" / "verify_review.py", self.hooks)
        # Copy the whole lib rather than a hand-listed subset: the list silently
        # went stale when verify_clears was split into verify_parse/verify_exec,
        # and a fixture that lags the real layout tests something that no longer
        # ships.
        for lib in (TPL / "hooks" / "lib").glob("*.py"):
            shutil.copy(lib, self.hooks / "lib")

    def write(self, rel, text):
        p = self.td / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")
        return p

    def fire(self, run_id="UIR-1", role=None, task_id=None):
        for name, val in (("current_run.txt", run_id), ("current_role.txt", role),
                          ("current_task.txt", task_id)):
            if val is not None:
                (self.state / name).write_text(val)
        subprocess.run([sys.executable, str(self.hooks / "verify_review.py")],
                       input="{}", cwd=str(self.td), capture_output=True, text=True)
        log = self.state / "logs" / f"{run_id}.jsonl"
        if not log.exists():
            return []
        return [json.loads(l) for l in log.read_text().splitlines() if l.strip()]

    def events(self, rows):
        return [r for r in rows if r.get("event") == "verification"]


class TestVerifyReviewHook(HookCase):
    CHECKLIST = "# Agent\n- [ ] area one\n- [ ] area two\n"

    def test_failing_report_is_recorded(self):
        self.write(".claude/agents/auditor.md", self.CHECKLIST)
        self.write("src/a.txt", "hit\nhit\n")
        self.write(".claude/state/audit/UIR-1/findings.md",
                   "# Audit\n\n## Coverage\n"
                   "| # | type | scan | result |\n|---|---|---|---|\n"
                   "| 1 | dup | `grep -c hit src/a.txt` -> 9 matches | F01 |\n")
        ev = self.events(self.fire(role="auditor"))
        self.assertEqual(len(ev), 1)
        self.assertEqual(ev[0]["exit"], 1)
        self.assertGreaterEqual(ev[0]["failing"], 1)
        self.assertEqual(ev[0]["agent_role"], "auditor")

    def test_clean_report_records_exit_zero(self):
        self.write(".claude/agents/auditor.md", "# Agent\n- [ ] only area\n")
        self.write("src/a.txt", "hit\nhit\n")
        self.write(".claude/state/audit/UIR-1/findings.md",
                   "# Audit\n\n## Coverage\n"
                   "| # | type | scan | result |\n|---|---|---|---|\n"
                   "| 1 | dup | `grep -c hit src/a.txt` -> 2 matches | none |\n")
        ev = self.events(self.fire(role="auditor"))
        self.assertEqual(len(ev), 1)
        self.assertEqual(ev[0]["exit"], 0)
        self.assertEqual(ev[0]["failing"], 0)

    def test_reviewer_verifies_the_task_file(self):
        # The reviewer's report IS the task file, so its path needs the task id.
        self.write(".claude/agents/reviewer.md", self.CHECKLIST)
        self.write(".claude/state/plans/FEAT-1/t5.md",
                   "---\nid: t5\n---\n\n## Review\n\n### Coverage\n"
                   "| # | area | checked | result |\n|---|---|---|---|\n"
                   "| 1 | area one | read it | pass |\n")
        ev = self.events(self.fire(run_id="FEAT-1", role="reviewer", task_id="t5"))
        self.assertEqual(len(ev), 1)
        self.assertEqual(ev[0]["coverage"], "incomplete")  # 1 row, 2 areas
        self.assertEqual(ev[0]["exit"], 1)

    def test_non_reviewing_role_writes_nothing(self):
        self.assertEqual(self.events(self.fire(role="implementer")), [])

    def test_missing_report_writes_nothing(self):
        self.write(".claude/agents/auditor.md", self.CHECKLIST)
        self.assertEqual(self.events(self.fire(role="auditor")), [])

    def test_no_run_context_writes_nothing(self):
        # No current_run.txt at all — the hook fires on every subagent stop,
        # including ones outside any workflow.
        subprocess.run([sys.executable, str(self.hooks / "verify_review.py")],
                       input="{}", cwd=str(self.td), capture_output=True, text=True)
        self.assertEqual(list((self.state / "logs").iterdir()), [])

    def test_role_falls_back_to_the_last_logged_call(self):
        # SubagentStop can fire after the command cleared per-agent state — the
        # same race that stranded a task's usage under task_id=null.
        self.write(".claude/agents/auditor.md", "# Agent\n- [ ] only area\n")
        self.write("src/a.txt", "hit\n")
        self.write(".claude/state/audit/UIR-1/findings.md",
                   "# Audit\n\n## Coverage\n"
                   "| # | type | scan | result |\n|---|---|---|---|\n"
                   "| 1 | dup | `grep -c hit src/a.txt` -> 1 matches | none |\n")
        (self.state / "logs" / "UIR-1.jsonl").write_text(json.dumps(
            {"run_id": "UIR-1", "task_id": None, "agent_role": "auditor",
             "tool": "Grep"}) + "\n")
        ev = self.events(self.fire(role=None))   # role state absent
        self.assertEqual(len(ev), 1)
        self.assertEqual(ev[0]["agent_role"], "auditor")

    def test_hook_never_raises_on_a_malformed_report(self):
        self.write(".claude/agents/auditor.md", self.CHECKLIST)
        self.write(".claude/state/audit/UIR-1/findings.md", "\x00 not markdown \xff")
        r = subprocess.run([sys.executable, str(self.hooks / "verify_review.py")],
                           input="{}", cwd=str(self.td), capture_output=True, text=True)
        self.assertEqual(r.returncode, 0)


if __name__ == "__main__":
    unittest.main()
