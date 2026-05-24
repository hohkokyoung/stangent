Run the full Stangent pipeline for a new feature request.

Usage: /feature <description>
       /feature --yes <description>     — auto-confirm spec (skips AWAITING_CONFIRMATION prompt)
       /feature --shadow <description>  — run planner only, print spec to stdout, don't create files / branch / contract

Example: /feature add a login screen with email and password
Example: /feature --shadow add push notifications     (preview what the planner would propose)

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

Parse flag prefixes in order; any may appear (but only one of --yes / --shadow).

auto_confirm = false
shadow_mode  = false
raw_request  = "$ARGUMENTS"

If raw_request starts with "--yes ":
  auto_confirm = true
  raw_request  = raw_request with "--yes " stripped
If raw_request starts with "--shadow ":
  shadow_mode  = true
  raw_request  = raw_request with "--shadow " stripped

If shadow_mode: jump to Step 2-shadow below instead of Step 2.

## Step 2-shadow — Planner-only dry run

(Only if shadow_mode is true.)

Run the tier classifier inline (read .stangent/prompts/classifier.md, apply to
raw_request). Output the classified tier.

Spawn the planner using the Agent tool. Provide a synthetic feature_id
("SHADOW") and a tmp path for feature_file_path (do not create any files
on disk — pass the path but tell the planner: "SHADOW MODE — return the spec
content as your output, do not write to disk").

Run only the planner. Do NOT create:
  - The feature file in feature_dir
  - The branch
  - The contract
  - The gateway active.json
  - Any registry entry

After the planner returns, print:
  ```
  --- SHADOW MODE — no files written ---
  Tier:  {tier}
  Spec:

  {spec content from planner}
  ---
  ```

STOP. To actually run the feature, drop --shadow and re-run.

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
specialists). When `pipeline.inline_direct_planning = true` (default), the
orchestrator runs Direct-tier planning inline instead of spawning the planner
subagent (STEP 3a.1) — saves ~30-40k tokens of cold-start. Standard tier always
runs the full pipeline with all subagent spawns.

Do not skip stages. Do not proceed past AWAITING_CONFIRMATION without explicit
developer confirmation.

If at any point the pipeline sets status = PAUSED or ESCALATED:
stop and output the resume instructions clearly.
