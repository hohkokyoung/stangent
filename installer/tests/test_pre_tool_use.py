#!/usr/bin/env python3
"""Tests for the pre_tool_use safety + role hook.

The hook resolves the repo root and role from cwd, so we exercise it as it
really runs: a subprocess with a controlled working directory.

    python3 -m unittest discover installer/tests
"""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
HOOK = REPO / "templates" / ".claude" / "hooks" / "pre_tool_use.py"


class HookCase(unittest.TestCase):
    def run_hook(self, payload, role=None, extra_files=None):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / ".claude" / "state").mkdir(parents=True)
            # Install the hook where it really lives (<repo>/.claude/hooks/) so
            # its __file__-derived REPO_ROOT (parents[2]) resolves to this
            # tempdir — the same layout as a real install. Running the source
            # copy directly would anchor the repo root at the framework tree.
            hooks_dir = root / ".claude" / "hooks"
            hooks_dir.mkdir(parents=True, exist_ok=True)
            hook_copy = hooks_dir / HOOK.name
            shutil.copy(HOOK, hook_copy)
            if role is not None:
                (root / ".claude" / "state" / "current_role.txt").write_text(role)
            for rel, content in (extra_files or {}).items():
                p = root / rel
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(content)
            proc = subprocess.run(
                [sys.executable, str(hook_copy)], input=json.dumps(payload),
                cwd=str(root), capture_output=True, text=True)
            return proc.returncode, proc.stdout

    def assertDenied(self, payload, role=None):
        code, out = self.run_hook(payload, role=role)
        self.assertEqual(code, 2, f"expected deny, got {code}: {out}")

    def assertAllowed(self, payload, role=None):
        code, out = self.run_hook(payload, role=role)
        self.assertEqual(code, 0, f"expected allow, got {code}: {out}")

    @staticmethod
    def bash(cmd):
        return {"tool_name": "Bash", "tool_input": {"command": cmd}}

    @staticmethod
    def write(path, tool="Write"):
        key = "notebook_path" if tool == "NotebookEdit" else "file_path"
        return {"tool_name": tool, "tool_input": {key: path}}


class TestHardSafety(HookCase):
    def test_rm_variants_denied(self):
        for cmd in ("rm -rf build", "rm -fr build", "rm -r -f build",
                    "rm --recursive --force build", "rm -Rf x"):
            self.assertDenied(self.bash(cmd))

    def test_safe_rm_allowed(self):
        self.assertAllowed(self.bash("rm file.txt"))
        self.assertAllowed(self.bash("rm -r build"))   # recursive but not forced
        self.assertAllowed(self.bash("rm -f file.txt"))  # forced but not recursive

    def test_force_push_variants_denied(self):
        for cmd in ("git push --force origin main", "git push --force-with-lease",
                    "git push -f"):
            self.assertDenied(self.bash(cmd))

    def test_read_git_allowed(self):
        self.assertAllowed(self.bash("git push origin main"))
        self.assertAllowed(self.bash("git log --name-only"))
        self.assertAllowed(self.bash("git diff HEAD~1"))

    def test_notebook_path_outside_repo_denied(self):
        self.assertDenied(self.write("/etc/evil.ipynb", tool="NotebookEdit"))

    def test_write_inside_repo_allowed(self):
        self.assertAllowed(self.write(".claude/x.md"))


class TestRoleScopes(HookCase):
    def test_auditor_confined_to_audit_dir(self):
        self.assertAllowed(self.write(".claude/state/audit/A1/findings.md"), role="auditor")
        self.assertDenied(self.write("src/main.py"), role="auditor")
        self.assertDenied(self.write(".claude/state/plans/F/t1.md"), role="auditor")

    def test_debugger_confined_to_debug_dir(self):
        self.assertAllowed(self.write(".claude/state/debug/DBG.md"), role="debugger")
        self.assertDenied(self.write("app/x.py"), role="debugger")

    def test_reviewer_may_edit_plans_not_code(self):
        self.assertAllowed(self.write(".claude/state/plans/F/t1.md", tool="Edit"), role="reviewer")
        self.assertDenied(self.write("src/api.py", tool="Edit"), role="reviewer")

    def test_sketcher_scope(self):
        self.assertAllowed(self.write(".claude/launch.json"), role="sketcher")
        self.assertAllowed(self.write(".claude/design/screens/x.html"), role="sketcher")
        self.assertDenied(self.write("lib/main.dart"), role="sketcher")

    def test_implementer_writes_code_freely(self):
        self.assertAllowed(self.write("src/main.py"), role="implementer")

    def test_git_mutation_blocked_while_role_active(self):
        self.assertDenied(self.bash("git commit -m x"), role="implementer")
        self.assertDenied(self.bash("git merge feat"), role="reviewer")

    def test_branch_helper_allowed(self):
        self.assertAllowed(
            self.bash("python3 .claude/hooks/lib/git_branch.py create FEAT-001"),
            role="implementer")

    def test_fail_open_without_role(self):
        self.assertAllowed(self.write("src/main.py"))
        self.assertAllowed(self.bash("git commit -m x"))


if __name__ == "__main__":
    unittest.main()
