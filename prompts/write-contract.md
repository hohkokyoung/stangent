# Write Feature Contract

Write `.stangent/contracts/{{feature_id}}.json` immediately after the spec.
The gateway reads this to enforce paths, agent identity, and bash constraints at every tool call.

## Step A — Extract `allowed_paths` from `## Files to Touch`

- Each file or directory listed → add to `allowed_paths`
- Use glob patterns for directories: `src/auth/` → `src/auth/**`
- Exclude `[doc]`-tagged entries (they are for review only, not writes)

## Step B — Extract `blocked_paths` from `## Out of Bounds`

- Each explicit file or directory path → add to `blocked_paths`
- Skip behavioural constraints with no path (e.g. "Do not write implementation code")

## Step C — Validate paths

For each path in `allowed_paths` and `blocked_paths`:
- Check if the path or its parent directory exists in the repo
- New files (no parent dir) → warn in the feature file comment, do not fail
- Paths whose parent dir does not exist → add a comment in `## Implementation Log`:
  `Note: {path} parent dir does not exist yet — will be created`

## Step D — Build `allowed_agents` per state

Use the default state→agent mapping. Only include states relevant to this feature.

## Step E — Build `capabilities` per agent

Read the active language profile to determine the correct lint and test commands.
Map them to bash capability tokens.

## Write the contract

```json
{
  "feature_id": "{{feature_id}}",
  "allowed_paths": [
    "src/auth/jwt.py",
    "src/auth/**",
    "tests/auth/**"
  ],
  "blocked_paths": [
    "lib/screens/home_screen.dart",
    "lib/main.dart"
  ],
  "bash_blocklist": [],
  "allowed_agents": {
    "PLANNING":     ["planner"],
    "IMPLEMENTING": ["implementer", "linter", "unit_tester", "query_analyzer"],
    "REVIEWING":    ["reviewer", "security_scanner"],
    "REFINING":     ["planner"],
    "SRS_UPDATE":   ["srs_agent"]
  },
  "capabilities": {
    "implementer": ["bash:git diff", "bash:git add", "bash:git commit", "bash:git log",
                    "bash:git status", "bash:git branch"],
    "linter":      ["bash:ruff", "bash:flutter analyze", "bash:dart analyze"],
    "unit_tester": ["bash:pytest", "bash:flutter test", "bash:dart test"],
    "query_analyzer": ["bash:grep", "bash:find"]
  }
}
```

Populate `implementer` capabilities from profile.bash_allowlist if defined.
Populate `linter` / `unit_tester` from the active profile's lint/test commands.
Leave `bash_blocklist` empty — the gateway's built-in hard blocks cover destructive commands.
