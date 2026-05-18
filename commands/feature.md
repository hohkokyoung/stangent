Run the full Stangent pipeline for a new feature request.

Usage: /feature <description>
Example: /feature add a login screen with email and password

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output the following and stop:
  "Stangent is not initialised in this project.
   Run init.py from your stangent installation, or see:
   https://github.com/hohkokyoung/stangent"

Extract and hold these values for the rest of this session:
  - feature_dir     = config.paths.feature_dir
  - archive_dir     = config.paths.archive_dir
  - log_dir         = config.paths.log_dir
  - srs_path        = config.paths.srs_path
  - registry_path   = config.paths.registry_path
  - max_retries     = config.pipeline.max_retries
  - config_path     = (absolute path to .stangent/config.json)

## Step 2 — Run orchestrator

Read the full contents of: .claude/agents/stangent.md

Execute the orchestrator instructions with:
  - raw_request   : "$ARGUMENTS"
  - feature_id    : "" (assigned during STEP 1 of orchestrator)
  - resume_from   : ""
  - config_path   : (absolute path to .stangent/config.json)

The orchestrator guides every stage:
PLANNING → AWAITING_CONFIRMATION → IMPLEMENTING → REVIEWING → SRS_UPDATE → COMPLETE

Do not skip stages. Do not proceed past AWAITING_CONFIRMATION without explicit
developer confirmation.

If at any point the pipeline sets status = PAUSED or ESCALATED:
stop and output the resume instructions clearly.
