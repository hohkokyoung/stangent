---
name: pipeline-debug
description: >
  Diagnose the current stangent pipeline state. Trigger when the user reports
  a pipeline failure, asks what is happening in the pipeline, or pastes agent
  output they do not understand. Auto-trigger on phrases like "pipeline stuck",
  "why did it fail", "what is the pipeline doing", or any stangent failure output.
---

Read the current stangent pipeline state and explain it in plain English.

## Step 1 — Find active feature

Read `.stangent/gateway/active.json`.
If not found: output "No active pipeline run. Nothing to debug." and stop.

Extract: `feature_id`, `state`, `agent`.

## Step 2 — Load feature state

Read `.stangent/config.json` → get `paths.feature_dir`.
Glob `{feature_dir}/{feature_id}-*.md` to find the feature file.

Read from the feature file:
- Frontmatter: `status`, `retry_count`, `title`
- `## Pipeline History` — last 5 entries only
- `## Review Verdict` — only if status is REVIEWING, FAILED, or ESCALATED

## Step 3 — Map state to plain English

| Status              | What is happening                                                      |
|---------------------|------------------------------------------------------------------------|
| PLANNING            | Planner is analysing the codebase and writing the feature spec.        |
| AWAITING_CONFIRMATION | Spec is ready. Waiting for your approval.                            |
| CONFIRMED           | Spec approved. Preparing to implement.                                 |
| IMPLEMENTING        | Implementer is writing code.                                           |
| REVIEWING           | Reviewer is checking the implementation.                               |
| SRS_UPDATE          | SRS agent is updating the requirements doc.                            |
| PAUSED              | Pipeline is paused. Use /resume {feature_id} to continue.             |
| FAILED              | Agent hit an unexpected error (not a review failure).                  |
| ESCALATED           | Max retries reached. Manual intervention required.                     |
| BLOCKED             | A dependency feature is not COMPLETE yet.                              |

For FAILED or ESCALATED: read the last Pipeline History entry for the specific reason.

Determine intervention:
- No action needed: PLANNING, CONFIRMED, IMPLEMENTING, REVIEWING, SRS_UPDATE
- Action required: PAUSED, FAILED, ESCALATED, BLOCKED, AWAITING_CONFIRMATION

## Step 4 — Output

```
Feature:  {feature_id} — {title}
Status:   {status}
Agent:    {agent}
Retries:  {retry_count}

What's happening:
{plain English explanation — 1-2 sentences max}

{if failure_type known from Pipeline History}
Failure type: {LINT | TEST | QUERY | SECURITY | REVIEW_CRITICAL | REVIEW_MAJOR}
Reason: {specific reason from Review Verdict or Pipeline History — one line}

{if no intervention needed}
→ No action needed. Pipeline will continue automatically.

{if intervention needed}
→ Action required: {specific recovery command}
```

Keep the output under 12 lines. No raw JSON. No markdown tables.
