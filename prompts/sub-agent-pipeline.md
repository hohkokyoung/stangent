# Sub-Agent Pipeline

Run sub-agents in fixed order. Pass `feature_id`, `feature_file_path`, and `config_path` to each.

## Retry limit

Read `config.pipeline.sub_agent_max_retries` (default: 3).
Each sub-agent tracks its own retry count independently.
If a sub-agent exhausts its retries: set status = PAUSED, write to `## Implementation Log`:
`Sub-agent {name} exceeded sub_agent_max_retries ({N}) — paused for developer review.`
Return PAUSED to the orchestrator. Do not proceed to the next sub-agent.

## 1. Linter

Derive `project_root = Path(config_path).parent.parent`

Spawn using the Agent tool:
```
INPUTS: { "feature_id": "...", "feature_file_path": "...", "config_path": "...", "extra": {} }
INSTRUCTIONS: Read {project_root}/.claude/agents/subagents/stangent-linter.md and execute.
```

Wait for result. Read `## Linter Report`.
- FAIL: fix all reported issues. Increment linter retry count.
  If retry < sub_agent_max_retries: re-run linter.
  If retry >= sub_agent_max_retries: PAUSED (see limit above).
- PASS: proceed to step 2.

## 2. Unit tester

Spawn using the Agent tool:
```
INPUTS: { "feature_id": "...", "feature_file_path": "...", "config_path": "...", "extra": {} }
INSTRUCTIONS: Read {project_root}/.claude/agents/subagents/stangent-unit-tester.md and execute.
```

Wait for result. Read `## Test Report`.
- FAIL: fix failing tests. Do not add new tests — fix existing ones first. Increment retry count.
  If retry < sub_agent_max_retries: re-run.
  If retry >= sub_agent_max_retries: PAUSED.
- SKIPPED: all ACs were platform-bound with valid n/a justifications. Proceed to step 3.
- PASS: proceed to step 3.

## 3. Query analyzer

Check: does this feature touch any DB layer (models, repositories, raw queries)?
- No DB layer touched: write `## Query Analysis Report` status as SKIPPED. Done.
- DB layer touched: spawn using the Agent tool:

```
INPUTS: { "feature_id": "...", "feature_file_path": "...", "config_path": "...", "extra": {} }
INSTRUCTIONS: Read {project_root}/.claude/agents/subagents/stangent-query-analyzer.md and execute.
```

Wait for result. Read `## Query Analysis Report`.
- FAIL: fix all DANGER findings. Increment retry count.
  If retry < sub_agent_max_retries: re-run.
  If retry >= sub_agent_max_retries: PAUSED.
- WARN: review each warning. Fix or document why it is acceptable. Proceed once all addressed.
- PASS: done.
