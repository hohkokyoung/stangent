Run the project test suite for a feature (or the whole project) without re-implementing.

Usage:
  /test              — run tests for the current active feature
  /test FEAT-XXX     — run tests for a specific feature
  /test --all        — run the full test suite (no feature scope)

Spawns the unit_tester sub-agent directly. Returns the Test Report.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir = config.paths.feature_dir
  - config_path = (absolute path to .stangent/config.json)

## Step 2 — Resolve feature ID

If "$ARGUMENTS" contains "--all":
  feature_id = "" (signals full-suite mode to unit_tester)
  feature_file_path = ""
Else if "$ARGUMENTS" is non-empty:
  feature_id = "$ARGUMENTS" (normalised — uppercase, FEAT- prefix if missing)
  Find feature file: glob {feature_dir}/{feature_id}*.md
  If not found: output "Feature {feature_id} not found." and stop.
Else:
  Read `.stangent/gateway/active.json`. Use its feature_id.
  If none: output "No active feature. Pass a FEAT-ID or --all." and stop.

## Step 3 — Spawn unit tester

Spawn the unit_tester sub-agent using the Agent tool:

  INPUTS:
  {
    "feature_id":        "{feature_id}",
    "feature_file_path": "{feature_file_path}",
    "config_path":       "(absolute path to .stangent/config.json)"
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/subagents/stangent-unit-tester.md
  Then execute those instructions using the inputs above.

  For --all mode: skip the AC Coverage Check (no feature_id to match against).
  Run the full suite and report coverage + failures only.

## Step 4 — Output

The unit_tester writes `## Test Report` to the feature file (or stdout for --all).

Display the Test Report to the developer. Highlight:
  - Status (PASS / FAIL / SKIPPED)
  - Coverage delta
  - Any failing tests or regressions

If FAIL with a specific feature:
  Output: "Tests failed. Use /implement {feature_id} to fix, or /debug if you need help diagnosing."
If PASS: output "All tests pass."

STOP.
