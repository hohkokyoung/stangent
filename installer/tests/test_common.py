#!/usr/bin/env python3
"""Tests for the shared hook helpers (lib/common.py)."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
COMMON = REPO / "templates" / ".claude" / "hooks" / "lib" / "common.py"
_spec = importlib.util.spec_from_file_location("common", COMMON)
c = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(c)


class TestReadText(unittest.TestCase):
    def test_reads_and_strips(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "f.txt"; p.write_text("  hello \n")
            self.assertEqual(c.read_text_or_none(p), "hello")

    def test_missing_is_none(self):
        self.assertIsNone(c.read_text_or_none(Path("/no/such/file")))

    def test_empty_is_none(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "e.txt"; p.write_text("   \n")
            self.assertIsNone(c.read_text_or_none(p))


class TestReadJsonl(unittest.TestCase):
    def test_parses_and_skips_bad_lines(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "l.jsonl"
            p.write_text('{"a":1}\nnot json\n\n{"b":2}\n')
            self.assertEqual(c.read_jsonl(p), [{"a": 1}, {"b": 2}])

    def test_missing_is_empty(self):
        self.assertEqual(c.read_jsonl(Path("/no/such/file.jsonl")), [])


if __name__ == "__main__":
    unittest.main()
