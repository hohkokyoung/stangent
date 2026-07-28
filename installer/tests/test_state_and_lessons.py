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
            self.assertIn("cleared dispatch state", r.stdout)
            self.assertFalse((sd / "current_run.txt").exists())
            self.assertFalse((sd / "current_task.txt").exists())

    def test_clear_when_empty(self):
        with tempfile.TemporaryDirectory() as td:
            (Path(td) / ".claude" / "state").mkdir(parents=True)
            r = run(STATE, ["clear"], td)
            self.assertIn("no dispatch state", r.stdout)

    def test_clear_agent_keeps_the_run_context(self):
        # The distinction the commands depend on: a command dispatching several
        # subagents clears between them, but its tool calls must keep landing in
        # the same run's log until teardown.
        with tempfile.TemporaryDirectory() as td:
            sd = self._scaffold(td, {"current_run.txt": False, "current_task.txt": False,
                                     "current_role.txt": False, "current_model.txt": False})
            r = run(STATE, ["clear", "--agent"], td)
            self.assertIn("cleared agent state", r.stdout)
            self.assertTrue((sd / "current_run.txt").exists(), "run context must survive")
            for gone in ("current_task.txt", "current_role.txt", "current_model.txt"):
                self.assertFalse((sd / gone).exists(), gone)

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

    def test_recent_current_run_log_keeps_build_active(self):
        # State files old, but the CURRENT run's log was written recently → active.
        with tempfile.TemporaryDirectory() as td:
            sd = self._scaffold(td, {"current_run.txt": True, "current_task.txt": True})
            (sd / "current_run.txt").write_text("FEAT-001")  # matches log below
            old = time.time() - 4000
            os.utime(sd / "current_run.txt", (old, old))  # keep run file old
            logs = sd / "logs"
            logs.mkdir()
            (logs / "FEAT-001.jsonl").write_text("{}\n")  # fresh mtime
            r = run(STATE, ["check", "--json"], td)
            self.assertEqual(json.loads(r.stdout)["stale"], [])

    def test_ambient_log_does_not_mask_stale_state(self):
        # A fresh _no-run.jsonl (unrelated tool use) must NOT hide leftover state.
        with tempfile.TemporaryDirectory() as td:
            sd = self._scaffold(td, {"current_run.txt": True, "current_task.txt": True})
            (sd / "current_run.txt").write_text("FEAT-001")
            old = time.time() - 4000
            os.utime(sd / "current_run.txt", (old, old))
            logs = sd / "logs"
            logs.mkdir()
            (logs / "_no-run.jsonl").write_text("{}\n")   # fresh, but ambient
            r = run(STATE, ["check", "--json"], td)
            self.assertEqual(sorted(s["file"] for s in json.loads(r.stdout)["stale"]),
                             ["current_run.txt", "current_task.txt"])


class TestStateClean(unittest.TestCase):
    def _build(self, td):
        sd = Path(td) / ".claude" / "state"
        (sd / "audit" / "REVIEW-empty").mkdir(parents=True)          # empty → prune
        good = sd / "audit" / "REVIEW-good"
        good.mkdir(parents=True)
        (good / "findings.md").write_text("x")                       # keep
        old = time.time() - 40 * 86400
        old_plan = sd / "plans" / "FEAT-OLD"
        old_plan.mkdir(parents=True)
        (old_plan / "t1.md").write_text("x")
        os.utime(old_plan, (old, old))                               # >30d → prune
        new_plan = sd / "plans" / "FEAT-NEW"
        new_plan.mkdir(parents=True)
        (new_plan / "t1.md").write_text("x")                         # recent → keep
        return sd

    def test_dry_run_lists_but_keeps(self):
        with tempfile.TemporaryDirectory() as td:
            sd = self._build(td)
            r = run(STATE, ["clean", "--json"], td)
            data = json.loads(r.stdout)
            self.assertIn("audit/REVIEW-empty", data["candidates"])
            self.assertIn("plans/FEAT-OLD", data["candidates"])
            self.assertEqual(data["removed"], [])
            self.assertTrue((sd / "audit" / "REVIEW-empty").exists())  # untouched

    def test_apply_removes_only_stale(self):
        with tempfile.TemporaryDirectory() as td:
            sd = self._build(td)
            run(STATE, ["clean", "--apply"], td)
            self.assertFalse((sd / "audit" / "REVIEW-empty").exists())
            self.assertFalse((sd / "plans" / "FEAT-OLD").exists())
            self.assertTrue((sd / "audit" / "REVIEW-good" / "findings.md").exists())
            self.assertTrue((sd / "plans" / "FEAT-NEW").exists())

    def test_nothing_to_clean(self):
        with tempfile.TemporaryDirectory() as td:
            (Path(td) / ".claude" / "state").mkdir(parents=True)
            r = run(STATE, ["clean"], td)
            self.assertIn("nothing to clean", r.stdout)

    def test_deferred_run_protected_from_age_prune(self):
        # An OLD run holding a deferred (parked) task must never be pruned —
        # deleting it would break /agentic-resume.
        with tempfile.TemporaryDirectory() as td:
            sd = Path(td) / ".claude" / "state"
            parked = sd / "plans" / "FEAT-PARKED"
            parked.mkdir(parents=True)
            (parked / "t1.md").write_text("---\nstatus: deferred\n---\n")
            old = time.time() - 90 * 86400
            os.utime(parked, (old, old))
            os.utime(parked / "t1.md", (old, old))
            r = run(STATE, ["clean", "--json"], td)
            data = json.loads(r.stdout)
            self.assertNotIn("plans/FEAT-PARKED", data["candidates"])

    def test_current_run_protected(self):
        with tempfile.TemporaryDirectory() as td:
            sd = Path(td) / ".claude" / "state"
            cur = sd / "plans" / "FEAT-CURRENT"
            cur.mkdir(parents=True)
            (cur / "t1.md").write_text("---\nstatus: done\n---\n")
            (sd / "current_run.txt").write_text("FEAT-CURRENT")
            old = time.time() - 90 * 86400
            os.utime(cur, (old, old))
            os.utime(cur / "t1.md", (old, old))
            r = run(STATE, ["clean", "--json"], td)
            data = json.loads(r.stdout)
            self.assertNotIn("plans/FEAT-CURRENT", data["candidates"])


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
        self.assertIn("missing UTC timestamp", lessons._review_body(text))
        self.assertNotIn("stuff", lessons._review_body(text))

    def test_extract_skips_placeholder_only(self):
        text = "## Review\n\n<!-- reviewer appends ONLY here -->\n"
        self.assertIsNone(lessons._review_body(text))


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
