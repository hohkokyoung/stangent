#!/usr/bin/env python3
"""Tests for git_branch.py checkpoint — the per-task recovery boundary.

Runs against real temporary git repos, because every interesting case here is
about what git actually does (a dirty tree, a switched branch, a rejecting
pre-commit hook) rather than about our own string handling.

    python3 -m unittest discover installer/tests
"""
import contextlib
import importlib.util
import io
import subprocess
import tempfile
import unittest
from pathlib import Path

try:
    import yaml as _yaml  # noqa: F401  — presence probe for skipUnless
    HAVE_YAML = True
except ImportError:
    HAVE_YAML = False

MODULE = (Path(__file__).resolve().parents[1] / "templates" / ".claude"
          / "hooks" / "lib" / "git_branch.py")

AGENTIC_YML = """\
git:
  auto_branch: true
  branch_template: "feat/{run_id}"
  base_branch: ""
  fail_on_wip: true
  checkpoint_commits: true
"""


def load_module_at(root: Path):
    """Import git_branch.py with REPO_ROOT bound to `root`.

    The module resolves REPO_ROOT from cwd at import time, so each test gets its
    own freshly-imported copy pointed at its own temp repo.
    """
    spec = importlib.util.spec_from_file_location(f"gb_{root.name}", MODULE)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    mod.REPO_ROOT = root
    mod.AGENTIC_YML = root / ".claude" / ".agentic.yml"
    return mod


class CheckpointCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        (self.root / ".claude").mkdir()
        (self.root / ".claude" / ".agentic.yml").write_text(AGENTIC_YML)
        self.git("init", "-q")
        self.git("config", "user.email", "t@example.com")
        self.git("config", "user.name", "T")
        # .claude/state is the run's working memory and must never be committed.
        (self.root / ".gitignore").write_text(".claude/state/\n")
        (self.root / "seed.txt").write_text("seed\n")
        self.git("add", "-A")
        self.git("commit", "-qm", "seed")
        self.git("switch", "-qc", "feat/FEAT-001")
        self.gb = load_module_at(self.root)

    def tearDown(self):
        self._td.cleanup()

    def git(self, *args) -> subprocess.CompletedProcess:
        return subprocess.run(["git", *args], cwd=self.root,
                              capture_output=True, text=True)

    def head_subject(self) -> str:
        return self.git("log", "-1", "--pretty=%s").stdout.strip()

    def count_commits(self) -> int:
        return len(self.git("log", "--oneline").stdout.strip().splitlines())

    def dirty(self, name="app.txt", text="x\n"):
        (self.root / name).write_text(text)

    # --- the happy path -----------------------------------------------------

    def test_commits_task_work(self):
        self.dirty()
        before = self.count_commits()
        self.assertEqual(self.gb.cmd_checkpoint("FEAT-001", "t4", "implementer"), 0)
        self.assertEqual(self.count_commits(), before + 1)
        self.assertIn("FEAT-001 t4", self.head_subject())
        self.assertIn("implementer", self.head_subject())
        self.assertFalse(self.gb.working_tree_dirty(), "tree should be clean after")

    def test_state_dir_is_never_committed(self):
        # Run working memory is gitignored; a checkpoint must not sweep it in.
        state = self.root / ".claude" / "state" / "plans"
        state.mkdir(parents=True)
        (state / "t1.md").write_text("secret working memory\n")
        self.dirty()
        self.gb.cmd_checkpoint("FEAT-001", "t1")
        tracked = self.git("ls-files").stdout
        self.assertNotIn(".claude/state", tracked)

    # --- the guards ---------------------------------------------------------

    def test_refuses_to_commit_on_the_wrong_branch(self):
        # The case that matters: if the branch moved mid-run, committing would
        # land the run's work on whatever is checked out.
        self.git("switch", "-qc", "some-other-branch")
        self.dirty()
        before = self.count_commits()
        self.assertEqual(self.gb.cmd_checkpoint("FEAT-001", "t4"), 0)
        self.assertEqual(self.count_commits(), before,
                         "must not commit onto an unexpected branch")
        self.assertTrue(self.gb.working_tree_dirty(), "work must be left in place")

    def test_accepts_the_versioned_branch_name(self):
        # cmd_create appends -v2/-v3 when the branch already exists.
        self.git("switch", "-qc", "feat/FEAT-001-v2")
        self.dirty()
        self.assertEqual(self.gb.cmd_checkpoint("FEAT-001", "t4"), 0)
        self.assertIn("FEAT-001 t4", self.head_subject())

    @unittest.skipUnless(HAVE_YAML, "reading `checkpoint_commits: false` needs PyYAML")
    def test_disabled_in_config_skips(self):
        self.gb.AGENTIC_YML.write_text(AGENTIC_YML.replace(
            "checkpoint_commits: true", "checkpoint_commits: false"))
        self.dirty()
        before = self.count_commits()
        self.assertEqual(self.gb.cmd_checkpoint("FEAT-001", "t4"), 0)
        self.assertEqual(self.count_commits(), before)

    def test_without_pyyaml_config_is_ignored_but_says_so(self):
        # The honest degraded behaviour, pinned rather than left to chance: with
        # no YAML parser the opt-out cannot be read, so checkpointing proceeds —
        # and the only thing standing between that and a silent surprise is the
        # warning. Assert both halves.
        saved, self.gb.yaml = self.gb.yaml, None
        try:
            self.gb.AGENTIC_YML.write_text(AGENTIC_YML.replace(
                "checkpoint_commits: true", "checkpoint_commits: false"))
            self.dirty()
            before = self.count_commits()
            err = io.StringIO()
            with contextlib.redirect_stderr(err):
                self.gb.cmd_checkpoint("FEAT-001", "t4")
            self.assertEqual(self.count_commits(), before + 1,
                             "without a parser the opt-out cannot be honoured")
            self.assertIn("IGNORED", err.getvalue(),
                          "an unreadable opt-out must not be silent")
        finally:
            self.gb.yaml = saved

    def test_no_warning_when_there_is_no_config_to_ignore(self):
        saved, self.gb.yaml = self.gb.yaml, None
        try:
            self.gb.AGENTIC_YML.unlink()
            self.dirty()
            err = io.StringIO()
            with contextlib.redirect_stderr(err):
                self.gb.cmd_checkpoint("FEAT-001", "t4")
            self.assertEqual(err.getvalue(), "", "nothing is being overridden")
        finally:
            self.gb.yaml = saved

    def test_absent_key_defaults_to_enabled(self):
        # Projects seeded before the key existed must still get the boundary.
        self.gb.AGENTIC_YML.write_text(
            AGENTIC_YML.replace("  checkpoint_commits: true\n", ""))
        self.dirty()
        before = self.count_commits()
        self.gb.cmd_checkpoint("FEAT-001", "t4")
        self.assertEqual(self.count_commits(), before + 1)

    def test_clean_tree_is_a_no_op(self):
        before = self.count_commits()
        self.assertEqual(self.gb.cmd_checkpoint("FEAT-001", "t4"), 0)
        self.assertEqual(self.count_commits(), before)

    # --- never abort the build ---------------------------------------------

    def test_rejecting_pre_commit_hook_does_not_fail_the_build(self):
        hook = self.root / ".git" / "hooks" / "pre-commit"
        hook.write_text("#!/bin/sh\nexit 1\n")
        hook.chmod(0o755)
        self.dirty()
        before = self.count_commits()
        self.assertEqual(self.gb.cmd_checkpoint("FEAT-001", "t4"), 0,
                         "a rejected commit must not abort a 3-hour build")
        self.assertEqual(self.count_commits(), before)

    def test_not_a_git_repo_is_a_no_op(self):
        with tempfile.TemporaryDirectory() as td:
            plain = Path(td)
            (plain / ".claude").mkdir()
            (plain / ".claude" / ".agentic.yml").write_text(AGENTIC_YML)
            gb = load_module_at(plain)
            self.assertEqual(gb.cmd_checkpoint("FEAT-001", "t4"), 0)


if __name__ == "__main__":
    unittest.main()
