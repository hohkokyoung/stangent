---
description: Show a readable summary of a workflow's logs — per-task tool-call counts, retrieve/get_symbol usage, denials, failures, and duration. Reads the existing JSONL; no run required.
argument-hint: "[run-id | review-id] [--json]"
---

# /agentic-logs

Turn the write-only JSONL logs into a legible report. Works for any log context —
build runs (`FEAT-NNN`), reviews (`SEC-`/`DR-`/`UIR-`/`PR-`), debug (`DBG-`),
design (`DS-`), baseline tests (`baseline-<flow>`).

## Procedure

1. Resolve the interpreter:
   ```
   PYEXE=$(ls .venv/bin/python venv/bin/python .env/bin/python 2>/dev/null | head -1)
   ```
2. **If `$ARGUMENTS` names a context id** → summarize it:
   ```
   ${PYEXE:-python3} .claude/hooks/lib/logs.py summarize <id>
   ```
3. **If `$ARGUMENTS` is empty** → list contexts (newest first), then summarize the
   most recent:
   ```
   ${PYEXE:-python3} .claude/hooks/lib/logs.py list
   ${PYEXE:-python3} .claude/hooks/lib/logs.py summarize <newest-id>
   ```
4. Pass `--json` through to either subcommand when the developer asks for machine
   output.
5. Print the report verbatim. If asked, call out what stands out — a task with
   many tool calls, any denials/failures, or an unusually long duration.

## Notes

- Derived entirely from `.claude/state/logs/<id>.jsonl` + `dispatch.jsonl` (+ the
  plan dir for task status when the context is a build run). Read-only.
- Tool-call counts, denials, failures, retrieve/get_symbol usage, and per-task
  durations are all available today; token/cost columns arrive with the
  telemetry hook.
- Only agentic work is logged (any command that dispatches an agent). Ambient
  dev is not, unless `AGENTIC_LOG_AMBIENT=1` is set.
