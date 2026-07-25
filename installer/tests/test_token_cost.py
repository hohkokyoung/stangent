#!/usr/bin/env python3
"""Tests for token_cost.py (pricing) and log_usage.trailing_sidechain_usage."""
import importlib.util
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


class TestSidechainAttribution(unittest.TestCase):
    def _asst(self, sidechain, out, model="claude-sonnet-4-6"):
        return {"type": "assistant", "isSidechain": sidechain,
                "message": {"model": model, "usage": {"output_tokens": out, "input_tokens": 1}}}

    def test_trailing_block_only(self):
        # main turn, then a 2-message subagent block at the tail
        records = [
            self._asst(False, 100),           # main (dispatcher) — excluded
            self._asst(True, 10),             # subagent turn 1
            {"type": "user", "isSidechain": True, "message": {}},  # tool result, no usage
            self._asst(True, 20),             # subagent turn 2
        ]
        tokens, model, turns = lu.trailing_sidechain_usage(records)
        self.assertEqual(turns, 2)
        self.assertEqual(tokens["output"], 30)  # 10 + 20, NOT the main's 100
        self.assertEqual(model, "claude-sonnet-4-6")

    def test_no_sidechain_returns_zero(self):
        records = [self._asst(False, 100), self._asst(False, 50)]
        tokens, model, turns = lu.trailing_sidechain_usage(records)
        self.assertEqual(turns, 0)

    def test_earlier_sidechain_block_excluded(self):
        # a prior subagent, then main, then the current subagent — only the last counts
        records = [
            self._asst(True, 999),            # earlier subagent — excluded
            self._asst(False, 5),             # main between dispatches
            self._asst(True, 7),              # current subagent
        ]
        tokens, _, turns = lu.trailing_sidechain_usage(records)
        self.assertEqual(turns, 1)
        self.assertEqual(tokens["output"], 7)


if __name__ == "__main__":
    unittest.main()
