#!/usr/bin/env python3
"""Tests for installer helpers — sync_managed_hooks (hook propagation)."""
import importlib.util
import json
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
            self.assertIn("python3 .claude/hooks/log_usage.py", cmds)
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
            ag._merge_missing_routing_keys(root)
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
            ag._merge_missing_routing_keys(root)
            ag._merge_missing_routing_keys(root)
            text = (root / ".claude" / ".agentic.yml").read_text()
            self.assertEqual(text.count("never_downgrade:"), 1)
            self.assertIn("[reviewer]", text)  # user value never overwritten

    def test_merge_routing_keys_skips_when_no_block(self):
        # No complexity_routing: at all → the section-level append owns that case.
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); (root / ".claude").mkdir(parents=True)
            p = root / ".claude" / ".agentic.yml"
            p.write_text("enabled_skills: []\n")
            ag._merge_missing_routing_keys(root)
            self.assertNotIn("never_downgrade:", p.read_text())


if __name__ == "__main__":
    unittest.main()
