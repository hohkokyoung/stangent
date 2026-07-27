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

# The same configuration as a dict, for tests that exercise logic taking a config
# rather than the loading of one. Kept honest by test_config_dict_matches_yaml.
CONFIG_DICT = {
    "models": {
        "default": "claude-sonnet-4-6",
        "implementer": "claude-sonnet-4-6",
        "reviewer": "claude-haiku-4-5-20251001",
        "tester": "claude-haiku-4-5-20251001",
    },
    "complexity_routing": {
        "enabled": True,
        "low_cap": "claude-haiku-4-5-20251001",
        "high_floor": "claude-sonnet-4-6",
    },
    "model_capability_order": [
        "claude-haiku-4-5-20251001",
        "claude-sonnet-4-6",
        "claude-opus-4-8",
    ],
    "retrieval": {"default_k": 6, "role_k": {"reviewer": 2, "tester": 3}},
    "skill_groups": {"test": ["playwright", "maestro"]},
}

try:
    import yaml as _yaml  # noqa: F401  — presence probe for skipUnless
    HAVE_YAML = True
except ImportError:
    HAVE_YAML = False


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
        # Built natively rather than parsed from CONFIG. resolve_model takes a
        # plain dict, so parsing YAML here was incidental — and it made ten tests
        # of pure routing logic ERROR (not skip) on an interpreter without PyYAML,
        # which is a supported configuration the runtime has fallbacks for.
        # test_config_dict_matches_yaml keeps this in step with CONFIG.
        self.cfg = CONFIG_DICT

    @unittest.skipUnless(HAVE_YAML, "needs PyYAML to parse the reference config")
    def test_config_dict_matches_yaml(self):
        # The one place the two representations are compared. Without this the
        # dict could drift from CONFIG and every routing test would keep passing
        # against a config that no longer matches what gets written to disk.
        import yaml
        self.assertEqual(CONFIG_DICT, yaml.safe_load(CONFIG))

    def test_low_caps_sonnet_to_haiku(self):
        m, base, applied = dp.resolve_model("implementer", "low", self.cfg, None)
        self.assertEqual(m, "claude-haiku-4-5-20251001")
        self.assertTrue(applied)

    def test_low_leaves_haiku_role_alone(self):
        m, base, applied = dp.resolve_model("tester", "low", self.cfg, None)
        self.assertEqual(m, "claude-haiku-4-5-20251001")
        self.assertFalse(applied)

    def test_low_cap_does_not_downgrade_gate_roles(self):
        # A gate-owning role on a low-complexity task must keep its configured
        # model. A cheap reviewer's failure mode is reporting a checklist item as
        # cleared — which reads as verified and stops anyone looking again — and
        # a task marked "low" is exactly where that goes unnoticed.
        cfg = dict(self.cfg)
        cfg["models"] = {**cfg["models"], "reviewer": "claude-sonnet-4-6",
                         "design-critic": "claude-sonnet-4-6"}
        for role in ("reviewer", "design-critic", "architect", "security-reviewer"):
            m, base, applied = dp.resolve_model(role, "low", cfg, None)
            self.assertEqual(m, base, f"{role} was downgraded by low_cap")
            self.assertFalse(applied)

    def test_gate_roles_still_routed_up_by_high_floor(self):
        # never_downgrade blocks the cap, not the floor.
        m, base, applied = dp.resolve_model("reviewer", "high", self.cfg, None)
        self.assertEqual(m, "claude-sonnet-4-6")
        self.assertTrue(applied)

    def test_never_downgrade_is_overridable(self):
        cfg = dict(self.cfg)
        cfg["complexity_routing"] = {**cfg["complexity_routing"], "never_downgrade": []}
        cfg["models"] = {**cfg["models"], "reviewer": "claude-sonnet-4-6"}
        m, base, applied = dp.resolve_model("reviewer", "low", cfg, None)
        self.assertEqual(m, "claude-haiku-4-5-20251001")
        self.assertTrue(applied)

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


class TestWithoutPyYAML(unittest.TestCase):
    """What actually happens when PyYAML is absent.

    The runtime carries `yaml = None` fallbacks throughout and the README lists
    pyyaml as a dependency to install, so running without it is a real state — but
    it was only ever *unverified*, not supported: the tests that touched config
    errored out, which looks identical to the path being broken.
    """

    def setUp(self):
        self._saved = dp.yaml
        dp.yaml = None

    def tearDown(self):
        dp.yaml = self._saved

    def test_config_load_degrades_to_empty(self):
        # Not a crash, and not a partial parse — an empty config, which is what
        # every downstream default is written against.
        self.assertEqual(dp.load_config(), {})

    def test_routing_falls_back_to_the_session_model(self):
        # With no config, the role has no configured model, so the session model
        # is used and complexity routing is off (it is opt-in via config).
        m, base, applied = dp.resolve_model("implementer", "low", {}, "claude-sonnet-4-6")
        self.assertEqual(m, "claude-sonnet-4-6")
        self.assertEqual(base, "claude-sonnet-4-6")
        self.assertFalse(applied, "routing must not engage without a config to enable it")

    def test_k_falls_back_to_the_documented_default(self):
        self.assertEqual(dp.resolve_k("reviewer", None, {}), 6)
        self.assertEqual(dp.resolve_k("reviewer", 10, {}), 10, "task k still honoured")

    def test_skills_pass_through_untouched(self):
        # No skill_groups to filter against, so a tester keeps what it was given
        # rather than silently losing its testing method.
        self.assertEqual(dp.resolve_skills("tester", ["playwright", "fastapi"], {}),
                         ["playwright", "fastapi"])


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

    # Split from one test: the runnable set is computed from the task files and
    # needs no config, while model/k come from .agentic.yml and therefore need a
    # YAML parser. Together they made ordering coverage vanish on an interpreter
    # without PyYAML — the case this whole split exists to keep covered.
    _RESOLUTION_FILES = {
        "t1.md": task_md("t1", role="implementer", status="done"),
        "t2.md": task_md("t2", role="reviewer", complexity="low", depends_on=["t1"]),
        "t3.md": task_md("t3", role="tester", depends_on=["t2"]),
    }

    def test_runnable_set_needs_no_config(self):
        code, plan = self._run(dict(self._RESOLUTION_FILES))
        self.assertEqual(code, 0)
        self.assertFalse(plan["cycle"])
        ids = [r["task_id"] for r in plan["runnable"]]
        self.assertEqual(ids, ["t2"])  # t1 done, t3 waits on t2

    @unittest.skipUnless(HAVE_YAML, "model/k resolution reads .agentic.yml")
    def test_resolution_reads_config(self):
        code, plan = self._run(dict(self._RESOLUTION_FILES))
        self.assertEqual(code, 0)
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

    def test_deferred_dep_not_runnable(self):
        code, plan = self._run({
            "t1.md": task_md("t1", status="deferred"),
            "t2.md": task_md("t2", depends_on=["t1"]),
        })
        self.assertEqual(code, 0)
        self.assertEqual(plan["runnable"], [])  # t1 parked, t2 frozen with it
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
