#!/usr/bin/env python3
"""Tests for the regression registry.

The registry's whole value is that a green run is falsifiable later, so these
tests concentrate on the two ways that value is lost: a case whose command
cannot be reproduced, and a baseline that quietly moved to match a failure.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import os
import tempfile
import textwrap
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SRC = REPO / "templates" / ".claude" / "hooks" / "lib" / "test_registry.py"

VALID = textwrap.dedent("""\
    ---
    id: TC-001
    title: login reaches the dashboard
    kind: happy
    surface: e2e-mobile
    runner: flutter-skill
    status: active
    revision: 1
    command: "flutter test integration_test/TC-001_login_test.dart -d sim-1"
    expect:
      exit_code: 0
      assertions:
        - "Dashboard is visible"
    covers:
      requirements: ["user can sign in"]
    fixtures: "seeded qa user"
    ---

    # TC-001 — login reaches the dashboard

    ## Intent
    Body prose that must survive a record().
    """)


def load(root: Path):
    """Import a fresh copy bound to `root` — REPO_ROOT is captured at import."""
    os.chdir(root)
    spec = importlib.util.spec_from_file_location(f"tr_{id(root)}", SRC)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class RegistryCase(unittest.TestCase):
    def setUp(self):
        self._cwd = Path.cwd()
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name).resolve()
        (self.root / ".claude" / "tests" / "cases").mkdir(parents=True)
        self.tr = load(self.root)
        self.cases = self.root / ".claude" / "tests" / "cases"

    def tearDown(self):
        os.chdir(self._cwd)
        self._td.cleanup()

    def write(self, name: str, text: str) -> Path:
        p = self.cases / name
        p.write_text(text, encoding="utf-8")
        return p


class TestIdAllocation(RegistryCase):
    def test_first_id_is_tc_001(self):
        self.assertEqual(self.tr.next_id(), "TC-001")

    def test_takes_max_plus_one_not_a_count(self):
        """With a gap, counting would mint an id that already exists."""
        self.write("TC-001-a.md", VALID)
        self.write("TC-003-c.md", VALID.replace("TC-001", "TC-003"))
        self.assertEqual(self.tr.next_id(), "TC-004")


class TestValidation(RegistryCase):
    def problems(self):
        return self.tr.validate_all()[0]

    def test_valid_case_has_no_problems(self):
        self.write("TC-001-login.md", VALID)
        self.assertEqual(self.problems(), [])

    def test_rejects_clock_dependent_command(self):
        self.write("TC-001-login.md", VALID.replace(
            "-d sim-1", "-d sim-1 --seed $(date +%s)"))
        self.assertTrue(any("$(date" in p for p in self.problems()))

    def test_rejects_machine_local_path(self):
        self.write("TC-001-login.md", VALID.replace(
            "integration_test/", "/Users/bob/app/integration_test/"))
        self.assertTrue(any("machine-local" in p for p in self.problems()))

    def test_rejects_case_with_no_expectation(self):
        """A case that cannot fail cannot catch a regression."""
        body = VALID.replace(
            'expect:\n  exit_code: 0\n  assertions:\n    - "Dashboard is visible"\n',
            "expect: {}\n")
        self.assertIn("expect: {}", body, "the fixture edit did not apply")
        self.write("TC-001-login.md", body)
        self.assertTrue(any("exit_code" in p and "assertions" in p
                            for p in self.problems()))

    def test_quarantine_requires_a_stated_reason(self):
        self.write("TC-001-login.md", VALID.replace("status: active",
                                                    "status: quarantined"))
        self.assertTrue(any("flake_reason" in p for p in self.problems()))

        self.write("TC-001-login.md",
                   VALID.replace("status: active",
                                 'status: quarantined\nflake_reason: "APNs sandbox"'))
        self.assertEqual(self.problems(), [])

    def test_id_must_match_filename(self):
        self.write("TC-009-login.md", VALID)  # frontmatter still says TC-001
        self.assertTrue(any("does not match its filename" in p
                            for p in self.problems()))

    def test_duplicate_ids_are_reported(self):
        self.write("TC-001-a.md", VALID)
        self.write("TC-001-b.md", VALID)
        self.assertTrue(any("duplicate id" in p for p in self.problems()))

    def test_unreadable_case_is_reported_not_dropped(self):
        """A case that silently vanishes reads exactly like a case that passed."""
        self.write("TC-001-broken.md", "no frontmatter here")
        problems, n = self.tr.validate_all()
        self.assertEqual(n, 0)
        self.assertTrue(any("frontmatter" in p for p in problems))


class TestRecord(RegistryCase):
    def test_writes_last_run_and_preserves_body_and_expect(self):
        p = self.write("TC-001-login.md", VALID)
        self.tr.record("TC-001", "pass", commit="abc1234")
        c = self.tr.parse_case(p)
        self.assertEqual(c["last_run"]["result"], "pass")
        self.assertEqual(c["last_run"]["commit"], "abc1234")
        self.assertEqual(c["expect"]["assertions"], ["Dashboard is visible"])
        self.assertIn("Body prose that must survive", c["_body"])

    def test_previous_run_moves_into_history(self):
        p = self.write("TC-001-login.md", VALID)
        self.tr.record("TC-001", "pass", commit="aaa")
        self.tr.record("TC-001", "fail", commit="bbb")
        c = self.tr.parse_case(p)
        self.assertEqual(c["last_run"]["result"], "fail")
        self.assertEqual(c["history"][0]["result"], "pass")
        self.assertEqual(c["history"][0]["commit"], "aaa")

    def test_history_is_capped(self):
        p = self.write("TC-001-login.md", VALID)
        for _ in range(self.tr.HISTORY_CAP + 5):
            self.tr.record("TC-001", "pass", commit="x")
        self.assertLessEqual(len(self.tr.parse_case(p)["history"]),
                             self.tr.HISTORY_CAP)

    def test_record_cannot_move_the_baseline(self):
        """The one invariant that makes a later green run mean anything."""
        p = self.write("TC-001-login.md", VALID)
        before = self.tr.parse_case(p)["expect"]
        self.tr.record("TC-001", "fail")
        self.assertEqual(self.tr.parse_case(p)["expect"], before)

    def test_rejects_unknown_result(self):
        self.write("TC-001-login.md", VALID)
        with self.assertRaises(SystemExit):
            self.tr.record("TC-001", "greenish")

    def test_rejects_unknown_case(self):
        with self.assertRaises(SystemExit):
            self.tr.record("TC-404", "pass")


class TestRegressionDetection(RegistryCase):
    def test_passing_case_is_not_a_regression(self):
        self.write("TC-001-login.md", VALID)
        self.tr.record("TC-001", "pass")
        self.assertFalse(self.tr.is_regression(self.tr.find_case("TC-001")))

    def test_was_passing_now_failing_is_a_regression(self):
        self.write("TC-001-login.md", VALID)
        self.tr.record("TC-001", "pass")
        self.tr.record("TC-001", "fail")
        self.assertTrue(self.tr.is_regression(self.tr.find_case("TC-001")))

    def test_never_passed_is_a_gap_not_a_regression(self):
        """Conflating the two makes a red bar unreadable: 'something broke
        today' and 'this was never covered' need different responses."""
        self.write("TC-001-login.md", VALID)
        self.tr.record("TC-001", "fail")
        self.assertFalse(self.tr.is_regression(self.tr.find_case("TC-001")))

    def test_error_after_a_pass_counts_as_a_regression(self):
        self.write("TC-001-login.md", VALID)
        self.tr.record("TC-001", "pass")
        self.tr.record("TC-001", "error")
        self.assertTrue(self.tr.is_regression(self.tr.find_case("TC-001")))


class TestIndex(RegistryCase):
    def test_index_links_every_case(self):
        self.write("TC-001-login.md", VALID)
        self.tr.write_index()
        text = (self.root / ".claude" / "tests" / "INDEX.md").read_text()
        self.assertIn("cases/TC-001-login.md", text)
        self.assertIn("GENERATED", text)

    def test_empty_registry_says_so_rather_than_looking_green(self):
        self.tr.write_index()
        text = (self.root / ".claude" / "tests" / "INDEX.md").read_text()
        self.assertIn("no cases registered yet", text)


if __name__ == "__main__":
    unittest.main()
