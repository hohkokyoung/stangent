#!/usr/bin/env python3
"""Tests for doctor.check_install_manifest — drift and staleness reporting.

Runs against real installs in temp dirs, because the check is entirely about
comparing files on disk against recorded hashes.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DOCTOR = REPO / "templates" / ".claude" / "hooks" / "lib" / "doctor.py"

_spec = importlib.util.spec_from_file_location("agentic", REPO / "agentic.py")
ag = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ag)


def load_doctor_at(root: Path):
    """Import doctor.py with CLAUDE bound to `root`/.claude (it resolves from cwd)."""
    spec = importlib.util.spec_from_file_location(f"doc_{abs(hash(str(root)))}", DOCTOR)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    mod.REPO_ROOT = root
    mod.CLAUDE = root / ".claude"
    return mod


class ManifestCheckCase(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        ag.install(self.root)
        self.doc = load_doctor_at(self.root)
        self.mf_path = self.root / ".claude" / ".install.json"

    def tearDown(self):
        self._td.cleanup()

    def result(self, name: str) -> dict:
        for r in self.doc.check_install_manifest():
            if r["name"] == name:
                return r
        self.fail(f"no check named {name!r}")

    def edit_manifest(self, fn):
        mf = json.loads(self.mf_path.read_text())
        fn(mf)
        self.mf_path.write_text(json.dumps(mf, indent=2, sort_keys=True))

    # --- the clean case -----------------------------------------------------

    def test_fresh_install_is_clean_on_every_axis(self):
        self.assertEqual(self.result("install manifest")["status"], self.doc.OK)
        self.assertEqual(self.result("local edits to system files")["status"], self.doc.OK)
        self.assertEqual(self.result("up to date with source")["status"], self.doc.OK)

    def test_stamped_agents_are_not_reported_as_edited(self):
        # The regression the two-hash design prevents: the installer rewrites
        # agent frontmatter, so a naive single-hash manifest would flag all 12
        # agents as locally modified the moment they were installed.
        r = self.result("local edits to system files")
        self.assertEqual(r["status"], self.doc.OK, r["detail"])

    # --- drift --------------------------------------------------------------

    def test_local_edit_is_reported(self):
        agent = self.root / ".claude" / "agents" / "reviewer.md"
        agent.write_text(agent.read_text() + "\nmy tweak\n")
        r = self.result("local edits to system files")
        self.assertEqual(r["status"], self.doc.WARN)
        self.assertIn("agents/reviewer.md", r["detail"])
        self.assertIn("OVERWRITTEN", r["detail"],
                      "must say the edit will be lost, not just that it exists")

    def test_deleted_system_file_fails(self):
        (self.root / ".claude" / "commands" / "agentic-status.md").unlink()
        r = self.result("system files present")
        self.assertEqual(r["status"], self.doc.FAIL)
        self.assertIn("agentic-status.md", r["detail"])

    def test_deleted_agent_is_left_to_check_agents(self):
        # The two checks overlap only here. check_agents names the missing role,
        # which is the more useful message, so this one stays quiet rather than
        # reporting the same deletion a second time.
        (self.root / ".claude" / "agents" / "reviewer.md").unlink()
        names = [r["name"] for r in self.doc.check_install_manifest()]
        self.assertNotIn("system files present", names)
        # …and check_agents does still report it, so nothing is lost.
        agent_results = {r["name"]: r for r in self.doc.check_agents()}
        self.assertEqual(agent_results["agent: reviewer"]["status"], self.doc.FAIL)

    def test_seed_files_are_not_drift(self):
        # .agentic.yml is user config; editing it is the documented workflow.
        cfg = self.root / ".claude" / ".agentic.yml"
        cfg.write_text(cfg.read_text() + "\n# my project note\n")
        self.assertEqual(self.result("local edits to system files")["status"],
                         self.doc.OK)

    # --- staleness ----------------------------------------------------------

    def test_source_moving_on_is_reported(self):
        self.edit_manifest(
            lambda mf: mf["files"]["agents/planner.md"].__setitem__("tpl", "0" * 16))
        r = self.result("up to date with source")
        self.assertEqual(r["status"], self.doc.WARN)
        self.assertIn("agents/planner.md", r["detail"])

    def test_new_upstream_file_is_reported(self):
        self.edit_manifest(lambda mf: mf["files"].pop("agents/planner.md"))
        r = self.result("up to date with source")
        self.assertEqual(r["status"], self.doc.WARN)
        self.assertIn("new", r["detail"])

    def test_missing_source_says_it_cannot_tell(self):
        # Must not imply the install is current just because it can't check.
        self.edit_manifest(lambda mf: mf.__setitem__("source", "/nonexistent/x"))
        r = self.result("up to date with source")
        self.assertEqual(r["status"], self.doc.WARN)
        self.assertIn("cannot tell", r["detail"])

    # --- degraded manifests -------------------------------------------------

    def test_absent_manifest_warns_without_crashing(self):
        self.mf_path.unlink()
        r = self.result("install manifest")
        self.assertEqual(r["status"], self.doc.WARN)
        self.assertIn("re-run", r["detail"])

    def test_corrupt_manifest_warns_without_crashing(self):
        self.mf_path.write_text("{not json")
        r = self.result("install manifest")
        self.assertEqual(r["status"], self.doc.WARN)


if __name__ == "__main__":
    unittest.main()
