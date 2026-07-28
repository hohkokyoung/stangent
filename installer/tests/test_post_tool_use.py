#!/usr/bin/env python3
"""Tests for the post_tool_use logger: ambient gating + size rotation.

Runs the hook as it really does — a subprocess with a controlled repo root.

    python3 -m unittest discover installer/tests
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
HOOK = REPO / "templates" / ".claude" / "hooks" / "post_tool_use.py"
PAYLOAD = {"tool_name": "Bash", "tool_input": {"command": "echo hi"},
           "tool_response": {}}


class LoggerCase(unittest.TestCase):
    def run_hook(self, run_id=None, env=None, pre=None, payload=None, state=None):
        """Run the hook and snapshot logs/ BEFORE the tempdir is cleaned up.
        Returns {filename: line_count}; parsed lines land in self.logged.
        `pre`: {logname: bytes} to seed first. `state`: {filename: text} extra
        state files (current_role.txt, current_model.txt, …)."""
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            hooks = root / ".claude" / "hooks"
            logs = root / ".claude" / "state" / "logs"
            hooks.mkdir(parents=True)
            (root / ".claude" / "state").mkdir(exist_ok=True)
            shutil.copy(HOOK, hooks / HOOK.name)
            if run_id is not None:
                (root / ".claude" / "state" / "current_run.txt").write_text(run_id)
            for name, text in (state or {}).items():
                (root / ".claude" / "state" / name).write_text(text)
            if pre:
                logs.mkdir(parents=True, exist_ok=True)
                for name, data in pre.items():
                    (logs / name).write_bytes(data)
            runenv = {**os.environ, **(env or {})}
            proc = subprocess.run([sys.executable, str(hooks / HOOK.name)],
                                  input=json.dumps(payload or PAYLOAD), cwd=str(root),
                                  capture_output=True, text=True, env=runenv)
            self.stdout = proc.stdout
            self.returncode = proc.returncode
            snapshot = {}
            self.logged = {}
            if logs.exists():
                for f in logs.iterdir():
                    if f.is_file():
                        lines = f.read_text(errors="replace").splitlines()
                        snapshot[f.name] = len(lines)
                        self.logged[f.name] = [
                            json.loads(ln) for ln in lines if ln.startswith("{")]
            return snapshot

    def test_ambient_not_logged_by_default(self):
        logs = self.run_hook(run_id=None)
        self.assertNotIn("_no-run.jsonl", logs,
                         "ambient tool call must not be logged without a run")

    def test_ambient_logged_when_opted_in(self):
        logs = self.run_hook(run_id=None, env={"AGENTIC_LOG_AMBIENT": "1"})
        self.assertEqual(logs.get("_no-run.jsonl"), 1)

    def test_run_activity_logged(self):
        logs = self.run_hook(run_id="FEAT-001")
        self.assertEqual(logs.get("FEAT-001.jsonl"), 1)

    def test_non_build_context_logged_under_its_id(self):
        # Tier 0: reviews/debug/design set current_run to their own workflow id
        # (SEC-/DR-/UIR-/PR-/DBG-/DS-). Those must log under that id, not vanish.
        for ctx in ("SEC-20260724-120000", "DBG-20260724-120000", "DS-20260724-120000"):
            logs = self.run_hook(run_id=ctx)
            self.assertEqual(logs.get(f"{ctx}.jsonl"), 1, f"{ctx} not logged")

    def test_model_recorded_from_state(self):
        # The reviews used to write current_run/current_role but not
        # current_model, so every logged call carried "model": null and a run's
        # cost could not be traced to the model that produced it.
        self.run_hook(run_id="UIR-1", state={"current_model.txt": "claude-haiku-4-5-20251001"})
        line = self.logged["UIR-1.jsonl"][0]
        self.assertEqual(line["model"], "claude-haiku-4-5-20251001")

    def test_result_size_recorded(self):
        p = {"tool_name": "Bash", "tool_input": {"command": "grep -r x ."},
             "tool_response": {"stdout": "y" * 5000, "stderr": "z" * 100}}
        self.run_hook(run_id="FEAT-001", payload=p)
        self.assertEqual(self.logged["FEAT-001.jsonl"][0]["res_chars"], 5100)

    def test_result_size_measures_result_not_args(self):
        # The point of res_chars: a tiny command can return a huge result. If it
        # tracked args we would be blind to exactly the calls that cost the most.
        p = {"tool_name": "Bash", "tool_input": {"command": "ls"},
             "tool_response": {"content": "x" * 90000}}
        self.run_hook(run_id="FEAT-001", payload=p)
        line = self.logged["FEAT-001.jsonl"][0]
        self.assertEqual(line["res_chars"], 90000)
        self.assertLess(len(json.dumps(line["args"])), 200)

    def test_result_size_handles_list_and_unknown_shapes(self):
        for resp, expected in (
            ({"content": [{"type": "text", "text": "ab"}]}, 2),
            ("plain string", 12),
            ({}, 0),
            (None, 0),
        ):
            p = {"tool_name": "Read", "tool_input": {"file_path": "/a"},
                 "tool_response": resp}
            self.run_hook(run_id="FEAT-001", payload=p)
            self.assertEqual(self.logged["FEAT-001.jsonl"][0]["res_chars"], expected,
                             f"bad size for {resp!r}")

    def test_result_size_never_raises_on_junk(self):
        p = {"tool_name": "Weird", "tool_input": {"a": 1},
             "tool_response": {"unknown_key": {"nested": [1, 2, 3]}}}
        logs = self.run_hook(run_id="FEAT-001", payload=p)
        self.assertEqual(logs.get("FEAT-001.jsonl"), 1)
        self.assertGreater(self.logged["FEAT-001.jsonl"][0]["res_chars"], 0)

    def test_budget_warning_fires_once_per_threshold(self):
        # A task that keeps editing one file compounds cost silently — FEAT-025
        # t4 hit $16.40 across 361 turns with no signal until the run ended.
        import json as _j
        line = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit"})
        seed = ("\n".join([line] * 149) + "\n").encode()
        self.run_hook(run_id="FEAT-001",
                             state={"current_task.txt": "t4"},
                             pre={"FEAT-001.jsonl": seed})
        rows = self.logged["FEAT-001.jsonl"]
        budget = [r for r in rows if r.get("event") == "budget"]
        self.assertEqual(len(budget), 1, "150th call should emit one budget event")
        self.assertEqual(budget[0]["threshold"], 150)
        self.assertEqual(budget[0]["task_id"], "t4")

    def test_budget_warning_not_repeated_below_next_threshold(self):
        import json as _j
        call = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit"})
        warned = _j.dumps({"run_id": "FEAT-001", "task_id": "t4",
                           "event": "budget", "threshold": 150})
        seed = ("\n".join([call] * 200 + [warned]) + "\n").encode()
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": seed})
        rows = self.logged["FEAT-001.jsonl"]
        self.assertEqual(len([r for r in rows if r.get("event") == "budget"]), 1)

    def test_budget_warning_fires_on_result_bytes(self):
        # The axis that tracks the bill. 60 calls is well under the 150-call
        # threshold, but 60 x 20k chars is the FEAT-025 t4/t5 shape: each Edit
        # echoing a whole 25 KB file back into a context every later turn re-reads.
        import json as _j
        line = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit",
                         "res_chars": 20000})
        seed = ("\n".join([line] * 60) + "\n").encode()
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": seed})
        budget = [r for r in self.logged["FEAT-001.jsonl"]
                  if r.get("event") == "budget"]
        self.assertEqual(len(budget), 1, "crossing 800k result chars should warn")
        self.assertEqual(budget[0]["axis"], "result_chars")
        self.assertEqual(budget[0]["threshold"], 800_000)

    def test_budget_axes_are_deduped_independently(self):
        # A prior calls warning must not suppress the first result_chars warning:
        # they are different failures and the bytes one is the expensive default.
        import json as _j
        call = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit",
                         "res_chars": 20000})
        warned = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "event": "budget",
                           "axis": "calls", "threshold": 150})
        seed = ("\n".join([call] * 160 + [warned]) + "\n").encode()
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": seed})
        # Only events the hook just wrote carry a "ts"; the seeded one does not.
        emitted = [r for r in self.logged["FEAT-001.jsonl"]
                   if r.get("event") == "budget" and "ts" in r]
        self.assertEqual([e["axis"] for e in emitted], ["result_chars"],
                         "bytes axis must fire; calls axis must stay deduped")

    def test_bytes_axis_clearing_several_thresholds_warns_once(self):
        # res_chars can jump past multiple thresholds in one call (a single 4 MB
        # result), unlike calls which tick up by one. Dedup is on the highest
        # threshold warned, so the jump produces one event, not one per later call.
        import json as _j
        big = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Bash",
                        "res_chars": 4_000_000})
        first = self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                              pre={"FEAT-001.jsonl": (big + "\n").encode()})
        del first
        emitted = [r for r in self.logged["FEAT-001.jsonl"]
                   if r.get("event") == "budget" and "ts" in r]
        self.assertEqual(len(emitted), 1)
        self.assertEqual(emitted[0]["threshold"], 3_000_000,
                         "should name the highest threshold crossed")

        # …and a later call on the same task must not re-warn.
        seeded = "\n".join(_j.dumps(r) for r in self.logged["FEAT-001.jsonl"])
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": (seeded + "\n").encode()})
        again = [r for r in self.logged["FEAT-001.jsonl"]
                 if r.get("event") == "budget" and r.get("axis") == "result_chars"]
        self.assertEqual(len(again), 1, "already-warned threshold must not re-fire")

    def test_cheap_calls_do_not_trip_the_bytes_axis(self):
        # The separation that motivated the axis: many small results is fine.
        import json as _j
        line = _j.dumps({"run_id": "FEAT-001", "task_id": "t8", "tool": "Edit",
                         "res_chars": 2000})
        seed = ("\n".join([line] * 100) + "\n").encode()
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t8"},
                      pre={"FEAT-001.jsonl": seed})
        budget = [r for r in self.logged["FEAT-001.jsonl"]
                  if r.get("event") == "budget"]
        self.assertFalse(budget, "200k chars over 100 calls is healthy; must not warn")

    def test_no_budget_warning_without_a_task(self):
        self.run_hook(run_id="UIR-1")
        rows = self.logged["UIR-1.jsonl"]
        self.assertFalse([r for r in rows if r.get("event") == "budget"])

    def test_ordinary_call_emits_nothing_on_stdout(self):
        # The load-bearing one. This hook fires on EVERY tool call, and stdout is
        # parsed by the harness — anything unconditional would staple a system
        # reminder to every single tool result in every session.
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t1"})
        self.assertEqual(self.stdout, "")
        self.assertEqual(self.returncode, 0)

    def test_budget_warning_reaches_the_running_agent(self):
        # Written to the log the warning is forensics — nothing reads it until
        # /agentic-logs, after the run. Injected as additionalContext the agent
        # sees it next turn, with calls left to change course.
        import json as _j
        line = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit",
                         "res_chars": 20000})
        seed = ("\n".join([line] * 60) + "\n").encode()
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": seed})
        payload = _j.loads(self.stdout)
        hso = payload["hookSpecificOutput"]
        self.assertEqual(hso["hookEventName"], "PostToolUse")
        ctx = hso["additionalContext"]
        self.assertIn("script it", ctx, "must name the action, not just the number")
        self.assertLessEqual(len(ctx), 10000, "documented additionalContext cap")

    def test_budget_warning_never_blocks_the_tool_call(self):
        # Exit 2 would block. A long task can be legitimate; halting real work on
        # a heuristic costs more than the spend it prevents.
        import json as _j
        line = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit",
                         "res_chars": 20000})
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": ("\n".join([line] * 60) + "\n").encode()})
        self.assertEqual(self.returncode, 0)

    def test_context_is_not_re_emitted_once_warned(self):
        # Same dedup as the log event: crossing a threshold already warned about
        # must not staple the reminder to every subsequent call.
        import json as _j
        call = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "tool": "Edit",
                         "res_chars": 20000})
        warned = _j.dumps({"run_id": "FEAT-001", "task_id": "t4", "event": "budget",
                           "axis": "result_chars", "threshold": 800000})
        seed = ("\n".join([call] * 60 + [warned]) + "\n").encode()
        self.run_hook(run_id="FEAT-001", state={"current_task.txt": "t4"},
                      pre={"FEAT-001.jsonl": seed})
        self.assertEqual(self.stdout, "")

    def test_large_log_rotates(self):
        big = b"x" * (5 * 1024 * 1024 + 8)  # just over MAX_LOG_BYTES
        logs = self.run_hook(run_id="FEAT-001", pre={"FEAT-001.jsonl": big})
        self.assertIn("FEAT-001.1.jsonl", logs, "oversized log should roll to .1.jsonl")
        self.assertEqual(logs.get("FEAT-001.jsonl"), 1)  # fresh log holds only the new line


if __name__ == "__main__":
    unittest.main()
