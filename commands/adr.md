Record an architectural decision as a JSON entry in decisions.json.

> **Note:** ADRs are bootstrapped automatically on the first `/feature` call —
> the planner scans your codebase and asks which patterns become binding decisions.
> Use `/adr` to add explicit decisions any time, or when you want to record a
> decision that wasn't captured during bootstrap.

Usage:
  /adr <title>         — start a guided ADR session
  /adr list            — show all existing ADRs with status
  /adr show <ADR-ID>   — show one ADR in full

Examples:
  /adr state management library
  /adr use JWT for authentication
  /adr list

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - decisions_path   = config.paths.decisions_path
  - config_path      = (absolute path to .stangent/config.json)

## Step 2 — Determine mode

If "$ARGUMENTS" is exactly "list" → LIST MODE
If "$ARGUMENTS" starts with "show " → SHOW MODE
Otherwise → WRITE MODE (title = "$ARGUMENTS")

---

## LIST MODE

Read {decisions_path}.
Find all `## ADR-XXX:` headings. For each, extract title and status line.
Output:

```
ADR-001  [Accepted]    State Management — use Riverpod
ADR-002  [Accepted]    Auth — JWT tokens with 24h expiry
ADR-003  [Superseded by ADR-005]  API layer — REST
...

Total: N decisions  (N Accepted, N Superseded, N Deprecated)
```

If no ADRs exist: output "No ADRs recorded yet. Write one with: /adr <title>"

---

## SHOW MODE

Title = "$ARGUMENTS" with "show " stripped from the front.
Search {decisions_path} for the matching ADR-XXX heading.
Output the full ADR block.

---

## WRITE MODE

Read the full contents of: .claude/agents/stangent-adr.md

Execute the ADR agent with:

    INPUTS:
    {
      "title":          "$ARGUMENTS",
      "decisions_path": "{decisions_path}",
      "config_path":    "{absolute path to .stangent/config.json}"
    }

    INSTRUCTIONS:
    Read the full contents of: .claude/agents/stangent-adr.md
    Then execute those instructions using the inputs above.
