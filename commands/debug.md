Conversational bug investigation. Walks through the problem with you — understand, narrow, root cause, fix, wrap up.

Usage: /debug <description of the problem>
Example: /debug the second profile card is visible underneath during initial load

Unlike /plan, this does not create a feature spec or pipeline. It is a focused
investigation session. If the fix turns out to be complex, the debug agent will
offer to escalate to /plan.

---

## Step 1 — Load configuration (optional)

Read `.stangent/config.json` if it exists.
If it does not exist: proceed without it — debug works on any project.

Extract `config_path` (absolute path to .stangent/config.json) if found, else empty string.

## Step 2 — Spawn debug agent

Spawn using the Agent tool:

  INPUTS:
  {
    "description": "$ARGUMENTS",
    "config_path": "(absolute path to .stangent/config.json, or empty string)"
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/stangent-debug.md
  Then execute those instructions using the inputs above.

## Step 3 — Handle result

- FIXED:     output "Bug resolved. Session complete."
- ESCALATED: output "Investigation escalated. Use /plan to track a formal fix."
- ABANDONED: output "Debug session ended."
