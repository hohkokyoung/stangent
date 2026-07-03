#!/usr/bin/env python3
"""Tests for state.py (dispatch hygiene) and lessons.py (cross-run learning).

    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LIB = REPO / "templates" / ".claude" / "hooks" / "lib"
STATE = LIB / "state.py"
LESSONS = LIB / "lessons.py"

_spec = importlib.util.spec_from_file_location("lessons", LESSONS)
lessons = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(lessons)


def run(script, args, cwd):
    return subprocess.run([sys.executable, str(script)] + args,
                          cwd=str(cwd), capture_output=True, text=True)


class TestState(unittest.TestCase):
    def _scaffold(self, td, files):
        sd = Path(td) / ".claude" / "state"
        sd.mkdir(parents=True)
        for name, backdate in files.items():
            p = sd / name
            p.write_text("x")
            if backdate:
                old = time.time() - 4000
                os.utime(p, (old, old))
        return sd

    def test_clear_removes_all(self):
        with tempfile.TemporaryDirectory() as td:
            sd = self._scaffold(td, {"current_run.txt": False, "current_task.txt": False})
            r = run(STATE, ["clear"], td)
            self.assertIn("cleared leftover", r.stdout)
            self.assertFalse((sd / "current_run.txt").exists())
            self.assertFalse((sd / "current_task.txt").exists())

    def test_clear_when_empty(self):
        with tempfile.TemporaryDirectory() as td:
            (Path(td) / ".claude" / "state").mkdir(parents=True)
            r = run(STATE, ["clear"], td)
            self.assertIn("no leftover", r.stdout)

    def test_fresh_activity_is_not_stale(self):
        # An old current_run.txt alongside a freshly-written current_task.txt is
        # an active build, not leftover state — nothing should be flagged.
        with tempfile.TemporaryDirectory() as td:
            self._scaffold(td, {"current_run.txt": True, "current_task.txt": False})
            r = run(STATE, ["check", "--json"], td)
            data = json.loads(r.stdout)
            self.assertEqual(data["stale"], [])
            self.assertIn("current_task.txt", data["present"])

    def test_all_old_is_stale(self):
        # Every present file old AND no recent log activity → leftover from a crash.
        with tempfile.TemporaryDirectory() as td:
            self._scaffold(td, {"current_run.txt": True, "current_task.txt": True})
            r = run(STATE, ["check", "--json"], td)
            data = json.loads(r.stdout)
            self.assertEqual(sorted(s["file"] for s in data["stale"]),
                             ["current_run.txt", "current_task.txt"])

    def test_recent_log_keeps_build_active(self):
        # State files old, but a log written recently → still an active build.
        with tempfile.TemporaryDirectory() as td:
            self._scaffold(td, {"current_run.txt": True, "current_task.txt": True})
            logs = Path(td) / ".claude" / "state" / "logs"
            logs.mkdir()
            (logs / "FEAT-001.jsonl").write_text("{}\n")  # fresh mtime
            r = run(STATE, ["check", "--json"], td)
            self.assertEqual(json.loads(r.stdout)["stale"], [])


class TestLessonsExtract(unittest.TestCase):
    def test_extract_review_section(self):
        text = textwrap.dedent("""\
            ## Design
            stuff
            ## Review
            Verdict: blocking
            - [ADR-001] missing UTC timestamp
            ## Test results
            passed
            """)
        self.assertIn("missing UTC timestamp", lessons._extract_section(text, "Review"))
        self.assertNotIn("stuff", lessons._extract_section(text, "Review"))

    def test_extract_skips_placeholder_only(self):
        text = "## Review\n\n<!-- reviewer appends ONLY here -->\n"
        self.assertEqual(lessons._extract_section(text, "Review"), "")


class TestLessonsCLI(unittest.TestCase):
    def _scaffold_run(self, td, reviews):
        run_dir = Path(td) / ".claude" / "state" / "plans" / "FEAT-001"
        run_dir.mkdir(parents=True)
        for tid, review in reviews.items():
            body = f"---\nid: {tid}\n---\n## Review\n{review}\n"
            (run_dir / f"{tid}.md").write_text(body)

    def test_collect_returns_nonempty_reviews(self):
        with tempfile.TemporaryDirectory() as td:
            self._scaffold_run(td, {"t1": "Verdict: blocking\n- bad thing", "t2": ""})
            r = run(LESSONS, ["collect"], td)
            data = json.loads(r.stdout)
            self.assertEqual([d["task_id"] for d in data], ["t1"])

    def test_add_dedup_and_cap(self):
        with tempfile.TemporaryDirectory() as td:
            (Path(td) / ".claude" / "state").mkdir(parents=True)
            run(LESSONS, ["add", "Validate input server-side"], td)
            r = run(LESSONS, ["add", "validate   input   server-side"], td)  # dup (norm+case)
            self.assertIn("skipped", r.stdout)
            for i in range(35):
                run(LESSONS, ["add", f"lesson number {i}"], td)
            content = run(LESSONS, ["show"], td).stdout
            n = content.count("\n- ")
            self.assertEqual(n, 30)  # capped
            self.assertNotIn("Validate input server-side", content)  # oldest dropped


if __name__ == "__main__":
    unittest.main()
