---
name: gateway-audit
description: >
  Summarise the stangent gateway audit log. Trigger when the user says the
  gateway is blocking something, asks why a tool call was rejected, or wants
  to understand what the gateway has been enforcing. Auto-trigger on phrases
  like "gateway blocking", "why was this blocked", "what is gateway doing".
---

Read and summarise `.stangent/logs/gateway_audit.jsonl`.

## Step 1 — Load config

Read `.stangent/config.json` → get `paths.log_dir`.
Audit log path: `{log_dir}/gateway_audit.jsonl`.
If the file does not exist: output "No gateway audit log found. Has any feature run yet?" and stop.

## Step 2 — Read audit log

Read the full audit log. Each line is a JSON object with fields:
- `timestamp` — when the event occurred
- `feature_id` — which feature was active
- `agent` — which agent made the call
- `action` — ALLOW or BLOCK
- `tool` — tool name
- `reason` — why it was blocked (BLOCK only)
- `path` — file path involved (if applicable)

Focus only on BLOCK entries. Ignore ALLOW.

## Step 3 — Summarise

Group blocks by reason category:
- `hard_bash_block` — absolute bash commands that are always forbidden
- `blocked_path` — file path outside allowed scope
- `contract_bash_block` — bash command not in agent's allowlist
- `capability_model` — tool not permitted for current agent/state
- `agent_state_check` — wrong agent or pipeline state

Count blocks per agent and per reason category.
Identify the most blocked tool and most blocked agent.

If total blocks = 0: output "No blocks recorded. Gateway has allowed everything so far." and stop.

## Step 4 — Output

```
Gateway Audit Summary
─────────────────────
Total blocks: {n}
Most blocked agent: {agent} ({n} blocks)
Most blocked tool:  {tool} ({n} blocks)

By reason:
  {reason_category}: {count}
  ...

Recent blocks (last 5):
  {timestamp} | {agent} | {tool} | {reason} | {path if present}
  ...

{if hard_bash_block or blocked_path blocks exist}
→ These are hard blocks — do not loosen them.

{if contract_bash_block blocks exist}
→ Agent's bash_allowlist may need expanding. Check agents/{agent}.md.

{if capability_model blocks exist}
→ Agent capability config may be too restrictive. Check gateway/gateway.py.
```

Keep output under 20 lines. No raw JSON.
