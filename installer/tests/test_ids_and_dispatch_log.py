#!/usr/bin/env python3
"""Tests for the two id allocators and the dispatch logger.

Small modules, but plan_id and adr_id decide the identity every artifact in a run
is filed under — a duplicate or skipped id is not a cosmetic problem, it collides
with a directory that already exists. log_dispatch is the only record of which
model a task was actually routed to.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest

try:
    import yaml as _yaml  # noqa: F401  — presence probe; the config path needs it
except ImportError:
    _yaml = None
from pathlib import Path

LIB = Path(__file__).resolve().parents[1] / "templates" / ".claude" / "hooks" / "lib"


def load(name: str):
    spec = importlib.util.spec_from_file_location(f"{name}_mod", LIB / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


plan_id = load("plan_id")
adr_id = load("adr_id")


class PlanIdCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        self.plans = self.root / ".claude" / "state" / "plans"
        self.plans.mkdir(parents=True)
        # The config path is resolved from REPO_ROOT per call, so pointing the
        # root at the temp dir is enough — there is no second frozen constant to
        # keep in sync. PLANS_DIR is still derived at import and needs its own.
        self._saved = (plan_id.PLANS_DIR, plan_id.REPO_ROOT)
        plan_id.PLANS_DIR = self.plans
        plan_id.REPO_ROOT = self.root

    def tearDown(self):
        plan_id.PLANS_DIR, plan_id.REPO_ROOT = self._saved
        self._td.cleanup()

    def write_cfg(self, text):
        (self.root / ".claude" / ".agentic.yml").write_text(text)

    def mk(self, *names):
        for n in names:
            (self.plans / n).mkdir()

    def test_first_id_uses_configured_start(self):
        self.assertEqual(plan_id.cmd_next(), "FEAT-001")

    def test_next_follows_the_highest_existing(self):
        self.mk("FEAT-001", "FEAT-002")
        self.assertEqual(plan_id.cmd_next(), "FEAT-003")

    def test_a_gap_is_not_reused(self):
        # Counting directories instead of taking the max would mint FEAT-003
        # here and collide the moment 003 is created later.
        self.mk("FEAT-001", "FEAT-003")
        self.assertEqual(plan_id.cmd_next(), "FEAT-004")

    def test_ordering_is_numeric_not_lexical(self):
        # "FEAT-009" > "FEAT-010" as strings; the allocator must not think so.
        self.mk("FEAT-009", "FEAT-010")
        self.assertEqual(plan_id.cmd_next(), "FEAT-011")

    def test_unrelated_entries_are_ignored(self):
        self.mk("FEAT-001", "SEC-20260101-000000", "notes", "FEAT-abc")
        (self.plans / "FEAT-999.md").write_text("a file, not a run dir")
        self.assertEqual(plan_id.cmd_next(), "FEAT-002")

    def test_peek_reports_the_latest_or_empty(self):
        self.assertEqual(plan_id.cmd_peek(), "")
        self.mk("FEAT-001", "FEAT-002")
        self.assertEqual(plan_id.cmd_peek(), "FEAT-002")

    def test_config_overrides_prefix_pad_and_start(self):
        if _yaml is None:
            self.skipTest("needs PyYAML to read plan_id: config")
        self.write_cfg(
            "plan_id:\n  prefix: TASK\n  pad: 4\n  start: 100\n")
        self.assertEqual(plan_id.cmd_next(), "TASK-0100")
        (self.plans / "TASK-0100").mkdir()
        self.assertEqual(plan_id.cmd_next(), "TASK-0101")

    def test_prefix_change_does_not_see_the_old_prefix(self):
        # Ids are scoped to their prefix, so switching prefix restarts rather
        # than continuing someone else's sequence.
        if _yaml is None:
            self.skipTest("needs PyYAML to read plan_id: config")
        self.mk("FEAT-007")
        self.write_cfg("plan_id:\n  prefix: TASK\n  pad: 3\n  start: 1\n")
        self.assertEqual(plan_id.cmd_next(), "TASK-001")

    def test_missing_plans_dir_is_not_an_error(self):
        plan_id.PLANS_DIR = self.root / "nope"
        self.assertEqual(plan_id.cmd_next(), "FEAT-001")


class AdrIdCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        self.adrs = self.root / ".claude" / "adrs"
        self.adrs.mkdir(parents=True)
        self._saved = adr_id.ADRS_DIR
        adr_id.ADRS_DIR = self.adrs

    def tearDown(self):
        adr_id.ADRS_DIR = self._saved
        self._td.cleanup()

    def test_first_id(self):
        self.assertEqual(adr_id.cmd_next(), "ADR-001")

    def test_matches_both_bare_and_slugged_filenames(self):
        (self.adrs / "ADR-001.md").write_text("x")
        (self.adrs / "ADR-002-use-utc-timestamps.md").write_text("x")
        self.assertEqual(adr_id.cmd_next(), "ADR-003")
        self.assertEqual(adr_id.cmd_peek(), "ADR-002-use-utc-timestamps")

    def test_ignores_readme_and_directories(self):
        (self.adrs / "README.md").write_text("x")
        (self.adrs / "ADR-001-a.md").write_text("x")
        (self.adrs / "ADR-002-dir").mkdir()
        self.assertEqual(adr_id.cmd_next(), "ADR-002",
                         "a directory named like an ADR is not an ADR")

    def test_ordering_is_numeric(self):
        (self.adrs / "ADR-009-a.md").write_text("x")
        (self.adrs / "ADR-010-b.md").write_text("x")
        self.assertEqual(adr_id.cmd_next(), "ADR-011")

    def test_missing_dir_is_not_an_error(self):
        adr_id.ADRS_DIR = self.root / "nope"
        self.assertEqual(adr_id.cmd_next(), "ADR-001")
        self.assertEqual(adr_id.cmd_peek(), "")


class DispatchLogCase(unittest.TestCase):
    """Run as the dispatcher does — a subprocess with cwd as the repo root."""

    def run_it(self, root: Path, *args):
        return subprocess.run(
            [sys.executable, str(LIB / "log_dispatch.py"), *args],
            cwd=str(root), capture_output=True, text=True, timeout=60)

    def read(self, root: Path) -> list[dict]:
        p = root / ".claude" / "state" / "logs" / "dispatch.jsonl"
        if not p.is_file():
            return []
        return [json.loads(l) for l in p.read_text().splitlines() if l.strip()]

    def test_writes_a_dispatch_record_and_creates_the_dir(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            r = self.run_it(root, "--run_id", "FEAT-001", "--task_id", "t4",
                            "--role", "implementer", "--complexity", "high",
                            "--role_baseline", "claude-sonnet-4-6",
                            "--model_selected", "claude-opus-4-8",
                            "--routing_applied")
            self.assertEqual(r.returncode, 0, r.stderr)
            rows = self.read(root)
            self.assertEqual(len(rows), 1)
            row = rows[0]
            self.assertEqual(row["event"], "dispatch")
            self.assertEqual(row["task_id"], "t4")
            self.assertEqual(row["model_selected"], "claude-opus-4-8")
            self.assertEqual(row["role_baseline"], "claude-sonnet-4-6")
            self.assertTrue(row["routing_applied"])
            self.assertTrue(row["ts"].endswith("Z"), "timestamps are UTC-suffixed")

    def test_routing_applied_defaults_false_and_blanks_become_null(self):
        # A blank is recorded as null rather than "", so a reader can tell
        # "not routed" from "routed to the empty string".
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            self.run_it(root, "--run_id", "FEAT-001", "--task_id", "t1")
            row = self.read(root)[0]
            self.assertFalse(row["routing_applied"])
            self.assertIsNone(row["role"])
            self.assertIsNone(row["model_selected"])
            self.assertEqual(row["complexity"], "medium", "documented default")

    def test_appends_rather_than_overwrites(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            for tid in ("t1", "t2", "t3"):
                self.run_it(root, "--run_id", "FEAT-001", "--task_id", tid)
            self.assertEqual([r["task_id"] for r in self.read(root)],
                             ["t1", "t2", "t3"])

    def test_invalid_complexity_is_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            r = self.run_it(root, "--complexity", "extreme")
            self.assertNotEqual(r.returncode, 0)
            self.assertEqual(self.read(root), [], "nothing logged on a bad arg")


if __name__ == "__main__":
    unittest.main()
