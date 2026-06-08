---
description: Diagnose a bug by inspecting live data + code. Produces a diagnosis report without touching the codebase.
argument-hint: "<brief bug description>"
---

# /agentic-debug

Entry point for data-aware debugging. Collects context from the developer, then runs the debugger agent.

## Procedure

1. **Allocate a debug ID** using the current timestamp: `DBG-<YYYYMMDD>-<HHMMSS>`.
   Create `.claude/state/debug/` if it does not exist.

2. **Clarification phase (YOU do this — do NOT delegate to the debugger).**
   Ask the developer for the following — batch into one `AskUserQuestion` round:
   - What is the symptom? (what does the user see vs what they expect)
   - Which feature, endpoint, or flow is affected?
   - Is there a specific user ID, record ID, or table name involved? (if known)
   - Has this ever worked, or is it a regression?

   Rules:
   - Do not assume answers from the goal text alone — ask.
   - All four questions in one round. Do not split across multiple rounds.
   - If the developer says "unknown" for user/record, note it and proceed — the debugger will look broadly.

3. **Invoke the debugger agent** with:
   - The bug description: `$ARGUMENTS`
   - The clarification answers
   - The `debug_id`

4. After the debugger writes its report, print:
   - `debug_id`
   - Full path to the diagnosis report
   - The **Diagnosis** and **Suggested next step** sections from the report
   - "To act on this: /agentic-update-plan or /agentic-plan with the diagnosis as context"

## Constraints

- Do NOT call any MCP tool yourself.
- Do NOT read code or query data yourself — that is the debugger's job.
- Do NOT create plan files, task files, or branches.
- Do NOT fix anything. Diagnosis only.
