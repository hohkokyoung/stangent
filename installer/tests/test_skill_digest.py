#!/usr/bin/env python3
"""Tests for the planner skill-digest builder.

Exercises section extraction and digest assembly without needing a real
project tree — SKILLS_DIR is monkeypatched to a temp dir.

    python3 -m unittest discover installer/tests
"""
import importlib.util
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SD = REPO / "templates" / ".claude" / "hooks" / "lib" / "skill_digest.py"

_spec = importlib.util.spec_from_file_location("skill_digest", SD)
sd = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sd)


class TestExtractSection(unittest.TestCase):
    SAMPLE = (
        "# SKILL: x\n\n"
        "## Purpose\n"
        "Line one.\n"
        "Line two.\n\n"
        "## Rules\n"
        "1. a rule\n\n"
        "## Planner hints\n"
        "- hint a\n"
        "- hint b\n"
    )

    def test_extracts_body(self):
        self.assertEqual(sd.extract_section(self.SAMPLE, "Purpose"), "Line one.\nLine two.")

    def test_stops_at_next_h2(self):
        # Purpose must not bleed into ## Rules.
        self.assertNotIn("rule", sd.extract_section(self.SAMPLE, "Purpose"))

    def test_section_at_eof(self):
        self.assertEqual(sd.extract_section(self.SAMPLE, "Planner hints"), "- hint a\n- hint b")

    def test_case_insensitive(self):
        self.assertEqual(sd.extract_section(self.SAMPLE, "purpose"), "Line one.\nLine two.")

    def test_missing_returns_none(self):
        self.assertIsNone(sd.extract_section(self.SAMPLE, "Anti-patterns"))

    def test_preserves_subheaders(self):
        text = "## Purpose\nintro\n### Detail\nmore\n## Next\nx\n"
        body = sd.extract_section(text, "Purpose")
        self.assertIn("### Detail", body)
        self.assertNotIn("x", body)

    def test_h3_named_like_target_does_not_match(self):
        # A ### with the same name must not be treated as the section start.
        text = "### Purpose\nnope\n## Rules\nr\n"
        self.assertIsNone(sd.extract_section(text, "Purpose"))


class TestBuildDigest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.skills = Path(self._tmp.name)
        self._orig = sd.SKILLS_DIR
        sd.SKILLS_DIR = self.skills

    def tearDown(self):
        sd.SKILLS_DIR = self._orig
        self._tmp.cleanup()

    def _write(self, name, text):
        d = self.skills / name
        d.mkdir()
        (d / "SKILL.md").write_text(text, encoding="utf-8")

    def test_both_sections_present(self):
        self._write("react", "## Purpose\nreact purpose\n## Planner hints\n- check pages\n")
        digest, notes = sd.build_digest(["react"])
        self.assertIn("## react", digest)
        self.assertIn("react purpose", digest)
        self.assertIn("- check pages", digest)
        self.assertEqual(notes, [])

    def test_header_lists_enabled_skills(self):
        self._write("react", "## Purpose\nr\n")
        self._write("fastapi", "## Purpose\nf\n")
        digest, _ = sd.build_digest(["react", "fastapi"])
        self.assertIn("Enabled skills: react, fastapi", digest)

    def test_header_enabled_none_when_empty(self):
        digest, _ = sd.build_digest([])
        self.assertIn("Enabled skills: (none)", digest)

    def test_missing_hints_renders_none(self):
        self._write("owasp", "## Purpose\nsecurity\n## Rules\n1. x\n")
        digest, notes = sd.build_digest(["owasp"])
        self.assertIn("### Planner hints\n(none)", digest)
        self.assertEqual(notes, [])  # missing hints is normal, not a note

    def test_missing_purpose_is_noted(self):
        self._write("weird", "## Rules\n1. x\n")
        digest, notes = sd.build_digest(["weird"])
        self.assertIn("### Purpose\n(none)", digest)
        self.assertTrue(any("missing '## Purpose'" in n for n in notes))

    def test_missing_skill_md_is_skipped(self):
        # e.g. the "project" pseudo-skill has no SKILL.md.
        digest, notes = sd.build_digest(["project"])
        self.assertNotIn("## project", digest)
        self.assertTrue(any("no SKILL.md" in n for n in notes))

    def test_preserves_enabled_order(self):
        self._write("b", "## Purpose\nB\n")
        self._write("a", "## Purpose\nA\n")
        digest, _ = sd.build_digest(["b", "a"])
        self.assertLess(digest.index("## b"), digest.index("## a"))


if __name__ == "__main__":
    unittest.main()
