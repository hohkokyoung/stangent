#!/usr/bin/env python3
"""Tests for sweep_plan — deterministic batching for an exhaustive review.

The module exists so coverage is arithmetic rather than a claim, which puts the
weight on two properties: the same tree must always produce the same batches, and
every in-scope file must land in exactly one of them. A batching bug does not
look like a bug — it looks like a clean review of less code than you think.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

MODULE = (Path(__file__).resolve().parents[1] / "templates" / ".claude"
          / "hooks" / "lib" / "sweep_plan.py")


def load_at(root: Path):
    spec = importlib.util.spec_from_file_location(f"sw_{abs(hash(str(root)))}", MODULE)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    mod.REPO_ROOT = root
    return mod


class SweepCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        self.sw = load_at(self.root)

    def tearDown(self):
        self._td.cleanup()

    def write(self, rel, size=1000):
        p = self.root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text("x" * size)
        return p

    def files(self, patterns=("*.dart",), scope=None):
        return self.sw.iter_files(scope or self.root, list(patterns))

    # --- selection ----------------------------------------------------------

    def test_generated_and_vendored_files_are_excluded(self):
        self.write("lib/a.dart")
        self.write("lib/a.g.dart")
        self.write("lib/a.freezed.dart")
        self.write("build/gen.dart")
        self.write(".dart_tool/x.dart")
        got = [p.name for p in self.files()]
        self.assertEqual(got, ["a.dart"],
                         "reviewing generated output produces findings nobody can act on")

    def test_order_is_stable(self):
        for n in ("d/c.dart", "a/b.dart", "z/y.dart", "a/a.dart"):
            self.write(n)
        self.assertEqual([p.as_posix() for p in self.files()],
                         [p.as_posix() for p in self.files()])
        self.assertEqual([p.name for p in self.files()],
                         ["a.dart", "b.dart", "c.dart", "y.dart"])

    def test_a_pattern_matching_nothing_is_an_error_not_an_empty_sweep(self):
        self.write("lib/a.dart")
        plan, code = self.sw.build_plan("", ["*.kt"], 20, 80_000)
        self.assertEqual(code, 2)
        self.assertIn("no files", plan["error"])

    # --- the coverage property ---------------------------------------------

    def test_every_file_lands_in_exactly_one_batch(self):
        for i in range(37):
            self.write(f"lib/f{i:02d}/a.dart", size=9000)
        files = self.files()
        batches = self.sw.plan_batches(files, 20, 80_000)
        flat = [f for b in batches for f in b["files"]]
        self.assertEqual(len(flat), len(files), "a file was dropped or duplicated")
        self.assertEqual(len(set(flat)), len(flat), "a file is in two batches")

    def test_a_file_larger_than_the_cap_still_gets_a_batch(self):
        # Dropping it would silently break the one property this module sells.
        self.write("lib/huge.dart", size=200_000)
        self.write("lib/small.dart", size=100)
        batches = self.sw.plan_batches(self.files(), 20, 80_000)
        flat = [f for b in batches for f in b["files"]]
        self.assertEqual(len(flat), 2)
        self.assertTrue(any("huge.dart" in f for f in flat))

    def test_batching_is_reproducible(self):
        for i in range(25):
            self.write(f"lib/mod{i % 5}/f{i}.dart", size=7000)
        a = self.sw.plan_batches(self.files(), 20, 80_000)
        b = self.sw.plan_batches(self.files(), 20, 80_000)
        self.assertEqual(a, b, "two sweeps must plan byte-identical batches")

    def test_batches_respect_both_caps(self):
        for i in range(30):
            self.write(f"lib/d/f{i:02d}.dart", size=5000)
        for b in self.sw.plan_batches(self.files(), 4, 80_000):
            self.assertLessEqual(len(b["files"]), 4)
        for b in self.sw.plan_batches(self.files(), 100, 12_000):
            self.assertLessEqual(b["chars"], 12_000 + 5000)  # +1 file that closed it

    def test_paths_are_repo_relative(self):
        # Batch lists get handed to an agent and stored in a report; absolute
        # paths from one machine are noise everywhere else.
        self.write("lib/a.dart")
        b = self.sw.plan_batches(self.files(), 20, 80_000)[0]
        self.assertEqual(b["files"], ["lib/a.dart"])

    def test_a_directory_stays_together_when_it_fits(self):
        for i in range(3):
            self.write(f"lib/widgets/w{i}.dart", size=1000)
        for i in range(3):
            self.write(f"lib/screens/s{i}.dart", size=1000)
        batches = self.sw.plan_batches(self.files(), 20, 80_000)
        # Sibling files are what make "this component is styled two ways"
        # visible at all; scattering them by size alone would hide it.
        for b in batches:
            dirs = {Path(f).parent.as_posix() for f in b["files"]}
            self.assertLessEqual(len(dirs), 2, f"batch spans {dirs}")


    def test_fallback_matching_nothing_names_the_fix(self):
        # A Go/Rust/Java project before indexing: the fallback covers web/mobile
        # only, so it matches zero files. detect_stack knows those languages, so
        # the error must point at /agentic-index rather than read as "unsupported".
        self.write("cmd/main.go")
        plan, code = self.sw.build_plan("", self.sw.FALLBACK_PATTERNS, 20, 80_000,
                                        "fallback (no project.yml — run /agentic-index)")
        self.assertEqual(code, 2)
        self.assertIn("/agentic-index", plan["error"])

    def test_a_deliberate_pattern_matching_nothing_gets_no_such_hint(self):
        self.write("cmd/main.go")
        plan, code = self.sw.build_plan("", ["*.kt"], 20, 80_000, "explicit --pattern")
        self.assertEqual(code, 2)
        self.assertNotIn("/agentic-index", plan["error"])

    # --- verification of a finished sweep -----------------------------------

    def sweep_of(self, n_files=25):
        for i in range(n_files):
            self.write(f"lib/d{i % 4}/f{i:02d}.dart", size=9000)
        plan, code = self.sw.build_plan("", ["*.dart"], 20, 80_000)
        self.assertEqual(code, 0)
        return plan

    def test_verify_passes_when_every_batch_reported(self):
        plan = self.sweep_of()
        bd = self.root / "batches"; bd.mkdir()
        for b in plan["batches"]:
            (bd / f"b{b['index']:02d}.md").write_text("findings")
        rep, code = self.sw.verify_sweep(plan, bd)
        self.assertEqual(code, 0)
        self.assertTrue(rep["complete"])
        self.assertEqual(rep["files_covered"], rep["files_planned"])

    def test_verify_fails_on_a_batch_that_never_reported(self):
        # The failure that reads exactly like a complete review.
        plan = self.sweep_of()
        bd = self.root / "batches"; bd.mkdir()
        for b in plan["batches"][:-1]:
            (bd / f"b{b['index']:02d}.md").write_text("findings")
        rep, code = self.sw.verify_sweep(plan, bd)
        self.assertEqual(code, 1)
        self.assertFalse(rep["complete"])
        self.assertEqual(rep["missing_batches"], [plan["batches"][-1]["index"]])
        self.assertLess(rep["files_covered"], rep["files_planned"])

    def test_verify_fails_when_no_batches_ran_at_all(self):
        plan = self.sweep_of()
        rep, code = self.sw.verify_sweep(plan, self.root / "nope")
        self.assertEqual(code, 1)
        self.assertEqual(rep["batches_reported"], 0)

    # --- CLI ----------------------------------------------------------------

    def test_cli_plan_emits_json(self):
        self.write("lib/a.dart", size=100)
        r = subprocess.run([sys.executable, str(MODULE), "plan", "", "--pattern",
                            "*.dart", "--json"], cwd=str(self.root),
                           capture_output=True, text=True, timeout=60)
        self.assertEqual(r.returncode, 0, r.stderr)
        out = json.loads(r.stdout)
        self.assertEqual(out["total_files"], 1)
        self.assertEqual(out["batches"][0]["files"], ["lib/a.dart"])

    def test_cli_verify_exits_nonzero_on_an_incomplete_sweep(self):
        self.write("lib/a.dart", size=100)
        r = subprocess.run([sys.executable, str(MODULE), "verify", "",
                            "batches", "--pattern", "*.dart"],
                           cwd=str(self.root), capture_output=True, text=True,
                           timeout=60)
        self.assertEqual(r.returncode, 1)
        self.assertIn("never reported", r.stdout)


if __name__ == "__main__":
    unittest.main()


class DetectedPatternsCase(unittest.TestCase):
    """Which files a sweep covers must come from detection, not a guess.

    A hardcoded default silently sweeps the wrong language on any stack nobody
    anticipated — and then reports complete coverage of it, which is the failure
    mode this whole module exists to remove.
    """

    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        (self.root / ".claude" / "state").mkdir(parents=True)
        self.sw = load_at(self.root)

    def tearDown(self):
        self._td.cleanup()

    def project_yml(self, text):
        (self.root / ".claude" / "state" / "project.yml").write_text(text)

    def test_reads_globs_detected_by_agentic_index(self):
        self.project_yml("app_id: x\nproject_index_globs:\n  - '**/*.dart'\n"
                         "  - '**/*.py'\ntest_framework: maestro\n")
        pats, src = self.sw.detected_patterns()
        self.assertEqual(pats, ["*.dart", "*.py"], "**/ prefix should normalise")
        self.assertIn("detected", src)

    def test_block_ends_at_the_next_top_level_key(self):
        self.project_yml("project_index_globs:\n  - '**/*.dart'\n"
                         "test_framework: maestro\napp_id: com.example\n")
        self.assertEqual(self.sw.detected_patterns()[0], ["*.dart"],
                         "a following key must not be swallowed as a glob")

    def test_missing_project_yml_falls_back_and_says_so(self):
        pats, src = self.sw.detected_patterns()
        self.assertEqual(pats, self.sw.FALLBACK_PATTERNS)
        self.assertIn("/agentic-index", src, "must name the fix, not fail silently")

    def test_project_yml_without_globs_falls_back(self):
        self.project_yml("app_id: x\ntest_framework: maestro\n")
        pats, src = self.sw.detected_patterns()
        self.assertEqual(pats, self.sw.FALLBACK_PATTERNS)
        self.assertIn("fallback", src)

    def test_unparseable_file_degrades_rather_than_raising(self):
        # No PyYAML dependency here on purpose; a odd file must not except.
        self.project_yml("\x00\x01 not yaml at all\n")
        self.assertEqual(self.sw.detected_patterns()[0], self.sw.FALLBACK_PATTERNS)
