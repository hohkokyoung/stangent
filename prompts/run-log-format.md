## Run Log Format

Every agent appends JSON Lines to `{paths.log_dir}/{feature_id}.jsonl`.
One line per significant action. Do not write intermediate progress lines.

### Schema

```json
{
  "ts":            "ISO-8601 timestamp (UTC)",
  "feature_id":    "FEAT-XXX",
  "agent":         "orchestrator | planner | implementer | reviewer | srs_agent | linter | unit_tester | query_analyzer | security_scanner",
  "agent_version": "semver from agent frontmatter e.g. 1.1.0",
  "action":        "see actions table below",
  "detail":        "free-form description of what happened",
  "result":        "PASS | FAIL | PAUSED | SKIPPED | COMPLETE — include only for terminal actions",
  "tokens_in":     0,
  "tokens_out":    0
}
```

### Action values

| action | written by | when |
|--------|-----------|------|
| `stage_start` | any agent | first line, on entry |
| `stage_complete` | any agent | last line, on exit |
| `file_read` | any agent | when reading a significant source file |
| `bash_run` | any agent | before running each bash command |
| `ask_developer` | planner, implementer, reviewer | when pausing for developer input |
| `agent_spawn` | orchestrator, implementer | before spawning a sub-agent |
| `state_transition` | orchestrator | on every `status` change in frontmatter |
| `registry_update` | orchestrator | when FEAT-ID is claimed from registry |

### Required fields per action

`ts`, `feature_id`, `agent`, `action` — always required.
`agent_version`, `detail` — include when known.
`result` — only on `stage_complete` and `bash_run`.
`tokens_in`, `tokens_out` — include when measurable; 0 otherwise.
