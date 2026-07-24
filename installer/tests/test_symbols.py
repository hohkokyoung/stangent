#!/usr/bin/env python3
"""Tests for the dependency-free structural symbol extractor.

Exercises extract_symbols across indent + brace families without touching a
real project tree.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SY = REPO / "templates" / ".claude" / "hooks" / "lib" / "symbols.py"

_spec = importlib.util.spec_from_file_location("symbols", SY)
s = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(s)


def one(src, name, ext):
    ms = s.extract_symbols(src, name, ext)
    return ms[0] if ms else None


class TestPython(unittest.TestCase):
    SRC = (
        "@decorator\n"
        "def charge(account, amount):\n"
        "    if amount < 0:\n"
        "        raise ValueError()\n"
        "    return Txn(account, amount)\n"
        "\n"
        "class Ledger:\n"
        "    def post(self, e):\n"
        "        return e\n"
    )

    def test_function_includes_decorator(self):
        m = one(self.SRC, "charge", ".py")
        self.assertEqual(m["kind"], "function")
        self.assertTrue(m["text"].startswith("@decorator"))
        self.assertIn("return Txn", m["text"])

    def test_body_stops_before_next_top_level(self):
        m = one(self.SRC, "charge", ".py")
        self.assertNotIn("class Ledger", m["text"])

    def test_class(self):
        m = one(self.SRC, "Ledger", ".py")
        self.assertEqual(m["kind"], "class")
        self.assertIn("def post", m["text"])

    def test_nested_method(self):
        m = one(self.SRC, "post", ".py")
        self.assertEqual(m["text"].strip(), "def post(self, e):\n        return e".strip())

    def test_missing(self):
        self.assertIsNone(one(self.SRC, "nope", ".py"))


class TestBrace(unittest.TestCase):
    TS = (
        "export function charge(id: string): Result {\n"
        '  if (!id) { throw new Error("no {brace} here"); }\n'
        "  return ok(id);\n"
        "}\n"
        "const helper = (a) => {\n"
        "  return a + 1;\n"
        "};\n"
        "class Billing {\n"
        "  refund(txn: string): void {\n"
        "    this.log(txn);\n"
        "  }\n"
        "}\n"
    )

    def test_function_brace_matched(self):
        m = one(self.TS, "charge", ".ts")
        self.assertEqual(m["kind"], "function")
        self.assertEqual(m["line"], 1)
        self.assertEqual(m["end_line"], 4)  # closes at the lone '}'

    def test_brace_in_string_ignored(self):
        # The '{brace}' inside the string must not unbalance the scan.
        m = one(self.TS, "charge", ".ts")
        self.assertIn("return ok(id);", m["text"])
        self.assertNotIn("const helper", m["text"])

    def test_arrow_const(self):
        m = one(self.TS, "helper", ".ts")
        self.assertEqual(m["kind"], "function")
        self.assertIn("return a + 1;", m["text"])

    def test_class_and_method(self):
        cls = one(self.TS, "Billing", ".ts")
        self.assertEqual(cls["kind"], "type")
        self.assertIn("refund", cls["text"])
        meth = one(self.TS, "refund", ".ts")
        self.assertEqual(meth["kind"], "method")
        self.assertIn("this.log(txn);", meth["text"])

    def test_go_receiver_method_and_struct(self):
        go = "func (s *Svc) Charge(id string) error {\n\treturn nil\n}\ntype Account struct {\n\tID string\n}\n"
        self.assertEqual(one(go, "Charge", ".go")["kind"], "function")
        self.assertEqual(one(go, "Account", ".go")["kind"], "type")

    def test_rust_fn_and_struct(self):
        rs = "fn charge(id: u32) -> bool {\n    true\n}\nstruct Account {\n    id: u32,\n}\n"
        self.assertEqual(one(rs, "charge", ".rs")["kind"], "function")
        self.assertEqual(one(rs, "Account", ".rs")["kind"], "type")


class TestFamilyDetection(unittest.TestCase):
    def test_known_exts(self):
        self.assertEqual(s._family(".py"), "indent")
        self.assertEqual(s._family(".tsx"), "brace")
        self.assertIsNone(s._family(".md"))

    def test_unsupported_language_not_guessed(self):
        # Ruby is def…end, not brace — must NOT be mis-extracted as brace.
        ruby = "def charge(id)\n  Txn.new(id)\nend\n"
        self.assertIsNone(s._family(".rb"))
        self.assertEqual(s.extract_symbols(ruby, "charge", ".rb"), [])

    def test_multiple_matches_returned(self):
        src = "def f():\n    return 1\n\ndef g():\n    return 2\n"
        # two different names, one each
        self.assertEqual(len(s.extract_symbols(src, "f", ".py")), 1)
        self.assertEqual(len(s.extract_symbols(src, "g", ".py")), 1)


if __name__ == "__main__":
    unittest.main()
