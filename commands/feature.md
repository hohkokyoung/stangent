Run the full Stangent pipeline for a new feature request.

Usage: /feature <description>
       /feature --yes <description>   — auto-confirm spec (skips AWAITING_CONFIRMATION prompt)

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

## Step 1.5 — Parse flags

If "$ARGUMENTS" starts with "--yes ":
  auto_confirm = true
  raw_request  = "$ARGUMENTS" with "--yes " stripped from the front
Else:
  auto_confirm = false
  raw_request  = "$ARGUMENTS"

## Step 2 — Run orchestrator

Read the full contents of: .claude/agents/stangent.md

Execute the orchestrator instructions with:
  - raw_request   : "{raw_request}"
  - feature_id    : "" (assigned during STEP 1 of orchestrator)
  - resume_from   : ""
  - auto_confirm  : {auto_confirm} (orchestrator skips STEP 4 prompt when true)
  - config_path   : (absolute path to .stangent/config.json)

The orchestrator guides every stage:
TIER CLASSIFICATION → PLANNING → AWAITING_CONFIRMATION → IMPLEMENTING → REVIEWING → SRS_UPDATE → COMPLETE

Tier classification (STEP 1g of the orchestrator) sets `tier = direct | standard`
in the feature frontmatter. Direct tier runs a lighter planner (no full codebase
scan, no risk analysis) and a lighter reviewer (skips performance + quality
specialists). Standard tier runs the full pipeline.

Do not skip stages. Do not proceed past AWAITING_CONFIRMATION without explicit
developer confirmation.

If at any point the pipeline sets status = PAUSED or ESCALATED:
stop and output the resume instructions clearly.
