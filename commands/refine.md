Revise a feature spec based on test feedback and reimplement from the updated plan.

Use this when you have tested the implementation and found it doesn't match what
you actually wanted. /refine updates the spec first, then reimplements cleanly —
avoiding the drift that comes from ad-hoc conversational corrections.

Usage: /refine <FEAT-ID> <description of what is wrong>
Example: /refine FEAT-003 the error toast only shows for network errors, not validation errors

The description must be provided inline. Be specific about what you observed
and what you expected — the planner uses it to identify which parts of the
spec need to change.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir   = config.paths.feature_dir
  - log_dir       = config.paths.log_dir
  - max_replans   = config.pipeline.max_replans (default: 2 if not set)
  - config_path   = (absolute path to .stangent/config.json)

---

## Step 2 — Parse arguments

$ARGUMENTS format: `<FEAT-ID> <feedback text>`

Split on the first whitespace-separated token:
  - feature_id = first token (e.g. "FEAT-003")
  - feedback   = everything after the first token

If feature_id is missing: output "Usage: /refine <FEAT-ID> <description>" and stop.
If feedback is empty:
  output:
    "Usage: /refine <FEAT-ID> <description of what is wrong>
     Example: /refine FEAT-003 the save button does nothing on Android"
  Stop.

---

## Step 3 — Load and validate feature

Find the feature file: glob {feature_dir}/{feature_id}*.md
If not found: output "Feature {feature_id} not found." and stop.

Read frontmatter: status, title, branch, retry_count, replan_count, spec_version.

Valid statuses for /refine: REVIEW_PASS, COMPLETE
Any other status:
  - CREATED / PLANNING / AWAITING_CONFIRMATION / CONFIRMED / IMPLEMENTING / REVIEWING:
      output "Feature {feature_id} is still in progress ({status}).
              Wait for it to finish, then /refine if needed." and stop.
  - PAUSED:
      output "Feature {feature_id} is paused. Resume with /resume {feature_id} first." and stop.
  - ESCALATED:
      output "Feature {feature_id} was escalated. Fix the escalation first,
              then /refine if the spec also needs changing." and stop.
  - ABANDONED / FAILED:
      output "Feature {feature_id} is {status} — cannot refine." and stop.

Check replan_count vs max_replans:
If replan_count >= max_replans:
  output:
    "⚠ {feature_id} has already been refined {replan_count} time(s) (max: {max_replans}).
     Further /refine runs are blocked to prevent infinite loops.
     Options:
       A — /abandon {feature_id} and start a new feature with a clearer spec
       B — Manually edit the spec and set status = CONFIRMED, then /implement {feature_id}"
  Stop.

---

## Step 4 — Build revision context

Read from the feature file:
  - `## Implementation Log` — what the implementer built and why
  - `## Files Changed`      — actual files touched

Build `revision_context`:
```
Developer test feedback: {feedback}

What was implemented:
  Files changed: {## Files Changed content, first 10 entries}
  Implementation summary: {## Implementation Log, first 500 chars}
```

---

## Step 5 — Set REFINING state

Update the feature file frontmatter:
  - Set `status = REFINING`
  - Set `updated` = current ISO date

Write `.stangent/gateway/active.json`:
```json
{ "feature_id": "{feature_id}", "state": "REFINING",
  "agent": "planner", "activated_at": "{ISO timestamp}" }
```

---

## Step 6 — Run planner in Revision Mode

Read the full contents of: .claude/agents/stangent-planner.md

Execute the planner with:
  - feature_id        : {feature_id}
  - feature_file_path : (resolved path)
  - config_path       : (absolute path to .stangent/config.json)
  - revision_context  : (built in Step 4)

---

## Step 7 — Handle planner result

### SPEC_REVISED

The planner has already presented the revision summary and asked for confirmation.
Wait for the developer's response:

**"yes" / "confirm" / "proceed":**
  - Increment `replan_count` by 1 in frontmatter
  - Reset `retry_count = 0` in frontmatter
  - Clear implementer-owned sections back to blank:
    - `## Implementation Log` → blank
    - `## Files Changed`      → blank
    - `## QA`                 → `lint: PENDING | test: PENDING | query: PENDING`
  - Clear reviewer sections back to PENDING:
    - `## Security Report`    → Status: PENDING, Findings: blank
    - `## Review`             → all fields back to PENDING
  - Set `status = CONFIRMED`
  - Update active.json: `{ ..., "state": "CONFIRMED", "agent": "orchestrator" }`
  - Output: "Spec confirmed (v{spec_version}). Starting implementation..."
  - Spawn the orchestrator using the Agent tool to run the full remaining
    pipeline (implement → review → retry loop → complete) in a fresh context:

    INPUTS:
    {
      "feature_id":  "{feature_id}",
      "config_path": "(absolute path to .stangent/config.json)"
    }
    INSTRUCTIONS:
    Read the full contents of: .claude/agents/stangent.md
    The spec has been revised and confirmed (spec_version {spec_version}).
    Begin from STEP 5 (IMPLEMENTING stage). Skip steps 1–4.
    feature_id is {feature_id}. config_path is (absolute path).

  - Wait for orchestrator result and output the final status.

**corrections (developer provides changes to the revised spec):**
  - Re-run planner with the same `revision_context` plus a new `corrections` field
    set to the developer's message. Return to Step 6.

**"abort":**
  - Restore status to COMPLETE (refinement is only available on COMPLETE features).
  - Update active.json to reflect restored state.
  - Output: "Refinement cancelled. Feature {feature_id} remains COMPLETE."
  - Stop.

### SPEC_UNCHANGED

The planner found no spec changes needed — the spec was correct, the issue is
in the implementation.

  - Restore status to COMPLETE. Update active.json to reflect restored state.
  - Output:
    "The planner found no spec changes needed for your feedback.
     The spec already covers the expected behaviour — the issue is in the
     implementation, not the plan.

     Options:
       A — Run /implement {feature_id} to retry the implementation from the
           current spec (retry_count resets)
       B — Be more specific: /refine {feature_id} <more detailed description>
       C — Edit the spec manually and run /implement {feature_id}"
  - Stop.

### PAUSED

  - Set status = PAUSED in frontmatter.
  - Update active.json: `{ ..., "state": "PAUSED", "agent": "orchestrator" }`
  - Output: "Refinement paused. Resume with: /resume {feature_id}"
  - Stop.

### FAILED

  - Set status = FAILED in frontmatter.
  - Output the planner's failure reason.
  - Stop.
