#!/usr/bin/env python3
"""Tests for logs.py — the log summarizer.

Runs it as a subprocess with a controlled cwd (it resolves state from cwd).

    python3 -m unittest discover installer/tests
"""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LOGS = REPO / "templates" / ".claude" / "hooks" / "lib" / "logs.py"


def run(args, cwd):
    return subprocess.run([sys.executable, str(LOGS)] + args,
                          cwd=str(cwd), capture_output=True, text=True)


def jl(lines):
    return "\n".join(json.dumps(x) for x in lines) + "\n"


class TestSummarize(unittest.TestCase):
    def _build(self, td):
        sd = Path(td) / ".claude" / "state"
        logs = sd / "logs"; logs.mkdir(parents=True)
        (logs / "FEAT-001.jsonl").write_text(jl([
            {"ts": "2026-07-24T10:00:00Z", "run_id": "FEAT-001", "task_id": "t1",
             "agent_role": "implementer", "model": "claude-sonnet-4-6", "tool": "mcp__agentic_mcp__retrieve", "ok": True, "args": {}},
            {"ts": "2026-07-24T10:02:00Z", "run_id": "FEAT-001", "task_id": "t1",
             "agent_role": "implementer", "model": "claude-sonnet-4-6", "tool": "mcp__agentic_mcp__get_symbol", "ok": True, "args": {}},
            {"ts": "2026-07-24T10:04:00Z", "run_id": "FEAT-001", "task_id": "t1",
             "agent_role": "implementer", "model": "claude-sonnet-4-6", "tool": "Bash", "ok": False, "args": {}},
            {"ts": "2026-07-24T10:05:00Z", "run_id": "FEAT-001", "task_id": "t2",
             "agent_role": "reviewer", "model": "claude-haiku-4-5-20251001", "tool": "Write", "ok": True,
             "args": {}, "deny_reason": "role 'reviewer' may only write under ..."},
        ]))
        (logs / "dispatch.jsonl").write_text(jl([
            {"ts": "2026-07-24T10:00:00Z", "run_id": "FEAT-001", "task_id": "t1", "role": "implementer", "model_selected": "claude-sonnet-4-6", "routing_applied": False},
            {"ts": "2026-07-24T10:05:00Z", "run_id": "FEAT-002", "task_id": "t1", "role": "implementer"},  # other run
        ]))
        plans = sd / "plans" / "FEAT-001"; plans.mkdir(parents=True)
        (plans / "t1.md").write_text("---\nid: t1\nstatus: done\n---\n")
        (plans / "t2.md").write_text("---\nid: t2\nstatus: blocked\n---\n")
        return sd

    def test_summary_counts(self):
        with tempfile.TemporaryDirectory() as td:
            self._build(td)
            r = run(["summarize", "FEAT-001", "--json"], td)
            rep = json.loads(r.stdout)
            self.assertEqual(rep["totals"]["tasks"], 2)
            self.assertEqual(rep["totals"]["calls"], 4)
            self.assertEqual(rep["totals"]["retrieve"], 1)
            self.assertEqual(rep["totals"]["get_symbol"], 1)
            self.assertEqual(rep["totals"]["failures"], 1)
            self.assertEqual(rep["totals"]["denials"], 1)

    def test_usage_events_folded_in(self):
        with tempfile.TemporaryDirectory() as td:
            sd = Path(td) / ".claude" / "state"
            logs = sd / "logs"; logs.mkdir(parents=True)
            (logs / "FEAT-001.jsonl").write_text(jl([
                {"ts": "2026-07-24T10:00:00Z", "run_id": "FEAT-001", "task_id": "t1",
                 "agent_role": "implementer", "model": "claude-sonnet-4-6", "tool": "Bash", "ok": True, "args": {}},
                {"ts": "2026-07-24T10:01:00Z", "event": "usage", "run_id": "FEAT-001", "task_id": "t1",
                 "agent_role": "implementer", "model": "claude-sonnet-4-6", "turns": 3,
                 "tokens": {"input": 1000, "output": 2000, "cache_read": 5000, "cache_write": 0},
                 "cost_usd": 0.033},
            ]))
            rep = json.loads(run(["summarize", "FEAT-001", "--json"], td).stdout)
            self.assertTrue(rep["has_usage"])
            self.assertAlmostEqual(rep["totals"]["cost_usd"], 0.033, places=3)
            self.assertEqual(rep["totals"]["tokens"]["output"], 2000)
            t1 = next(t for t in rep["tasks"] if t["task_id"] == "t1")
            self.assertEqual(t1["tokens"]["cache_read"], 5000)
            # usage line must NOT be counted as a tool call
            self.assertEqual(rep["totals"]["calls"], 1)

    def test_task_status_and_duration(self):
        with tempfile.TemporaryDirectory() as td:
            self._build(td)
            rep = json.loads(run(["summarize", "FEAT-001", "--json"], td).stdout)
            t1 = next(t for t in rep["tasks"] if t["task_id"] == "t1")
            self.assertEqual(t1["status"], "done")
            self.assertEqual(t1["role"], "implementer")
            self.assertEqual(t1["duration_s"], 240.0)  # 10:00 → 10:04
            t2 = next(t for t in rep["tasks"] if t["task_id"] == "t2")
            self.assertEqual(t2["status"], "blocked")

    def test_dispatch_filtered_to_run(self):
        # FEAT-002's dispatch line must not leak into FEAT-001's report.
        with tempfile.TemporaryDirectory() as td:
            self._build(td)
            rep = json.loads(run(["summarize", "FEAT-001", "--json"], td).stdout)
            self.assertEqual({t["task_id"] for t in rep["tasks"]}, {"t1", "t2"})

    def test_missing_log(self):
        with tempfile.TemporaryDirectory() as td:
            (Path(td) / ".claude" / "state" / "logs").mkdir(parents=True)
            rep = json.loads(run(["summarize", "NOPE", "--json"], td).stdout)
            self.assertFalse(rep["has_logs"])
            r = run(["summarize", "NOPE"], td)
            self.assertIn("no tool-use log", r.stdout)


class TestList(unittest.TestCase):
    def test_list_excludes_infra_logs(self):
        with tempfile.TemporaryDirectory() as td:
            logs = Path(td) / ".claude" / "state" / "logs"; logs.mkdir(parents=True)
            for n in ("FEAT-001.jsonl", "SEC-x.jsonl", "dispatch.jsonl",
                      "_no-run.jsonl", "FEAT-001.1.jsonl"):
                (logs / n).write_text("{}\n")
            ids = {c["id"] for c in json.loads(run(["list", "--json"], td).stdout)}
            self.assertEqual(ids, {"FEAT-001", "SEC-x"})


if __name__ == "__main__":
    unittest.main()
