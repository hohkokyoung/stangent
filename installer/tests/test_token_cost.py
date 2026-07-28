#!/usr/bin/env python3
"""Tests for token_cost.py (pricing) and log_usage.trailing_sidechain_usage."""
import importlib.util
import os
import tempfile
import time
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LIB = REPO / "templates" / ".claude" / "hooks" / "lib"
TC = LIB / "token_cost.py"
LU = REPO / "templates" / ".claude" / "hooks" / "log_usage.py"


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


tc = _load("token_cost", TC)
lu = _load("log_usage", LU)


class TestPricing(unittest.TestCase):
    def test_tokens_of_normalizes(self):
        t = tc.tokens_of({"input_tokens": 10, "output_tokens": 20,
                          "cache_read_input_tokens": 30, "cache_creation_input_tokens": 40})
        self.assertEqual(t, {"input": 10, "output": 20, "cache_read": 30, "cache_write": 40})

    def test_cost_math(self):
        # sonnet: in 3, out 15 per Mtok → 1M in + 1M out = $3 + $15 = $18
        c = tc.cost_usd("claude-sonnet-4-6",
                        {"input": 1_000_000, "output": 1_000_000, "cache_read": 0, "cache_write": 0})
        self.assertAlmostEqual(c, 18.0, places=2)

    def test_prefix_match_by_family(self):
        # a future point release inherits the family rate
        self.assertEqual(tc.rates_for("claude-opus-4-8"), tc.rates_for("claude-opus-4-99"))
        self.assertNotEqual(tc.rates_for("claude-opus-4-8"), tc.rates_for("claude-haiku-4-5"))

    def test_unknown_model_falls_back(self):
        self.assertEqual(tc.rates_for("gpt-4"), tc._FALLBACK)


class TestSubagentUsage(unittest.TestCase):
    def _asst(self, out, model="claude-opus-4-8"):
        return {"type": "assistant",
                "message": {"model": model, "usage": {"output_tokens": out, "input_tokens": 1,
                            "cache_read_input_tokens": out * 10, "cache_creation_input_tokens": 0}}}

    def test_sums_all_assistant_turns(self):
        # a subagent transcript is one agent's whole conversation → sum it all
        records = [self._asst(100),
                   {"type": "user", "message": {}},  # tool result, skipped
                   self._asst(50)]
        tokens, model, turns = lu.subagent_usage(records)
        self.assertEqual(turns, 2)
        self.assertEqual(tokens["output"], 150)
        self.assertEqual(tokens["cache_read"], 1500)  # 1000 + 500
        self.assertEqual(model, "claude-opus-4-8")

    def test_empty_returns_zero(self):
        _, _, turns = lu.subagent_usage([{"type": "user", "message": {}}])
        self.assertEqual(turns, 0)


class TestResolveSubagentTranscript(unittest.TestCase):
    def test_derives_newest_from_main_path(self):
        # main.jsonl + main/subagents/agent-*.jsonl → pick the newest subagent file
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            main = root / "abc123.jsonl"; main.write_text("{}\n")
            sub = root / "abc123" / "subagents"; sub.mkdir(parents=True)
            (sub / "agent-old.jsonl").write_text("{}\n")
            new = sub / "agent-new.jsonl"; new.write_text("{}\n")
            os.utime(new, (time.time() + 10, time.time() + 10))
            got = lu.resolve_subagent_transcript(str(main))
            self.assertEqual(got.name, "agent-new.jsonl")

    def test_uses_path_directly_when_already_subagent(self):
        with tempfile.TemporaryDirectory() as td:
            sub = Path(td) / "subagents"; sub.mkdir()
            f = sub / "agent-x.jsonl"; f.write_text("{}\n")
            self.assertEqual(lu.resolve_subagent_transcript(str(f)), f)

    def test_missing_returns_none(self):
        self.assertIsNone(lu.resolve_subagent_transcript("/no/such/main.jsonl"))


if __name__ == "__main__":
    unittest.main()


class ConfigPathIndependentOfCwdCase(unittest.TestCase):
    """`pricing:` overrides must apply wherever the caller happens to be.

    log_usage.py imports this module, and that hook runs with an unreliable cwd —
    which is why it derives its own paths from __file__. This one did not, so a
    project's pricing overrides silently did not apply and every cost fell back to
    built-in rates: wrong numbers, no error, nothing to notice. The bug only
    appears when cwd is not the repo root, so the test has to move.
    """

    def test_pricing_override_applies_from_a_foreign_cwd(self):
        try:
            import yaml  # noqa: F401
        except ImportError:
            self.skipTest("pricing: overrides need PyYAML — see doctor's dep note")
        import importlib.util
        import os
        import shutil
        import tempfile
        from pathlib import Path
        src = (Path(__file__).resolve().parents[1] / "templates" / ".claude"
               / "hooks" / "lib" / "token_cost.py")
        with tempfile.TemporaryDirectory() as proj, tempfile.TemporaryDirectory() as elsewhere:
            root = Path(proj)
            lib = root / ".claude" / "hooks" / "lib"
            lib.mkdir(parents=True)
            shutil.copy(src, lib / "token_cost.py")
            (root / ".claude" / ".agentic.yml").write_text(
                "pricing:\n  claude-sonnet-4-6:\n    input: 999\n    output: 999\n"
                "    cache_read: 999\n    cache_write: 999\n")
            cwd = os.getcwd()
            try:
                os.chdir(elsewhere)          # the condition that exposed the bug
                spec = importlib.util.spec_from_file_location(
                    "tc_isolated", lib / "token_cost.py")
                tc = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(tc)
                rates = tc.rates_for("claude-sonnet-4-6")
            finally:
                os.chdir(cwd)
        self.assertEqual(rates, (999.0, 999.0, 999.0, 999.0),
                         "override not read — config path is cwd-dependent again")
