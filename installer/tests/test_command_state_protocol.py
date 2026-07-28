#!/usr/bin/env python3
"""The command corpus must speak one dialect of dispatch-state hygiene.

`state.py` owns clearing, but every command is prose, so nothing stopped fifteen
of them from hand-writing `rm -f .claude/state/current_*.txt` instead — each with
a slightly different file list. That drift is invisible in review (every variant
looks plausible on its own line) and only shows up at runtime, as a leaked
`current_role.txt` that keeps pre_tool_use.py's write-whitelist armed against the
user's own edits, or a leaked `current_run.txt` that mistags later tool calls
into a dead run's log.

These tests check the corpus, not any one command: the system has thorough
machinery for verifying an *agent's* claims and, before this file, none for
verifying its own commands' consistency.

    python3 -m unittest discover installer/tests
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
COMMANDS = REPO / "templates" / ".claude" / "commands"

STATE_FILES = ("current_run.txt", "current_task.txt",
               "current_role.txt", "current_model.txt")
CLEAR_CMD = "state.py clear"


def command_files():
    return sorted(COMMANDS.glob("agentic-*.md"))


def arms(text: str, name: str) -> bool:
    """True if the command writes `name` (the `printf … > …/name` handshake)."""
    return bool(re.search(rf">\s*\.claude/state/{re.escape(name)}", text))


class ClearingGoesThroughStatePy(unittest.TestCase):
    def test_no_command_hand_rolls_rm_of_state_files(self):
        offenders = [p.name for p in command_files()
                     if "rm -f .claude/state" in p.read_text(encoding="utf-8")]
        self.assertEqual(offenders, [], (
            "these commands hand-roll state clearing; use "
            "`python3 .claude/hooks/lib/state.py clear` (all four files) or "
            "`… clear --agent` (task/role/model, keeping the run id). A "
            "hand-written list drifts and is not portable off POSIX."))

    def test_every_command_that_arms_state_also_clears_it(self):
        for p in command_files():
            text = p.read_text(encoding="utf-8")
            armed = [n for n in STATE_FILES if arms(text, n)]
            if not armed:
                continue
            with self.subTest(command=p.name):
                self.assertIn(CLEAR_CMD, text, (
                    f"{p.name} arms {armed} but never clears — the state "
                    f"outlives the command and mistags everything after it."))

    def test_a_command_that_arms_the_run_also_arms_role_and_model(self):
        # Partial arming is how /agentic-sweep ended up logging every call in
        # the most expensive command with a null model: the run id was set, so
        # the calls were logged, but nothing said which model produced them.
        for p in command_files():
            text = p.read_text(encoding="utf-8")
            if not arms(text, "current_run.txt"):
                continue
            # Read-only consumers of the run id are not dispatchers.
            if not arms(text, "current_role.txt"):
                continue
            with self.subTest(command=p.name):
                self.assertTrue(arms(text, "current_model.txt"), (
                    f"{p.name} arms current_run.txt + current_role.txt but not "
                    f"current_model.txt — its tool calls log with a null model "
                    f"and its cost cannot be attributed."))

    def test_teardown_clear_is_unqualified(self):
        # `clear --agent` deliberately keeps current_run.txt. A command whose
        # ONLY clear is the --agent form never releases the run context.
        for p in command_files():
            text = p.read_text(encoding="utf-8")
            if CLEAR_CMD not in text:
                continue
            clears = re.findall(rf"{re.escape(CLEAR_CMD)}(\s+--agent)?", text)
            with self.subTest(command=p.name):
                self.assertTrue(any(not c.strip() for c in clears), (
                    f"{p.name} only ever clears with --agent, so "
                    f"current_run.txt survives the command."))


class StatePyBackstheContract(unittest.TestCase):
    """The tests above are only meaningful if state.py offers both scopes."""

    def test_state_py_exposes_both_clear_scopes(self):
        src = (REPO / "templates" / ".claude" / "hooks" / "lib" / "state.py"
               ).read_text(encoding="utf-8")
        self.assertIn("AGENT_FILES", src)
        self.assertIn("--agent", src)

    def test_agent_scope_is_a_strict_subset_that_keeps_the_run(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location(
            "state_mod", REPO / "templates" / ".claude" / "hooks" / "lib" / "state.py")
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        self.assertNotIn("current_run.txt", mod.AGENT_FILES)
        self.assertTrue(set(mod.AGENT_FILES) < set(mod.STATE_FILES))


if __name__ == "__main__":
    unittest.main()
