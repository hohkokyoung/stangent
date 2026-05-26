Run the implementation stage for a feature that has been planned and confirmed.

Usage: /implement <FEAT-ID>
Example: /implement FEAT-001

The feature must have status CONFIRMED, AWAITING_CONFIRMATION, or PAUSED.
PAUSED features are resumed from where they left off.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir     = config.paths.feature_dir
  - log_dir         = config.paths.log_dir
  - max_retries     = config.pipeline.max_retries
  - config_path     = (absolute path to .stangent/config.json)

## Step 2 — Load and validate feature

Find the feature file: glob {feature_dir}/$ARGUMENTS*.md
If not found: output "Feature $ARGUMENTS not found." and stop.

Read the feature file. Check status:

  - CREATED
      Output: "Run /plan $ARGUMENTS first." and stop.

  - PLANNING
      Output: "Planner is still running or was interrupted for {{feature_id}}."
      Output: "If planning is done, check the spec at {{feature_file_path}} and set status to CONFIRMED, then re-run /implement $ARGUMENTS."
      Stop.

  - AWAITING_CONFIRMATION
      Present spec summary (Scope + Acceptance Criteria + Out of Bounds).
      Proceed immediately — the spec was already reviewed during /plan.
      Set status = CONFIRMED.

  - CONFIRMED or PAUSED
      Proceed immediately.

  - IMPLEMENTING
      Output: "Already in progress. Check for a PAUSED state in the feature file." and stop.

  - REVIEW_PASS
      Output: "Feature has already passed review.
               If you tested it and something is wrong, use: /refine {feature_id} <description>
               If you want to reimplement without changing the spec, set status = CONFIRMED and re-run /implement." and stop.

  - COMPLETE
      Output: "Feature is already complete.
               If you tested it and something is wrong, use: /refine {feature_id} <description>" and stop.

  - ESCALATED
      Output: "Feature escalated — manual intervention required.
               Review: {feature_file_path}
               Resolve issues, set status = CONFIRMED, then re-run /implement $ARGUMENTS" and stop.

  - BLOCKED
      Read ## Depends On. Output dependency status for each blocking feature. Stop.

## Step 3 — Load retry context

Read retry_count from feature file frontmatter.
If retry_count > 0: read `## Review` findings — pass as previous_verdict.
Else: previous_verdict = ""

## Step 4 — Set IMPLEMENTING state

Before spawning the implementer, update the feature file:
  - Set `status = IMPLEMENTING` in frontmatter
  - Set `implementer_agent_version` to the version from implementer frontmatter

## Step 5 — Spawn implementer

Spawn the implementer using the Agent tool:

  INPUTS:
  {
    "feature_id":        "$ARGUMENTS",
    "feature_file_path": "(resolved path from Step 2)",
    "config_path":       "(absolute path to .stangent/config.json)",
    "extra": {
      "previous_verdict": "(## Review findings content if retry_count > 0, else '')",
      "failure_type":     ""
    }
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/stangent-implementer.md
  Then execute those instructions using the inputs above.

## Step 6 — Continue pipeline

On PAUSED: output resume instructions. Stop.
On FAILED: output failure details. Stop.

On IMPLEMENTED:
  Spawn the orchestrator using the Agent tool to handle review, retry loop,
  SRS update, and completion — all in a fresh context:

  INPUTS:
  {
    "feature_id":  "$ARGUMENTS",
    "config_path": "(absolute path to .stangent/config.json)"
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/stangent.md
  The feature has just been implemented successfully.
  Begin from STEP 6 (REVIEWING stage). Skip steps 1–5.
  feature_id is $ARGUMENTS. config_path is (absolute path).

  Wait for the orchestrator result and output the final status.
