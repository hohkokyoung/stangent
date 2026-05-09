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
  - stangent_path   = config.stangent_path
  - feature_dir     = config.paths.feature_dir
  - log_dir         = config.paths.log_dir
  - max_retries     = config.pipeline.max_retries

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

  - REVIEW_PASS or COMPLETE
      Output: "Feature already complete." and stop.

  - ESCALATED
      Output: "Feature escalated — manual intervention required.
               Review: {feature_file_path}
               Resolve issues, set status = CONFIRMED, then re-run /implement $ARGUMENTS" and stop.

  - BLOCKED
      Read ## Depends On. Output dependency status for each blocking feature. Stop.

## Step 3 — Load retry context

Read retry_count from feature file frontmatter.
If retry_count > 0: read ## Review Verdict — pass as previous_verdict.
Else: previous_verdict = ""

## Step 4 — Set IMPLEMENTING state

Before spawning the implementer, update the feature file:
  - Set `status = IMPLEMENTING` in frontmatter
  - Set `implementer_agent_version` to the version from implementer.md frontmatter
  - Append to ## Pipeline History:
    `[timestamp] | IMPLEMENTING | orchestrator | /implement invoked`

## Step 5 — Run implementer

Read the full contents of: {{stangent_path}}/agents/implementer.md

Execute the implementer with:
  - feature_id         : $ARGUMENTS
  - feature_file_path  : (resolved path from Step 2)
  - previous_verdict   : (from Step 3)
  - config_path        : (absolute path to .stangent/config.json)

## Step 6 — Continue to review

On IMPLEMENTED: read {{stangent_path}}/agents/orchestrator.md, execute from STEP 6 (REVIEWING).
On PAUSED:      output resume instructions. Stop.
On FAILED:      output failure details. Stop.
