Update the System Requirements Specification.

Usage:
  /srs              — process all completed features since last SRS update
  /srs <FEAT-ID>    — update SRS for one specific completed feature

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - stangent_path   = config.stangent_path
  - feature_dir     = config.paths.feature_dir
  - srs_path        = config.paths.srs_path
  - log_dir         = config.paths.log_dir

## Step 2 — Determine mode

If "$ARGUMENTS" is empty  → standalone mode (process all pending features)
If "$ARGUMENTS" is a FEAT-ID → single-feature mode

In single-feature mode: find {feature_dir}/$ARGUMENTS*.md
If not found: output "Feature $ARGUMENTS not found." and stop.
If status ≠ COMPLETE: output "Feature must be COMPLETE before SRS update." and stop.

## Step 3 — Run SRS agent

Read the full contents of: {stangent_path}/agents/srs_agent.md

Execute the SRS agent with:
  - feature_id         : "$ARGUMENTS" (empty in standalone mode)
  - feature_file_path  : (resolved path, empty in standalone mode)
  - config_path        : (absolute path to .stangent/config.json)

## Step 4 — Output result

On UPDATED:
  Output:
    "✓ SRS updated — version {new_version}
     Sections updated: {list}
     Committed: docs(SRS): {commit_message}"

On SKIPPED:
  Output: "SRS is already up to date. No completed features since last update."

On FAILED:
  Output the error with: "Re-run /srs to retry."
