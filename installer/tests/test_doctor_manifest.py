#!/usr/bin/env python3
"""Tests for doctor.check_install_manifest — drift and staleness reporting.

Runs against real installs in temp dirs, because the check is entirely about
comparing files on disk against recorded hashes.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import os
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


class ModelIdCheckCase(unittest.TestCase):
    """`_check_model_ids` — the one config error nothing else can surface.

    An unrecognised model id does not error; it silently falls back to the
    session model, and cost telemetry stays correct because it prices what the
    transcript reports. So the config states an intent it never delivered and
    nothing anywhere disagrees.
    """

    def setUp(self):
        self.doc = load_doctor_at(Path("/nonexistent"))  # pure-function tests

    def statuses(self, cfg):
        return [(c["name"], c["status"]) for c in self.doc._check_model_ids(cfg)]

    LADDER = ["claude-haiku-4-5-20251001", "claude-sonnet-5", "claude-opus-5"]

    def test_all_ranked_ids_pass(self):
        cfg = {"models": {"default": "claude-sonnet-5", "architect": "claude-opus-5"},
               "model_capability_order": self.LADDER}
        self.assertEqual(self.statuses(cfg), [("config: model ids", "ok")])

    def test_the_observed_failure_is_caught(self):
        # The real incident: every role on an id from an older generation, the
        # ladder already updated. Ran for weeks with nothing to notice.
        cfg = {"models": {"default": "claude-sonnet-4-6",
                          "architect": "claude-opus-4-8",
                          "tester": "claude-haiku-4-5-20251001"},
               "model_capability_order": self.LADDER}
        result = self.doc._check_model_ids(cfg)
        self.assertEqual(result[0]["status"], "warn")
        detail = result[0]["detail"]
        self.assertIn("claude-sonnet-4-6", detail)
        self.assertIn("claude-opus-4-8", detail)
        self.assertIn("architect", detail)          # names the affected role
        self.assertNotIn("claude-haiku", detail)    # the valid id is not flagged

    def test_empty_string_means_inherit_and_is_not_flagged(self):
        cfg = {"models": {"default": "claude-sonnet-5", "tester": "", "reviewer": "  "},
               "model_capability_order": self.LADDER}
        self.assertEqual(self.statuses(cfg), [("config: model ids", "ok")])

    def test_missing_ladder_warns_rather_than_passing_silently(self):
        cfg = {"models": {"default": "anything-at-all"}}
        result = self.doc._check_model_ids(cfg)
        self.assertEqual(result[0]["status"], "warn")
        self.assertIn("model_capability_order", result[0]["detail"])

    def test_no_models_block_is_not_an_error(self):
        self.assertEqual(self.doc._check_model_ids({}), [])


class McpCredentialCheckCase(unittest.TestCase):
    """Both ways a credential goes missing must be reported.

    Moving the template to ${VAR} made "unset env var" the new failure mode. It
    did not retire the old one — a project seeded earlier still carries
    REPLACE_WITH_ placeholders — and checking only the new shape reported a real
    install's unfilled dbhub DSN as [ok].
    """

    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        ag.install(self.root)
        self.doc = load_doctor_at(self.root)

    def tearDown(self):
        self._td.cleanup()

    def write_mcp(self, servers):
        (self.root / ".mcp.json").write_text(json.dumps({"mcpServers": servers}))

    def cred_checks(self):
        return {c["name"]: c for c in self.doc.check_mcp_json()
                if c["name"].endswith("credentials")}

    def test_unfilled_placeholder_is_warned(self):
        self.write_mcp({"dbhub": {"command": "npx",
                                  "args": ["--dsn", "REPLACE_WITH_DSN"]}})
        c = self.cred_checks()["mcp:dbhub credentials"]
        self.assertEqual(c["status"], "warn")
        self.assertIn("REPLACE_WITH_", c["detail"])

    def test_unset_env_var_is_warned(self):
        self.write_mcp({"dbhub": {"command": "npx",
                                  "args": ["--dsn", "${DEFINITELY_UNSET_VAR_X}"]}})
        c = self.cred_checks()["mcp:dbhub credentials"]
        self.assertEqual(c["status"], "warn")
        self.assertIn("DEFINITELY_UNSET_VAR_X", c["detail"])

    def test_a_set_env_var_passes(self):
        os.environ["AGENTIC_TEST_DSN"] = "postgres://x"
        try:
            self.write_mcp({"dbhub": {"command": "npx",
                                      "args": ["--dsn", "${AGENTIC_TEST_DSN}"]}})
            self.assertEqual(self.cred_checks()["mcp:dbhub credentials"]["status"], "ok")
        finally:
            del os.environ["AGENTIC_TEST_DSN"]

    def test_a_filled_literal_passes(self):
        self.write_mcp({"dbhub": {"command": "npx", "args": ["--dsn", "postgres://x"]}})
        self.assertEqual(self.cred_checks()["mcp:dbhub credentials"]["status"], "ok")
