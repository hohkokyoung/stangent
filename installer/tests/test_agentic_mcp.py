#!/usr/bin/env python3
"""Tests for agentic_mcp — the stdio MCP server and its retrieve budget.

The budget is enforced HERE rather than in each agent's prompt, precisely because
a prompt can be ignored; that makes this server the only thing actually holding
the "one retrieve per task" contract, and it had no tests.

The server is driven for real over stdin/stdout JSON-RPC. `retriever.py` and
`symbols.py` are stubbed in the temp repo, so nothing here needs fastembed or
sqlite-vec — what is under test is the server's own protocol and budget logic,
not retrieval quality.

    python3 -m unittest discover installer/tests
"""
import contextlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SERVER = REPO / "templates" / ".claude" / "mcp" / "agentic_mcp.py"

_spec = importlib.util.spec_from_file_location("agentic_mcp_mod", SERVER)
mcp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mcp)

STUB = """#!/usr/bin/env python3
import json, sys
print(json.dumps([{"stub": " ".join(sys.argv[1:])}]))
"""


class ServerCase(unittest.TestCase):
    """Each test spins the real server in a temp repo and speaks JSON-RPC to it."""

    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.root = Path(self._td.name)
        lib = self.root / ".claude" / "hooks" / "lib"
        lib.mkdir(parents=True)
        (self.root / ".claude" / "mcp").mkdir(parents=True)
        (self.root / ".claude" / "state").mkdir(parents=True)
        shutil.copy(SERVER, self.root / ".claude" / "mcp" / "agentic_mcp.py")
        for name in ("retriever.py", "symbols.py"):
            (lib / name).write_text(STUB)

    def tearDown(self):
        self._td.cleanup()

    def state(self, name, text, mtime=None):
        p = self.root / ".claude" / "state" / name
        p.write_text(text)
        if mtime is not None:
            os.utime(p, (mtime, mtime))

    def talk(self, requests: list[dict]) -> list[dict]:
        """Send requests, return parsed replies (in order)."""
        proc = subprocess.run(
            [sys.executable, str(self.root / ".claude" / "mcp" / "agentic_mcp.py")],
            input="\n".join(json.dumps(r) for r in requests) + "\n",
            cwd=str(self.root), capture_output=True, text=True, timeout=60)
        return [json.loads(l) for l in proc.stdout.splitlines() if l.strip()]

    @contextlib.contextmanager
    def session(self):
        """A live server process, one request at a time.

        Needed wherever the assertion is about state the server holds BETWEEN
        calls — the retrieve budget is an in-process dict, so batching requests
        through `talk()` cannot exercise it.
        """
        proc = subprocess.Popen(
            [sys.executable, str(self.root / ".claude" / "mcp" / "agentic_mcp.py")],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            cwd=str(self.root), text=True, bufsize=1)

        class S:
            @staticmethod
            def send(req):
                proc.stdin.write(json.dumps(req) + "\n")
                proc.stdin.flush()
                line = proc.stdout.readline()
                if not line:
                    raise AssertionError("server closed stdout — it died mid-session")
                return json.loads(line)

        try:
            yield S
        finally:
            proc.stdin.close()
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=10)
            proc.stdout.close()
            proc.stderr.close()

    @staticmethod
    def call(req_id, tool, **args):
        return {"jsonrpc": "2.0", "id": req_id, "method": "tools/call",
                "params": {"name": tool, "arguments": args}}

    @staticmethod
    def payload(reply) -> list:
        """Unwrap the MCP content envelope back to the tool's JSON."""
        return json.loads(reply["result"]["content"][0]["text"])

    # ---- protocol ----------------------------------------------------------

    def test_initialize_and_tools_list(self):
        replies = self.talk([
            {"jsonrpc": "2.0", "id": 1, "method": "initialize"},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
        ])
        self.assertEqual(replies[0]["result"]["serverInfo"]["name"], "agentic_mcp")
        names = {t["name"] for t in replies[1]["result"]["tools"]}
        self.assertEqual(names, {"retrieve", "get_symbol"},
                         "the code-context layer is exactly these two tools")

    def test_unknown_tool_and_method_are_errors(self):
        replies = self.talk([
            self.call(1, "delete_everything"),
            {"jsonrpc": "2.0", "id": 2, "method": "tools/nope"},
        ])
        self.assertIn("unknown tool", replies[0]["error"]["message"])
        self.assertIn("unknown method", replies[1]["error"]["message"])

    def test_notifications_get_no_reply_and_junk_does_not_kill_the_server(self):
        # The server is long-lived: one malformed line must not end the session.
        proc = subprocess.run(
            [sys.executable, str(self.root / ".claude" / "mcp" / "agentic_mcp.py")],
            input='{"jsonrpc":"2.0","method":"notifications/initialized"}\n'
                  'not json at all\n'
                  '\n'
                  '{"jsonrpc":"2.0","id":9,"method":"initialize"}\n',
            cwd=str(self.root), capture_output=True, text=True, timeout=60)
        replies = [json.loads(l) for l in proc.stdout.splitlines() if l.strip()]
        self.assertEqual(len(replies), 1, "only the initialize should be answered")
        self.assertEqual(replies[0]["id"], 9)

    def test_malformed_k_does_not_crash_the_server(self):
        replies = self.talk([self.call(1, "retrieve", query="x", k="not-a-number"),
                             {"jsonrpc": "2.0", "id": 2, "method": "initialize"}])
        self.assertEqual(len(replies), 2, "server survived and kept answering")

    # ---- the retrieve budget ----------------------------------------------

    def test_budget_allows_two_then_refuses(self):
        self.state("current_run.txt", "FEAT-001")
        self.state("current_task.txt", "t1")
        self.state("current_role.txt", "implementer")
        replies = self.talk([self.call(i, "retrieve", query="q") for i in (1, 2, 3)])
        self.assertNotIn("error", str(self.payload(replies[0])))
        self.assertNotIn("error", str(self.payload(replies[1])))
        third = self.payload(replies[2])
        self.assertIn("budget exceeded", third[0]["error"])
        self.assertIn("insufficient_context", third[0]["error"],
                      "must tell the agent what to do, not just refuse")

    def test_ambient_calls_are_not_budgeted(self):
        # No current_task.txt — exploratory use outside a dispatch must not be
        # capped, or ordinary work in a repo hits a limit meant for one task.
        self.state("current_run.txt", "FEAT-001")
        replies = self.talk([self.call(i, "retrieve", query="q") for i in range(1, 6)])
        for i, r in enumerate(replies):
            self.assertNotIn("budget exceeded", str(self.payload(r)), f"call {i+1}")

    # These two MUST run inside a single server process. The budget lives in an
    # in-memory dict, so a fresh subprocess starts with an empty one — driving
    # them with separate `talk()` calls would pass even if the key were wrong,
    # which is exactly the mistake this comment exists to stop being repeated.

    def test_a_retried_task_gets_a_fresh_budget(self):
        # The mtime in the budget key exists for this: the server outlives a
        # dispatch, so a blocked-then-retried task would otherwise be denied its
        # very first retrieve by the count from the dead attempt.
        self.state("current_run.txt", "FEAT-001")
        self.state("current_task.txt", "t1", mtime=1_000_000)
        with self.session() as s:
            s.send(self.call(1, "retrieve", query="q"))
            s.send(self.call(2, "retrieve", query="q"))
            self.assertIn("budget exceeded",
                          str(self.payload(s.send(self.call(3, "retrieve", query="q")))))
            # The dispatcher rewrites current_task.txt on re-dispatch → new mtime.
            self.state("current_task.txt", "t1", mtime=2_000_000)
            after = s.send(self.call(4, "retrieve", query="q"))
        self.assertNotIn("budget exceeded", str(self.payload(after)),
                         "a re-dispatch must not inherit the dead attempt's count")

    def test_budget_is_per_task_not_per_run(self):
        self.state("current_run.txt", "FEAT-001")
        self.state("current_task.txt", "t1", mtime=1_000_000)
        with self.session() as s:
            for i in (1, 2, 3):
                s.send(self.call(i, "retrieve", query="q"))
            self.state("current_task.txt", "t2", mtime=1_000_001)
            after = s.send(self.call(4, "retrieve", query="q"))
        self.assertNotIn("budget exceeded", str(self.payload(after)),
                         "the next task in the same run starts fresh")

    def test_budget_survives_within_one_process(self):
        # The control for the two above: with the task state untouched, the count
        # must persist across calls in a single process. If this ever fails, the
        # two tests above are proving nothing.
        self.state("current_run.txt", "FEAT-001")
        self.state("current_task.txt", "t1", mtime=1_000_000)
        with self.session() as s:
            s.send(self.call(1, "retrieve", query="q"))
            s.send(self.call(2, "retrieve", query="q"))
            third = s.send(self.call(3, "retrieve", query="q"))
        self.assertIn("budget exceeded", str(self.payload(third)))

    def test_get_symbol_is_not_charged_against_the_budget(self):
        # Structural fetch is deterministic and cheap; charging it would push
        # agents back to Reading whole files, which is what it exists to avoid.
        self.state("current_run.txt", "FEAT-001")
        self.state("current_task.txt", "t1")
        reqs = [self.call(i, "get_symbol", name="foo") for i in range(1, 6)]
        reqs.append(self.call(6, "retrieve", query="q"))
        replies = self.talk(reqs)
        for r in replies[:5]:
            self.assertNotIn("budget", str(self.payload(r)))
        self.assertNotIn("budget exceeded", str(self.payload(replies[5])),
                         "get_symbol calls must not have consumed retrieve budget")

    def test_get_symbol_requires_a_name(self):
        r = self.talk([self.call(1, "get_symbol", name="   ")])
        self.assertIn("non-empty", self.payload(r[0])[0]["error"])

    # ---- subprocess failure surfaces diagnosably ---------------------------

    def test_retriever_failure_reports_stderr_and_interpreter(self):
        # "exit status 1" alone is undiagnosable; the usual cause is missing deps
        # in an interpreter the user did not realise was being used.
        (self.root / ".claude" / "hooks" / "lib" / "retriever.py").write_text(
            "import sys; sys.stderr.write('ModuleNotFoundError: fastembed\\n'); sys.exit(1)")
        r = self.talk([self.call(1, "retrieve", query="q")])
        err = self.payload(r[0])[0]["error"]
        self.assertIn("fastembed", err, "stderr must be surfaced")
        self.assertIn("interpreter", err, "must name which python was used")

    def test_symbols_returning_nothing_is_not_an_error(self):
        (self.root / ".claude" / "hooks" / "lib" / "symbols.py").write_text("pass")
        r = self.talk([self.call(1, "get_symbol", name="nope")])
        self.assertEqual(self.payload(r[0]), [], "no matches is a valid result")


class ResolvePythonCase(unittest.TestCase):
    """Picking the interpreter that has the retrieval deps, not the launcher."""

    def test_prefers_project_venv_over_launcher(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            venv_py = root / ".venv" / "bin" / "python"
            venv_py.parent.mkdir(parents=True)
            venv_py.write_text("")
            saved = mcp.REPO_ROOT
            try:
                mcp.REPO_ROOT = root
                self.assertEqual(mcp.resolve_python(), str(venv_py))
            finally:
                mcp.REPO_ROOT = saved

    def test_falls_back_to_the_launching_interpreter(self):
        with tempfile.TemporaryDirectory() as td:
            saved, saved_env = mcp.REPO_ROOT, os.environ.pop("VIRTUAL_ENV", None)
            try:
                mcp.REPO_ROOT = Path(td)
                self.assertEqual(mcp.resolve_python(), sys.executable)
            finally:
                mcp.REPO_ROOT = saved
                if saved_env is not None:
                    os.environ["VIRTUAL_ENV"] = saved_env


if __name__ == "__main__":
    unittest.main()


class RepoRootCase(unittest.TestCase):
    """The server is launched by the harness from .mcp.json, so its cwd is not
    the project's to guarantee. A wrong root here is silent and total: RETRIEVER,
    SYMBOLS and STATE_DIR all miss, and the per-task retrieve budget never
    engages because current_task.txt can never be read."""

    def test_root_comes_from_the_file_not_the_cwd(self):
        import os, shutil, tempfile
        with tempfile.TemporaryDirectory() as proj, tempfile.TemporaryDirectory() as elsewhere:
            root = Path(proj)
            mcp_dir = root / ".claude" / "mcp"
            mcp_dir.mkdir(parents=True)
            shutil.copy(SERVER, mcp_dir / "agentic_mcp.py")
            cwd = os.getcwd()
            try:
                os.chdir(elsewhere)
                spec = importlib.util.spec_from_file_location(
                    "mcp_isolated", mcp_dir / "agentic_mcp.py")
                mod = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(mod)
            finally:
                os.chdir(cwd)
        self.assertEqual(mod.REPO_ROOT, root.resolve())
        self.assertEqual(mod.STATE_DIR, root.resolve() / ".claude" / "state")
