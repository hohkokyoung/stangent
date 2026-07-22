#!/usr/bin/env python3
"""Tests for retriever query helpers.

Exercises the brute-force path and the ANN fallback without needing the
sqlite-vec extension or an embedding model (stdlib sqlite3 only).

    python3 -m unittest discover installer/tests
"""
import importlib.util
import io
import shutil
import sqlite3
import subprocess
import tempfile
import unittest
from contextlib import redirect_stderr
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
RET = REPO / "templates" / ".claude" / "hooks" / "lib" / "retriever.py"

_spec = importlib.util.spec_from_file_location("retriever", RET)
r = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(r)


def _db(rows):
    """rows: list of (skill, file, anchor, text, vec:list[float])."""
    conn = sqlite3.connect(":memory:")
    conn.execute(
        "CREATE TABLE chunks(id INTEGER PRIMARY KEY, skill TEXT, file TEXT, "
        "anchor TEXT, text TEXT, embedding BLOB)"
    )
    for skill, f, a, t, vec in rows:
        conn.execute(
            "INSERT INTO chunks(skill, file, anchor, text, embedding) VALUES (?,?,?,?,?)",
            (skill, f, a, t, r.pack_f32(vec)),
        )
    conn.commit()
    return conn


class TestBruteQuery(unittest.TestCase):
    def test_ranks_by_cosine(self):
        conn = _db([
            ("fastapi", "a.md", "x", "closest", [1.0, 0.0]),
            ("fastapi", "b.md", "y", "orthogonal", [0.0, 1.0]),
            ("fastapi", "c.md", "z", "near", [0.9, 0.1]),
        ])
        out = r._brute_query(conn, [1.0, 0.0], k=2, scope=[])
        self.assertEqual([o["text"] for o in out], ["closest", "near"])

    def test_scope_filters_by_skill(self):
        conn = _db([
            ("fastapi", "a.md", "x", "keep", [1.0, 0.0]),
            ("react", "b.md", "y", "drop", [1.0, 0.0]),
        ])
        out = r._brute_query(conn, [1.0, 0.0], k=5, scope=["fastapi"])
        self.assertEqual([o["text"] for o in out], ["keep"])

    def test_dimension_mismatch_warns_and_returns_empty(self):
        conn = _db([("fastapi", "a.md", "x", "wrongdim", [1.0, 0.0, 0.0])])
        buf = io.StringIO()
        with redirect_stderr(buf):
            out = r._brute_query(conn, [1.0, 0.0], k=5, scope=[])
        self.assertEqual(out, [])
        self.assertIn("dimension mismatch", buf.getvalue())


class TestKnnFallback(unittest.TestCase):
    def test_knn_returns_none_without_extension(self):
        # A plain connection is not registered in _VEC_LOADED → ANN unavailable.
        conn = _db([("fastapi", "a.md", "x", "t", [1.0, 0.0])])
        self.assertIsNone(r._knn_query(conn, [1.0, 0.0], k=1, scope=[]))


class TestIsExcluded(unittest.TestCase):
    def test_generated_files_excluded_by_default(self):
        d = r._DEFAULT_PROJECT_EXCLUDES
        for path in [
            "mobile/lib/model.g.dart",
            "mobile/lib/model.freezed.dart",
            "src/types/index.d.ts",
            "server/pb/service.pb.go",
            "api/schema_pb2.py",
            "web/schema.generated.ts",
            "components/Button.snap",
        ]:
            self.assertTrue(r._is_excluded(path, d), f"{path} should be excluded")

    def test_real_source_not_excluded(self):
        d = r._DEFAULT_PROJECT_EXCLUDES
        for path in ["mobile/lib/model.dart", "src/index.ts", "server/main.go"]:
            self.assertFalse(r._is_excluded(path, d), f"{path} should be kept")

    def test_config_excludes_are_additive(self):
        # Defaults still apply after a project adds its own exclude.
        excludes = r._DEFAULT_PROJECT_EXCLUDES + ["fixtures/**"]
        self.assertTrue(r._is_excluded("fixtures/data.dart", excludes))
        self.assertTrue(r._is_excluded("lib/model.g.dart", excludes))  # default intact

    def test_negation_re_includes(self):
        # "!tests/**" un-excludes tests that the default "tests/**" caught.
        excludes = r._DEFAULT_PROJECT_EXCLUDES + ["!tests/**"]
        self.assertFalse(r._is_excluded("tests/test_foo.py", excludes))
        # Other defaults still apply.
        self.assertTrue(r._is_excluded("lib/model.g.dart", excludes))

    def test_load_project_globs_merges_defaults(self):
        cfg = {"project_index": {"include": ["**/*.dart"], "exclude": ["fixtures/**"]}}
        include, exclude = r._load_project_globs(cfg)
        self.assertEqual(include, ["**/*.dart"])
        self.assertIn("fixtures/**", exclude)
        self.assertIn("node_modules/**", exclude)  # default preserved
        self.assertIn("*.g.dart", exclude)         # generated default preserved


@unittest.skipIf(shutil.which("git") is None, "git not available")
class TestGitignored(unittest.TestCase):
    def _repo(self, td):
        root = Path(td)
        subprocess.run(["git", "-C", str(root), "init", "-q"], check=True)
        (root / ".gitignore").write_text(".venv/\n.venv.bak-py314/\nbuild/\n")
        for rel in [
            "backend/app.py", "mobile/lib/main.dart",
            ".venv/lib/pkg.py", ".venv.bak-py314/lib/old.py",
            "backend/.venv/nested.py", "build/out.js",
        ]:
            p = root / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text("x")
        return root

    def test_skips_venvs_and_build(self):
        with tempfile.TemporaryDirectory() as td:
            root = self._repo(td)
            rels = [
                "backend/app.py", "mobile/lib/main.dart",
                ".venv/lib/pkg.py", ".venv.bak-py314/lib/old.py",
                "backend/.venv/nested.py", "build/out.js",
            ]
            ignored = r._gitignored(rels, root)
            # Backup and nested venvs — the ones static excludes miss — are caught.
            self.assertIn(".venv.bak-py314/lib/old.py", ignored)
            self.assertIn("backend/.venv/nested.py", ignored)
            self.assertIn(".venv/lib/pkg.py", ignored)
            self.assertIn("build/out.js", ignored)
            # Real source is kept.
            self.assertNotIn("backend/app.py", ignored)
            self.assertNotIn("mobile/lib/main.dart", ignored)

    def test_empty_input(self):
        with tempfile.TemporaryDirectory() as td:
            self.assertEqual(r._gitignored([], Path(td)), set())

    def test_non_git_dir_returns_empty(self):
        with tempfile.TemporaryDirectory() as td:
            # No `git init` → check-ignore exits 128 → index everything.
            self.assertEqual(r._gitignored(["a.py"], Path(td)), set())


class TestResolveScope(unittest.TestCase):
    def test_filter_wins_over_enabled(self):
        cfg = {"enabled_skills": ["react"]}
        self.assertEqual(r._resolve_scope(cfg, ["fastapi"]), ["fastapi"])

    def test_falls_back_to_enabled(self):
        self.assertEqual(r._resolve_scope({"enabled_skills": ["react"]}, None), ["react"])

    def test_empty_when_neither(self):
        self.assertEqual(r._resolve_scope({}, None), [])


if __name__ == "__main__":
    unittest.main()
