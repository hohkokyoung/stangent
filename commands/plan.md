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
  - stangent_path   = config.stangent_path
  - feature_dir     = config.paths.feature_dir
  - registry_path   = config.paths.registry_path
  - log_dir         = config.paths.log_dir

## Step 2 — Initialise feature record

Read {stangent_path}/agents/orchestrator.md.
Execute orchestrator STEP 1 (feature initialisation) only.

Note: STEP 2 (dependency check) is skipped here. The planner writes ## Depends On
as part of the spec — it does not exist yet when /plan starts. Dependency check
runs automatically when /implement is invoked.

## Step 3 — Run planner

Read the full contents of: {stangent_path}/agents/planner.md

Execute the planner with:
  - raw_request   : "$ARGUMENTS"
  - feature_id    : (assigned in Step 2)
  - corrections   : ""
  - config        : (full .stangent/config.json contents)

## Step 4 — Stop after spec

After the planner writes the spec and presents it for developer confirmation:
  Set feature status = AWAITING_CONFIRMATION.
  Output:
    "Spec written — {feature_id}.
     Review: {feature_dir}/{feature_id}-{slug}.md
     When ready: /implement {feature_id}"

STOP. Do not proceed to implementation.
