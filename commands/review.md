Run only the review stage for a feature that has been implemented.

Usage: /review <FEAT-ID>
Example: /review FEAT-001

Use this to re-run a review after manually fixing issues, or to run review
independently from the main pipeline.

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
  - Not yet IMPLEMENTING or beyond: output "Feature must be implemented first." and stop.
  - REVIEW_PASS or COMPLETE: output "Already reviewed and passed." and stop.
  - Anything else: proceed.

## Step 3 — Run reviewer

Read the full contents of: .claude/agents/stangent-reviewer.md

Execute the reviewer with:
  - feature_id         : $ARGUMENTS
  - feature_file_path  : (resolved path)
  - config_path        : (absolute path to .stangent/config.json)

## Step 4 — Handle verdict

On PASS:
  Set status = REVIEW_PASS.
  Output:
    "✓ $ARGUMENTS passed review.
     Run /srs $ARGUMENTS to update the SRS, or /feature for a new feature."

On FAIL:
  Read retry_count from frontmatter. Increment it. Write back.
  Output the ## Review Verdict clearly.
  If retry_count >= max_retries:
    Set status = ESCALATED.
    Output: "Max retries reached. Feature escalated. Manual intervention required."
  Else:
    Set status = CONFIRMED.
    Output: "Fix the issues above, then run /implement $ARGUMENTS to retry.
             Retry {retry_count}/{max_retries}."
    Note: status is set to CONFIRMED so /implement can be run immediately without
    a "already in progress" block.
