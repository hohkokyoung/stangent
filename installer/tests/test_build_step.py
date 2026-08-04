#!/usr/bin/env python3
"""Tests for build_step.py — the per-task driver that collapses /agentic-build's
bookkeeping from ~6 orchestrator turns to 2.

What matters here is that folding the calls together did not change the contract:
the same task is chosen, the same state is written, the same exit codes come back,
and `finish` still clears role state BEFORE checkpointing (pre_tool_use.py denies
`git commit` while a role is active, so the reverse order is silently refused).

The other half is the reason the change exists: `next` must emit ONE task, not the
whole plan. Re-printing every remaining task each iteration is what made the
orchestrator's context grow quadratically in task count.

    python3 -m unittest discover installer/tests
"""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from test_dispatch_plan import CONFIG, task_md

REPO = Path(__file__).resolve().parents[1]
SRC_LIB = REPO / "templates" / ".claude" / "hooks" / "lib"


class BuildStepCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.addCleanup(self._td.cleanup)
        self.root = Path(self._td.name)
        claude = self.root / ".claude"
        # Copy the whole lib: build_step shells out to its siblings and derives
        # every path from __file__, so it must sit in a real install layout.
        shutil.copytree(SRC_LIB, claude / "hooks" / "lib")
        (claude / "agents").mkdir(parents=True)
        (claude / "agents" / "reviewer.md").write_text("# Reviewer\n")
        (claude / ".agentic.yml").write_text(CONFIG)
        self.state = claude / "state"
        self.state.mkdir(parents=True)
        self.script = claude / "hooks" / "lib" / "build_step.py"

    def make_run(self, files, run_id="FEAT-001"):
        run = self.state / "plans" / run_id
        run.mkdir(parents=True, exist_ok=True)
        for name, content in files.items():
            (run / name).write_text(content)

    def step(self, *args):
        proc = subprocess.run([sys.executable, str(self.script), *args],
                              cwd=str(self.root), capture_output=True, text=True)
        out = json.loads(proc.stdout) if proc.stdout.strip() else {}
        return proc.returncode, out

    def state_files(self):
        return sorted(p.name for p in self.state.glob("*.txt"))

    # --- next -------------------------------------------------------------

    def test_emits_one_task_not_the_whole_plan(self):
        # The point of the change. Three tasks are runnable; only the first is
        # emitted, and the rest appear as a count rather than as resolved objects.
        self.make_run({
            "t1.md": task_md("t1"),
            "t2.md": task_md("t2"),
            "t3.md": task_md("t3"),
        })
        code, out = self.step("next", "FEAT-001")
        self.assertEqual(code, 0)
        self.assertEqual(out["task"]["task_id"], "t1")
        self.assertEqual(out["counts"]["runnable"], 3)
        self.assertNotIn("runnable", out,
                         "the resolved list must not be emitted — that is the cost")
        blob = json.dumps(out)
        self.assertNotIn("t2", blob)
        self.assertNotIn("t3", blob)

    def test_task_carries_every_resolved_field(self):
        self.make_run({"t1.md": task_md("t1", role="reviewer", complexity="low")})
        _, out = self.step("next", "FEAT-001")
        for field in ("task_id", "path", "role", "model", "role_baseline",
                      "routing_applied", "complexity", "skills", "k"):
            self.assertIn(field, out["task"], f"{field} must not need recomputing")

    def test_writes_the_dispatch_state(self):
        self.make_run({"t1.md": task_md("t1", role="implementer")})
        _, out = self.step("next", "FEAT-001")
        self.assertEqual(self.state_files(),
                         ["current_model.txt", "current_role.txt", "current_task.txt"])
        self.assertEqual((self.state / "current_task.txt").read_text(), "t1")
        self.assertEqual((self.state / "current_role.txt").read_text(), "implementer")
        self.assertEqual((self.state / "current_model.txt").read_text(),
                         out["task"]["model"])

    def test_reindex_field_carries_the_summary_not_progress_noise(self):
        # Found on a real install: fastembed writes a download progress bar to
        # stderr on a cold model cache, and taking the last line put
        # "Fetching 5 files: 100%|####|" into the dispatcher's context once per
        # task — the exact noise this script exists to remove.
        lib = self.root / ".claude" / "hooks" / "lib"
        (lib / "retriever.py").write_text(
            "import sys\n"
            "print('[retriever] project: 3 indexed, 0 skipped')\n"
            "sys.stderr.write('Fetching 5 files: 100%|####| 5/5 [00:03<00:00]\\n')\n")
        self.make_run({"t1.md": task_md("t1")})
        _, out = self.step("next", "FEAT-001")
        self.assertTrue(out["reindex"].startswith("[retriever]"), out["reindex"])
        self.assertNotIn("Fetching", out["reindex"])

    def test_done_when_nothing_runnable(self):
        self.make_run({"t1.md": task_md("t1", status="done")})
        code, out = self.step("next", "FEAT-001")
        self.assertEqual(code, 0)
        self.assertTrue(out["done"])
        self.assertNotIn("task", out)

    def test_dependency_cycle_propagates_exit_3(self):
        self.make_run({
            "t1.md": task_md("t1", depends_on=["t2"]),
            "t2.md": task_md("t2", depends_on=["t1"]),
        })
        code, out = self.step("next", "FEAT-001")
        self.assertEqual(code, 3, "the command branches on this code")
        self.assertIn("cycle", json.dumps(out))

    def test_refused_single_task_propagates_exit_4(self):
        # The dependency check for /agentic-build <task-id>. Must not be bypassed
        # just because the call is now wrapped.
        self.make_run({
            "t1.md": task_md("t1"),
            "t2.md": task_md("t2", depends_on=["t1"]),
        })
        code, _ = self.step("next", "FEAT-001", "--task", "t2")
        self.assertEqual(code, 4)

    def test_no_state_written_when_nothing_is_dispatched(self):
        # A cycle must not leave current_*.txt behind to mistag later logs.
        self.make_run({
            "t1.md": task_md("t1", depends_on=["t2"]),
            "t2.md": task_md("t2", depends_on=["t1"]),
        })
        self.step("next", "FEAT-001")
        self.assertEqual(self.state_files(), [])

    def test_invalid_deps_are_named_not_counted(self):
        # A count cannot be acted on: the developer has to see which task points
        # at which missing id to fix it.
        self.make_run({
            "t1.md": task_md("t1", depends_on=["nope"]),
            "t2.md": task_md("t2"),
        })
        _, out = self.step("next", "FEAT-001")
        self.assertEqual(out["invalid_deps"][0]["task_id"], "t1")
        self.assertEqual(out["invalid_deps"][0]["missing"], ["nope"])

    # --- finish -----------------------------------------------------------

    def test_finish_clears_per_task_state(self):
        self.make_run({"t1.md": task_md("t1")})
        self.step("next", "FEAT-001")
        self.assertNotEqual(self.state_files(), [])
        code, out = self.step("finish", "FEAT-001", "t1", "--role", "implementer")
        self.assertEqual(code, 0)
        self.assertEqual(self.state_files(), [],
                         "stale current_*.txt would mistag the next task's logs")
        self.assertIn("checkpoint", out)

    def test_finish_clears_the_edit_tally(self):
        # Per-task state: leaving it would charge one task's edits to the next.
        (self.state / "edit_counts.json").write_text('{"task": "t1", "files": {}}')
        self.make_run({"t1.md": task_md("t1")})
        self.step("finish", "FEAT-001", "t1", "--role", "implementer")
        self.assertFalse((self.state / "edit_counts.json").exists())

    def test_finish_verifies_coverage_only_for_reviewers(self):
        self.make_run({"t1.md": task_md("t1")})
        path = str(self.state / "plans" / "FEAT-001" / "t1.md")
        _, out = self.step("finish", "FEAT-001", "t1",
                           "--role", "implementer", "--path", path)
        self.assertNotIn("verify_clears", out)
        _, out = self.step("finish", "FEAT-001", "t1",
                           "--role", "reviewer", "--path", path)
        self.assertIn("verify_clears", out)

    def test_finish_survives_a_non_git_project(self):
        # The checkpoint is best-effort by contract; a missing repo prints a line
        # and the build continues rather than failing the task.
        self.make_run({"t1.md": task_md("t1")})
        code, out = self.step("finish", "FEAT-001", "t1", "--role", "implementer")
        self.assertEqual(code, 0)
        self.assertIsInstance(out["checkpoint"], str)


if __name__ == "__main__":
    unittest.main()
