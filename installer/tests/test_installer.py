#!/usr/bin/env python3
"""Tests for installer helpers — sync_managed_hooks (hook propagation)."""
import importlib.util
import json
import os
import re
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
AGENTIC = REPO / "agentic.py"
_spec = importlib.util.spec_from_file_location("agentic", AGENTIC)
ag = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ag)


def _settings(root, data):
    p = root / ".claude" / "settings.json"
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(data), encoding="utf-8")
    return p


class TestInstallManifest(unittest.TestCase):
    """The manifest is what lets an install answer 'has anyone edited me?' and
    'am I behind my source?' — neither was answerable before it existed."""

    def install(self, root: Path):
        ag.install(root)
        return json.loads((root / ".claude" / ".install.json").read_text())

    def test_written_on_install_with_both_hashes(self):
        with tempfile.TemporaryDirectory() as td:
            mf = self.install(Path(td))
            self.assertTrue(mf["files"], "no system files tracked")
            self.assertIn("agents/reviewer.md", mf["files"])
            for rel, hs in mf["files"].items():
                self.assertIn("tpl", hs, rel)
                self.assertIn("cur", hs, rel)

    def test_cur_hash_is_taken_after_model_stamping(self):
        # The subtlety the two-hash design exists for: the installer rewrites
        # agent frontmatter after copying, so a manifest recorded from the
        # template would flag every agent as locally edited on a fresh install.
        import hashlib
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            mf = self.install(root)
            entry = mf["files"]["agents/reviewer.md"]
            on_disk = hashlib.sha256(
                (root / ".claude" / "agents" / "reviewer.md").read_bytes()
            ).hexdigest()[:16]
            self.assertEqual(entry["cur"], on_disk,
                             "cur must hash the installed (stamped) file")
            self.assertNotEqual(entry["cur"], entry["tpl"],
                                "stamping should make cur differ from tpl")

    def test_only_mirrored_dirs_are_tracked(self):
        # Seed files (.agentic.yml, settings.json) are user config — editing them
        # is expected and must never be reported as drift.
        with tempfile.TemporaryDirectory() as td:
            mf = self.install(Path(td))
            tracked_roots = {rel.split("/")[0] for rel in mf["files"]}
            self.assertTrue(tracked_roots <= set(ag.MIRROR_DIRS), tracked_roots)
            self.assertNotIn(".agentic.yml", mf["files"])
            self.assertNotIn("settings.json", mf["files"])

    def test_records_source_and_commit_not_a_hand_maintained_version(self):
        with tempfile.TemporaryDirectory() as td:
            mf = self.install(Path(td))
            self.assertEqual(mf["source"], str(ag.SCRIPT_DIR))
            # The commit is derived and cannot drift; `system_version` was
            # hand-maintained, never bumped, and is deliberately gone.
            self.assertNotIn("system_version", mf)
            self.assertTrue(mf["source_commit"], "installing from a checkout must record its commit")

    def test_reinstall_refreshes_the_manifest(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            self.install(root)
            # A local edit is erased by the re-install (dirs are mirrored), so the
            # refreshed manifest must match the newly written file, not the edit.
            agent = root / ".claude" / "agents" / "reviewer.md"
            agent.write_text(agent.read_text() + "\nlocal edit\n")
            mf = self.install(root)
            import hashlib
            self.assertEqual(
                mf["files"]["agents/reviewer.md"]["cur"],
                hashlib.sha256(agent.read_bytes()).hexdigest()[:16])
            self.assertNotIn("local edit", agent.read_text())

    def test_uninstall_removes_it(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            self.install(root)
            ag.uninstall(root)
            self.assertFalse((root / ".claude" / ".install.json").exists())


class TestSyncManagedHooks(unittest.TestCase):
    def test_adds_missing_managed_hook(self):
        # An existing install missing the SubagentStop hook should get it, while
        # its user hooks and non-hook settings are preserved.
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            p = _settings(root, {
                "enabledMcpjsonServers": ["agentic_mcp"],
                "hooks": {
                    "PreToolUse": [{"matcher": "*", "hooks": [
                        {"type": "command", "command": "python3 .claude/hooks/pre_tool_use.py", "_agentic_managed": True}]}],
                    "PostToolUse": [{"matcher": "*", "hooks": [
                        {"type": "command", "command": "python3 .claude/hooks/post_tool_use.py", "_agentic_managed": True},
                        {"type": "command", "command": "python3 my_own_hook.py"}]}],  # user hook
                },
            })
            ag.sync_managed_hooks(root)
            data = json.loads(p.read_text())
            events = data["hooks"]
            self.assertIn("SubagentStop", events)  # newly synced
            cmds = [h["command"] for e in events["SubagentStop"] for h in e["hooks"]]
            self.assertIn("sh .claude/py .claude/hooks/log_usage.py", cmds)
            # user hook + other settings preserved
            post_cmds = [h["command"] for e in events["PostToolUse"] for h in e["hooks"]]
            self.assertIn("python3 my_own_hook.py", post_cmds)
            self.assertEqual(data["enabledMcpjsonServers"], ["agentic_mcp"])

    def test_no_duplicate_across_command_format_drift(self):
        # An older install used a ${CLAUDE_PROJECT_DIR} command prefix; syncing must
        # recognize it by script name and NOT add a plain-path duplicate.
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            p = _settings(root, {"hooks": {
                "PreToolUse": [{"matcher": "*", "hooks": [
                    {"type": "command",
                     "command": 'python3 "${CLAUDE_PROJECT_DIR:-.}"/.claude/hooks/pre_tool_use.py',
                     "_agentic_managed": True}]}],
                "PostToolUse": [{"matcher": "*", "hooks": [
                    {"type": "command",
                     "command": 'python3 "${CLAUDE_PROJECT_DIR:-.}"/.claude/hooks/post_tool_use.py',
                     "_agentic_managed": True}]}],
            }})
            ag.sync_managed_hooks(root)
            data = json.loads(p.read_text())
            pre_cmds = [h["command"] for e in data["hooks"]["PreToolUse"] for h in e["hooks"]]
            self.assertEqual(len(pre_cmds), 1, f"duplicated PreToolUse: {pre_cmds}")
            # SubagentStop is genuinely new → added
            self.assertIn("SubagentStop", data["hooks"])

    def test_idempotent(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            p = _settings(root, {"hooks": {}})
            ag.sync_managed_hooks(root)
            first = p.read_text()
            ag.sync_managed_hooks(root)
            self.assertEqual(first, p.read_text())  # second run changes nothing

    def test_no_settings_file_is_safe(self):
        with tempfile.TemporaryDirectory() as td:
            ag.sync_managed_hooks(Path(td))  # must not raise


class TestStampAgentModels(unittest.TestCase):
    YML = (
        "enabled_skills: []\n\n"
        "models:\n"
        "  default:     claude-sonnet-4-6\n"
        "  design-critic: claude-haiku-4-5-20251001   # cheap\n"
        "  planner:     \"\"\n"          # empty → inherit session default
        "\n"
        "complexity_routing:\n"
        "  enabled: true\n"
    )

    def test_parse_models(self):
        m = ag._parse_models(self.YML)
        self.assertEqual(m["default"], "claude-sonnet-4-6")
        self.assertEqual(m["design-critic"], "claude-haiku-4-5-20251001")
        self.assertEqual(m["planner"], "")      # empty kept (means "inherit default")
        self.assertNotIn("enabled", m)          # stopped at dedent (complexity_routing)

    def test_set_frontmatter_model_adds_and_replaces(self):
        md = "---\nname: x\ndescription: d\ntools: Read\n---\n\n# body\n"
        added = ag._set_frontmatter_model(md, "claude-haiku-4-5-20251001")
        self.assertIn("model: claude-haiku-4-5-20251001", added)
        self.assertIn("# body", added)
        # replacing is idempotent-ish (no duplicate model lines)
        again = ag._set_frontmatter_model(added, "claude-sonnet-4-6")
        self.assertEqual(again.count("model:"), 1)
        self.assertIn("model: claude-sonnet-4-6", again)

    def test_stamp_uses_role_model_then_default(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / ".claude").mkdir(parents=True)
            (root / ".claude" / ".agentic.yml").write_text(self.YML)
            agents = root / ".claude" / "agents"; agents.mkdir()
            (agents / "design-critic.md").write_text("---\nname: design-critic\ntools: Read\n---\n\nx\n")
            (agents / "implementer.md").write_text("---\nname: implementer\ntools: Read\n---\n\nx\n")
            (agents / "planner.md").write_text("---\nname: planner\ntools: Read\n---\n\nx\n")
            ag.stamp_agent_models(root)
            dc = (agents / "design-critic.md").read_text()
            impl = (agents / "implementer.md").read_text()
            plan = (agents / "planner.md").read_text()
            self.assertIn("model: claude-haiku-4-5-20251001", dc)   # role model
            self.assertIn("model: claude-sonnet-4-6", impl)          # falls back to default
            self.assertNotIn("model:", plan)                          # planner empty → not stamped

    def test_merge_adds_missing_model_roles(self):
        # a stale config (has models: but missing roles added later) self-heals,
        # existing values + the following section stay intact
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); (root / ".claude").mkdir(parents=True)
            (root / ".claude" / ".agentic.yml").write_text(
                "enabled_skills: []\n\n"
                "models:\n"
                "  default: claude-sonnet-4-6\n"
                "  implementer: claude-opus-4-8\n"   # a user override to preserve
                "\n"
                "git:\n  auto_branch: true\n"
            )
            ag._merge_missing_model_keys(root)
            text = (root / ".claude" / ".agentic.yml").read_text()
            m = ag._parse_models(text)
            self.assertIn("design-critic", m)                     # newer role added from template
            self.assertEqual(m["implementer"], "claude-opus-4-8")  # user override preserved
            self.assertIn("git:", text)                            # next section intact


    def test_merge_adds_missing_routing_keys(self):
        # A project seeded before `never_downgrade` existed keeps an outdated
        # routing policy forever: the section-level upgrade only appends blocks
        # that are absent wholesale, and complexity_routing: is already there.
        # Without the back-fill, low_cap silently pulls gate-owning reviewers
        # down to the cheapest model on low-complexity tasks.
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); (root / ".claude").mkdir(parents=True)
            (root / ".claude" / ".agentic.yml").write_text(
                "enabled_skills: []\n\n"
                "complexity_routing:\n"
                "  enabled: true\n"
                "  low_cap: claude-haiku-4-5-20251001\n"
                "  high_floor: claude-sonnet-4-6\n"
                "\n"
                "git:\n  auto_branch: true\n"
            )
            ag._merge_missing_section_keys(root, "complexity_routing", ("never_downgrade",))
            text = (root / ".claude" / ".agentic.yml").read_text()
            self.assertIn("never_downgrade:", text)
            self.assertIn("reviewer", text.split("never_downgrade:")[1].splitlines()[0])
            self.assertIn("low_cap: claude-haiku-4-5-20251001", text)  # existing kept
            self.assertIn("git:", text)                                # next section intact

    def test_merge_routing_keys_is_idempotent(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); (root / ".claude").mkdir(parents=True)
            (root / ".claude" / ".agentic.yml").write_text(
                "complexity_routing:\n"
                "  enabled: true\n"
                "  never_downgrade: [reviewer]\n"   # a user's own narrower list
            )
            ag._merge_missing_section_keys(root, "complexity_routing", ("never_downgrade",))
            ag._merge_missing_section_keys(root, "complexity_routing", ("never_downgrade",))
            text = (root / ".claude" / ".agentic.yml").read_text()
            self.assertEqual(text.count("never_downgrade:"), 1)
            self.assertIn("[reviewer]", text)  # user value never overwritten

    def test_merge_routing_keys_skips_when_no_block(self):
        # No complexity_routing: at all → the section-level append owns that case.
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); (root / ".claude").mkdir(parents=True)
            p = root / ".claude" / ".agentic.yml"
            p.write_text("enabled_skills: []\n")
            ag._merge_missing_section_keys(root, "complexity_routing", ("never_downgrade",))
            self.assertNotIn("never_downgrade:", p.read_text())


if __name__ == "__main__":
    unittest.main()


class TestAgenticYmlSectionUpgrade(unittest.TestCase):
    """`--upgrade-config` must heal a config seeded before a section existed.

    Config is seed-once, so a section added to the template after a project was
    installed never reaches it. This was four hardcoded blocks naming one
    section each — so `risk_profile`, `design` and `skill_groups` shipped and
    silently never arrived, and a fourth block named `maestro:`, which the
    template does not define at all.
    """

    OLD_SECTIONS = ("risk_profile", "design", "skill_groups", "model_capability_order")

    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        ag.install(self.root)
        self.cfg = self.root / ".claude" / ".agentic.yml"

    def tearDown(self):
        self._td.cleanup()

    def sections(self) -> set:
        return set(re.findall(r"^([a-z_][\w-]*):", self.cfg.read_text(), re.MULTILINE))

    def remove_sections(self, *names):
        t = self.cfg.read_text()
        for n in names:
            t = re.sub(rf"^{n}:.*?(?=^[a-z_][\w-]*:|\Z)", "", t,
                       flags=re.DOTALL | re.MULTILINE)
        self.cfg.write_text(t)

    def test_missing_sections_are_restored(self):
        self.remove_sections(*self.OLD_SECTIONS)
        self.assertTrue(self.sections().isdisjoint(self.OLD_SECTIONS))
        ag.upgrade_config(self.root)
        self.assertTrue(set(self.OLD_SECTIONS) <= self.sections())

    def test_restored_section_keeps_its_explanatory_comments(self):
        # risk_profile: without its comments is four keys with no statement of
        # what data_sensitivity or compliance accept.
        self.remove_sections("risk_profile")
        ag.upgrade_config(self.root)
        text = self.cfg.read_text()
        head = text[:text.index("risk_profile:")]
        self.assertIn("data_sensitivity", head,
                      "the block's documentation must come with it")

    def test_result_is_still_valid_yaml(self):
        try:
            import yaml
        except ImportError:
            self.skipTest("needs PyYAML to validate the merged file")
        self.remove_sections(*self.OLD_SECTIONS)
        ag.upgrade_config(self.root)
        self.assertIsInstance(yaml.safe_load(self.cfg.read_text()), dict)

    def test_user_values_are_never_overwritten(self):
        t = self.cfg.read_text().replace("enabled_skills: []",
                                         "enabled_skills: [react, fastapi]")
        self.cfg.write_text(t)
        ag.upgrade_config(self.root)
        self.assertIn("enabled_skills: [react, fastapi]", self.cfg.read_text())

    def test_upgrade_is_idempotent(self):
        ag.upgrade_config(self.root)
        once = self.cfg.read_text()
        ag.upgrade_config(self.root)
        self.assertEqual(once, self.cfg.read_text())


class TestInterpreterResolution(unittest.TestCase):
    """`.mcp.json` gets a real interpreter, not the name `python3`.

    `python3` is not on PATH on native Windows outside the Store build, so the
    retrieve/get_symbol server never started there. .mcp.json is gitignored and
    regenerated per install, which is what makes an absolute path safe here —
    unlike .claude/py, which is committed and must resolve per call.
    """

    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)

    def tearDown(self):
        self._td.cleanup()

    def mcp_command(self) -> str:
        return json.loads((self.root / ".mcp.json").read_text(
        ))["mcpServers"]["agentic_mcp"]["command"]

    def fake_venv(self, name=".venv"):
        d = self.root / name / "bin"
        d.mkdir(parents=True)
        (d / "python").write_text("#!/bin/sh\n")
        return d / "python"

    def test_install_replaces_the_bare_name(self):
        ag.install(self.root)
        self.assertNotIn(self.mcp_command(), ("python3", "python"))

    def test_a_project_venv_wins_over_the_installing_interpreter(self):
        venv_py = self.fake_venv()
        ag.install(self.root)
        self.assertEqual(self.mcp_command(), str(venv_py))

    def test_venv_path_is_not_symlink_resolved(self):
        # The trap: a real venv's bin/python is a symlink to the base
        # interpreter. Resolving it returns exactly the Python that does NOT
        # have the venv's site-packages, so the stamp would look right and load
        # none of the project's dependencies.
        base = self.root / "base_python"
        base.write_text("#!/bin/sh\n")
        d = self.root / ".venv" / "bin"
        d.mkdir(parents=True)
        link = d / "python"
        try:
            link.symlink_to(base)
        except (OSError, NotImplementedError) as e:
            # Windows refuses symlinks without admin or Developer Mode. The
            # behaviour under test is POSIX-specific anyway — a Windows venv's
            # Scripts/python.exe is a real file, so there is nothing to resolve.
            self.skipTest(f"symlinks unavailable on this platform: {e}")
        self.assertEqual(ag.resolve_interpreter(self.root), str(link))
        self.assertNotEqual(ag.resolve_interpreter(self.root), str(base))

    def test_a_user_pinned_interpreter_is_left_alone(self):
        ag.install(self.root)
        p = self.root / ".mcp.json"
        cfg = json.loads(p.read_text())
        cfg["mcpServers"]["agentic_mcp"]["command"] = "/opt/my/python"
        p.write_text(json.dumps(cfg))
        ag.stamp_mcp_interpreter(self.root)
        self.assertEqual(self.mcp_command(), "/opt/my/python")

    def test_the_resolver_script_is_installed_executable(self):
        ag.install(self.root)
        py = self.root / ".claude" / "py"
        self.assertTrue(py.is_file())
        self.assertTrue(os.access(py, os.X_OK), "commands rely on .claude/py being runnable")


class TestManagedHookRefresh(unittest.TestCase):
    """A managed hook's *invocation* must be updatable, not only addable.

    sync matches by script basename, so an existing entry was found and then
    left alone — meaning a fix to how the hook is invoked reached new installs
    only. That mattered once the hooks moved to `sh .claude/py`: without it they
    run on a PATH interpreter that may lack PyYAML, and pre_tool_use.py then
    reads no gateway.deny patterns and enforces none of them, silently.
    """

    def test_an_old_invocation_is_rewritten_not_duplicated(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            p = _settings(root, {"hooks": {"PreToolUse": [{"matcher": "*", "hooks": [
                {"type": "command",
                 "command": "python3 .claude/hooks/pre_tool_use.py",
                 "_agentic_managed": True}]}]}})
            ag.sync_managed_hooks(root)
            pre = [h["command"] for e in json.loads(p.read_text())["hooks"]["PreToolUse"]
                   for h in e["hooks"]]
            self.assertEqual(len(pre), 1, f"must not duplicate: {pre}")
            self.assertEqual(pre[0], "sh .claude/py .claude/hooks/pre_tool_use.py")

    def test_a_user_hook_with_the_same_script_name_is_untouched(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            p = _settings(root, {"hooks": {"PreToolUse": [{"matcher": "*", "hooks": [
                {"type": "command", "command": "python3 vendor/pre_tool_use.py"}]}]}})
            ag.sync_managed_hooks(root)
            cmds = [h["command"] for e in json.loads(p.read_text())["hooks"]["PreToolUse"]
                    for h in e["hooks"]]
            self.assertIn("python3 vendor/pre_tool_use.py", cmds,
                          "an unmanaged hook is the user's, even on a colliding name")

    def test_refresh_is_idempotent(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            p = _settings(root, {"hooks": {}})
            ag.sync_managed_hooks(root)
            once = p.read_text()
            ag.sync_managed_hooks(root)
            self.assertEqual(once, p.read_text())
