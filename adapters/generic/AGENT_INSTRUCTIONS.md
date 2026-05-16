# Stangent Gateway — Generic Agent Instructions

Include this file in your system prompt / agent context when using Stangent
with any AI coding tool that does not support Claude Code's PreToolUse hooks.

This provides **soft enforcement** (instruction-based). The gateway script
itself remains the authoritative policy engine — these instructions tell the
agent to call it before acting.

---

## Before every file write or edit

Before writing or editing any file, you MUST call the gateway check:

```bash
echo '{"tool_name":"Write","tool_input":{"file_path":"<PATH>"}}' | python .stangent/gateway/gateway.py
```

- Exit code 0 → proceed with the write.
- Exit code non-zero → read the error message, do NOT write the file. Use ASK_DEVELOPER instead.

## Before every bash command

Before running any bash command, you MUST call the gateway check:

```bash
echo '{"tool_name":"Bash","tool_input":{"command":"<COMMAND>"}}' | python .stangent/gateway/gateway.py
```

- Exit code 0 → proceed.
- Exit code non-zero → the command is blocked. Do NOT run it.

## Active feature context

The gateway reads `.stangent/gateway/active.json` to know which feature
is currently active and which agent is running. This file is written by
the orchestrator and must not be modified by other agents.

## What the gateway enforces

1. Hard bash blocks — `git push --force`, `rm -rf`, `git reset --hard`, etc.
2. Agent/state check — you must be the correct agent for the current pipeline state.
3. Out of Bounds paths — files explicitly blocked in the feature spec.
4. Files to Touch whitelist — writes must be in the declared scope.
5. Bash capability check — bash commands must match your declared capabilities.

## Override

If the gateway blocks something you believe is legitimate:
- Do NOT bypass the check silently.
- Report the block to the developer: `[Gateway] blocked: <reason>`
- The developer uses `/gateway unblock <path>` to add the path to the contract.

---

*Note: For hard enforcement (blocks at the tool-call level, not instruction-level),
use the Claude Code adapter which wires gateway.py into PreToolUse hooks.*
