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


if __name__ == "__main__":
    unittest.main()
