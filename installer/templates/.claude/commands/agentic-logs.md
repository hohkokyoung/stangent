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
   many tool calls, any denials/failures, an unusually long duration, or a
   lopsided `heaviest results` list (one unbounded grep dwarfing everything
   else, or the same search repeated).

## Notes

- Derived entirely from `.claude/state/logs/<id>.jsonl` + `dispatch.jsonl` (+ the
  plan dir for task status when the context is a build run). Read-only.
- Tool-call counts, denials, failures, retrieve/get_symbol usage, per-task
  durations, and the `res`/`heaviest results` sizes come from the tool-use log.
  Token/cost columns and the run-level cache-hit % come from the SubagentStop
  telemetry hook (`log_usage.py`) and appear once a run has dispatched at least
  one agent under the new hook.
- `res` is characters of tool RESULT pulled into the agent's context (÷4 ≈
  tokens), not billed tokens. It is the lever behind cache-read cost: a result
  is re-read on every later turn, so one 80k-char grep on turn 5 of 70 costs far
  more than its single call suggests. Use it to find which calls to bound; use
  the cost column for what was actually charged. Runs logged before this field
  existed show `-`.
- Only agentic work is logged (any command that dispatches an agent). Ambient
  dev is not, unless `AGENTIC_LOG_AMBIENT=1` is set.
