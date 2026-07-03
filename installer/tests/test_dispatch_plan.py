#!/usr/bin/env python3
"""Unit + CLI tests for dispatch_plan.py.

Run from the repo root with no third-party deps:
    python3 -m unittest discover installer/tests
"""
import importlib.util
import json
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LIB = REPO / "templates" / ".claude" / "hooks" / "lib" / "dispatch_plan.py"

spec = importlib.util.spec_from_file_location("dispatch_plan", LIB)
dp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dp)


CONFIG = textwrap.dedent("""
    models:
      default: claude-sonnet-4-6
      implementer: claude-sonnet-4-6
      reviewer: claude-haiku-4-5-20251001
      tester: claude-haiku-4-5-20251001
    complexity_routing:
      enabled: true
      low_cap: claude-haiku-4-5-20251001
      high_floor: claude-sonnet-4-6
    model_capability_order:
      - claude-haiku-4-5-20251001
      - claude-sonnet-4-6
      - claude-opus-4-8
    retrieval:
      default_k: 6
      role_k:
        reviewer: 2
        tester: 3
    skill_groups:
      test: [playwright, maestro]
""")


def task_md(tid, role="implementer", status="pending", complexity="medium",
            depends_on=None, skills=None, k=None):
    depends_on = depends_on or []
    skills = skills if skills is not None else ["project"]
    return textwrap.dedent(f"""\
        ---
        id: {tid}
        role: {role}
        status: {status}
        complexity: {complexity}
        k: {k if k is not None else "null"}
        skills_to_load: {json.dumps(skills)}
        depends_on: {json.dumps(depends_on)}
        ---
        ## Goal
        x
        """)


class TestParseFrontmatter(unittest.TestCase):
    def test_flow_and_scalar(self):
        fm = dp.parse_frontmatter(task_md("t1", depends_on=["s1"], skills=["fastapi", "project"]))
        self.assertEqual(fm["id"], "t1")
        self.assertEqual(fm["depends_on"], ["s1"])
        self.assertEqual(fm["skills_to_load"], ["fastapi", "project"])

    def test_hash_in_quoted_value_fallback(self):
        # Fallback parser must not truncate a value at a '#' inside quotes.
        saved = dp.yaml
        dp.yaml = None
        try:
            text = ('---\nid: t1\nintent: "fix #42 crash"\n'
                    'status: pending  # a real comment\n---\nbody\n')
            fm = dp.parse_frontmatter(text)
            self.assertEqual(fm["intent"], "fix #42 crash")
            self.assertEqual(fm["status"], "pending")
        finally:
            dp.yaml = saved

    def test_minimal_parser_without_yaml(self):
        # Force the no-yaml path to prove the fallback parser handles the shapes
        # the planner emits (flow lists + block lists + quoted scalars).
        saved = dp.yaml
        dp.yaml = None
        try:
            text = textwrap.dedent("""\
                ---
                id: t2
                role: tester
                intent: "do a thing"
                status: pending
                depends_on: [t1]
                skills_to_load:
                  - playwright
                  - project
                ---
                body
                """)
            fm = dp.parse_frontmatter(text)
            self.assertEqual(fm["id"], "t2")
            self.assertEqual(fm["intent"], "do a thing")
            self.assertEqual(fm["depends_on"], ["t1"])
            self.assertEqual(fm["skills_to_load"], ["playwright", "project"])
        finally:
            dp.yaml = saved


class TestTopoSort(unittest.TestCase):
    def _tasks(self, edges):
        return [{"id": k, "depends_on": v} for k, v in edges.items()]

    def test_linear(self):
        order, cycle = dp.topo_sort(self._tasks({"t1": [], "t2": ["t1"], "t3": ["t2"]}))
        self.assertIsNone(cycle)
        self.assertLess(order.index("t1"), order.index("t2"))
        self.assertLess(order.index("t2"), order.index("t3"))

    def test_diamond(self):
        order, cycle = dp.topo_sort(self._tasks(
            {"a": [], "b": ["a"], "c": ["a"], "d": ["b", "c"]}))
        self.assertIsNone(cycle)
        self.assertLess(order.index("a"), order.index("d"))
        self.assertLess(order.index("b"), order.index("d"))

    def test_cycle_detected(self):
        order, cycle = dp.topo_sort(self._tasks({"t1": ["t2"], "t2": ["t1"]}))
        self.assertEqual(order, [])
        self.assertIsNotNone(cycle)


class TestRouting(unittest.TestCase):
    def setUp(self):
        import yaml
        self.cfg = yaml.safe_load(CONFIG)

    def test_low_caps_sonnet_to_haiku(self):
        m, base, applied = dp.resolve_model("implementer", "low", self.cfg, None)
        self.assertEqual(m, "claude-haiku-4-5-20251001")
        self.assertTrue(applied)

    def test_low_leaves_haiku_role_alone(self):
        m, base, applied = dp.resolve_model("reviewer", "low", self.cfg, None)
        self.assertEqual(m, "claude-haiku-4-5-20251001")
        self.assertFalse(applied)

    def test_medium_unchanged(self):
        m, base, applied = dp.resolve_model("implementer", "medium", self.cfg, None)
        self.assertEqual(m, "claude-sonnet-4-6")
        self.assertFalse(applied)

    def test_high_floors_haiku_to_sonnet(self):
        m, base, applied = dp.resolve_model("reviewer", "high", self.cfg, None)
        self.assertEqual(m, "claude-sonnet-4-6")
        self.assertTrue(applied)

    def test_routing_disabled(self):
        cfg = dict(self.cfg)
        cfg["complexity_routing"] = {"enabled": False}
        m, base, applied = dp.resolve_model("reviewer", "high", cfg, None)
        self.assertEqual(m, "claude-haiku-4-5-20251001")
        self.assertFalse(applied)

    def test_unknown_model_treated_as_sonnet(self):
        cfg = dict(self.cfg)
        cfg["models"] = {"default": "some-future-model"}
        # high floor is sonnet; unknown ranks as sonnet, so no change.
        m, base, applied = dp.resolve_model("implementer", "high", cfg, None)
        self.assertEqual(m, "some-future-model")
        self.assertFalse(applied)

    def test_k_and_skills(self):
        self.assertEqual(dp.resolve_k("reviewer", None, self.cfg), 2)
        self.assertEqual(dp.resolve_k("implementer", None, self.cfg), 6)
        self.assertEqual(dp.resolve_k("implementer", 10, self.cfg), 10)
        self.assertEqual(dp.resolve_skills("tester", ["playwright", "fastapi"], self.cfg), ["playwright"])
        self.assertEqual(dp.resolve_skills("tester", ["fastapi"], self.cfg), [])
        self.assertEqual(dp.resolve_skills("implementer", ["fastapi"], self.cfg), ["fastapi"])


class TestBuildPlanCLI(unittest.TestCase):
    def _run(self, files, run_id="FEAT-001", args=None):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / ".claude").mkdir()
            (root / ".claude" / ".agentic.yml").write_text(CONFIG)
            run = root / ".claude" / "state" / "plans" / run_id
            run.mkdir(parents=True)
            for name, content in files.items():
                (run / name).write_text(content)
            proc = subprocess.run(
                [sys.executable, str(LIB), run_id] + (args or []),
                cwd=str(root), capture_output=True, text=True)
            out = json.loads(proc.stdout) if proc.stdout.strip() else {}
            return proc.returncode, out

    def test_runnable_and_resolution(self):
        code, plan = self._run({
            "t1.md": task_md("t1", role="implementer", status="done"),
            "t2.md": task_md("t2", role="reviewer", complexity="low", depends_on=["t1"]),
            "t3.md": task_md("t3", role="tester", depends_on=["t2"]),
        })
        self.assertEqual(code, 0)
        self.assertFalse(plan["cycle"])
        ids = [r["task_id"] for r in plan["runnable"]]
        self.assertEqual(ids, ["t2"])  # t1 done, t3 waits on t2
        t2 = plan["runnable"][0]
        self.assertEqual(t2["model"], "claude-haiku-4-5-20251001")  # reviewer already haiku
        self.assertEqual(t2["k"], 2)

    def test_blocked_dep_not_runnable(self):
        code, plan = self._run({
            "t1.md": task_md("t1", status="blocked"),
            "t2.md": task_md("t2", depends_on=["t1"]),
        })
        self.assertEqual(code, 0)
        self.assertEqual(plan["runnable"], [])
        self.assertEqual(plan["blocked_by_dep"], ["t2"])

    def test_dangling_dep_not_runnable(self):
        code, plan = self._run({
            "t1.md": task_md("t1", status="done"),
            "t2.md": task_md("t2", depends_on=["t1", "t99"]),  # t99 does not exist
        })
        self.assertEqual(code, 0)
        self.assertEqual(plan["runnable"], [])  # not dispatched out of order
        self.assertEqual(plan["invalid_deps"], [{"task_id": "t2", "missing": ["t99"]}])

    def test_cycle_exit_3(self):
        code, plan = self._run({
            "t1.md": task_md("t1", depends_on=["t2"]),
            "t2.md": task_md("t2", depends_on=["t1"]),
        })
        self.assertEqual(code, 3)
        self.assertTrue(plan["cycle"])

    def test_task_flag_refuses_when_deps_pending(self):
        code, plan = self._run({
            "t1.md": task_md("t1", status="pending"),
            "t2.md": task_md("t2", depends_on=["t1"]),
        }, args=["--task", "t2"])
        self.assertEqual(code, 4)
        self.assertIn("not runnable", plan["error"])


if __name__ == "__main__":
    unittest.main()
