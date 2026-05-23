Run only the planning stage for a feature. Writes a spec and stops before implementation.

Usage: /plan <description>
Example: /plan add push notifications for order updates

The feature will be left in AWAITING_CONFIRMATION state.
Start implementation later with: /implement FEAT-XXX

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir     = config.paths.feature_dir
  - registry_path   = config.paths.registry_path
  - log_dir         = config.paths.log_dir
  - config_path     = (absolute path to .stangent/config.json)

## Step 2 — Initialise feature record

Spawn the orchestrator using the Agent tool to run feature initialisation only:

  INPUTS:
  {
    "raw_request": "$ARGUMENTS",
    "feature_id":  "",
    "config_path": "(absolute path to .stangent/config.json)"
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/stangent.md
  Execute STEP 0 (pre-flight) and STEP 1 (feature initialisation) only.
  After STEP 1 completes (feature file created, branch created, active.json written),
  output the assigned feature_id and feature_file_path, then STOP.
  Do NOT proceed to STEP 2 or beyond.

Wait for the orchestrator to output the assigned feature_id and feature_file_path.
Store both for use in Step 3.

Note: STEP 2 (dependency check) is skipped — the planner writes ## Depends On
as part of the spec. Dependency check runs automatically when /implement is invoked.

## Step 3 — Spawn planner

Spawn the planner using the Agent tool:

  INPUTS:
  {
    "feature_id":        "(assigned in Step 2)",
    "feature_file_path": "(assigned in Step 2)",
    "config_path":       "(absolute path to .stangent/config.json)",
    "extra": { "raw_request": "$ARGUMENTS" }
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/stangent-planner.md
  Then execute those instructions using the inputs above.

## Step 4 — Stop after spec

After the planner writes the spec and presents it for developer confirmation:
  Set feature status = AWAITING_CONFIRMATION.
  Output:
    "Spec written — {feature_id}.
     Review: {feature_dir}/{feature_id}-{slug}.md
     When ready: /implement {feature_id}"

STOP. Do not proceed to implementation.
