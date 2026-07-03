#!/usr/bin/env python3
"""Tests for retriever query helpers.

Exercises the brute-force path and the ANN fallback without needing the
sqlite-vec extension or an embedding model (stdlib sqlite3 only).

    python3 -m unittest discover installer/tests
"""
import importlib.util
import io
import sqlite3
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
