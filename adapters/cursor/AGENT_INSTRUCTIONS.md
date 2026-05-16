# Stangent Gateway — Cursor Agent Instructions

Include this content in your `.cursorrules` file (or `.cursor/rules/stangent.mdc` for
Cursor v0.40+) when using Stangent inside Cursor AI.

This provides **soft enforcement** (instruction-based, not hook-based). The gateway
script is still the authoritative policy engine — these instructions tell the Cursor
agent to call it before acting. For hard enforcement, use the Claude Code adapter
which wires gateway.py into PreToolUse hooks at the tool call level.

---

## Setup

**Option A — .cursorrules (all Cursor versions):**
Copy the rules below into `.cursorrules` at the project root.

**Option B — .cursor/rules/stangent.mdc (Cursor v0.40+):**
Create `.cursor/rules/stangent.mdc` and paste the rules below.
Add `alwaysApply: true` in the frontmatter to activate for all files.

---

## Rules to add

```
# Stangent Gateway

This project uses Stangent for AI-assisted feature development.
Gateway enforcement is active. Follow these rules for every file write and bash command.

## Before every file write or edit

Call the gateway check first:

  echo '{"tool_name":"Write","tool_input":{"file_path":"<PATH>"}}' | python .stangent/gateway/gateway.py

- Exit 0 → proceed.
- Exit non-zero → do NOT write. Report the block: [Gateway] blocked: <reason>
  The developer uses /gateway unblock <path> to approve an out-of-scope path.

## Before every terminal command

Call the gateway check first:

  echo '{"tool_name":"Bash","tool_input":{"command":"<COMMAND>"}}' | python .stangent/gateway/gateway.py

- Exit 0 → proceed.
- Exit non-zero → do NOT run the command.

## What the gateway enforces

1. Hard bash blocks — git push --force, git reset --hard, rm -rf, DROP TABLE, DELETE FROM
2. Agent/state check — you must be the correct agent for the current pipeline state.
3. Out of Bounds paths — files blocked in the active feature spec.
4. Files to Touch whitelist — writes must be within the declared feature scope.

## Active feature context

The gateway reads .stangent/gateway/active.json to identify the active feature
and which agent is running. This file is written by the orchestrator — do not modify it.
If active.json does not exist, the gateway is in permissive mode.

## On a gateway block

- Do NOT silently bypass the check.
- Output: [Gateway] blocked: <reason from gateway output>
- Ask the developer to run `/gateway unblock <path>` in Claude Code to approve.
  Or ask them to run: python .stangent/gateway/gateway.py --unblock <path>

## On a hard block (git push --force, rm -rf, etc.)

These cannot be unblocked. Do not run them under any circumstances.
Explain why the command is blocked and suggest a safe alternative.
```

---

## Checking gateway status from Cursor terminal

```bash
# Is the gateway active?
cat .stangent/gateway/active.json

# What contract is active for the current feature?
cat .stangent/contracts/<FEAT-ID>.json

# Manual gateway check
echo '{"tool_name":"Write","tool_input":{"file_path":"src/example.py"}}' | python .stangent/gateway/gateway.py
```

---

## Stangent slash commands

These run in Claude Code (not Cursor). Open Claude Code alongside Cursor:

| Command | What it does |
|---------|-------------|
| `/feature <desc>` | Full pipeline: plan → implement → review → SRS |
| `/plan <desc>` | Plan only, writes spec |
| `/implement FEAT-XXX` | Implement a planned feature |
| `/resume FEAT-XXX` | Resume a paused feature |
| `/review FEAT-XXX` | Review an implemented feature |
| `/status` | Show all features and their pipeline state |
| `/doctor` | Validate stangent config and wiring |
| `/gateway status` | Show active contract details |
| `/gateway unblock <path>` | Approve an out-of-scope write |

---

*For hard enforcement (tool-call-level blocking, not instruction-level), use the
Claude Code adapter which wires gateway.py into PreToolUse hooks.*
