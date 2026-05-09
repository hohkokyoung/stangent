Cleanly abandon a feature. Archives its file and removes the branch if no commits exist.

Usage: /abandon <FEAT-ID>
Example: /abandon FEAT-003

This action is irreversible. If the branch has no commits it is deleted.
If the branch has commits it is preserved for manual cleanup.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir   = config.paths.feature_dir
  - archive_dir   = config.paths.archive_dir
  - base_branch   = config.pipeline.pr_target_branch (default: "main" if not set)
    This is the branch that feature branches are created from.
    Used to detect whether any commits exist on the feature branch.

## Step 2 — Validate

If "$ARGUMENTS" is empty: output "Usage: /abandon <FEAT-ID>" and stop.

Find the feature file: glob {feature_dir}/$ARGUMENTS*.md
If not found: output "Feature $ARGUMENTS not found." and stop.

Check status:
  - COMPLETE  → output "Feature is already complete. Cannot abandon." and stop.
  - ABANDONED → output "Already abandoned." and stop.
  - Any other → proceed.

## Step 3 — Confirm

Present to developer:
  "Abandon {feature_id} — {title}?
   Status: {status}
   Branch: {branch}
   Files changed so far: {count from ## Files Changed, or 'none yet'}

   This will:
   • Set status = ABANDONED
   • Move feature file to {archive_dir}
   • Delete branch (if no commits) OR preserve it (if commits exist)

   Type 'yes' to confirm, anything else to cancel."

Wait for response. If not "yes": output "Abandon cancelled." and stop.

## Step 4 — Execute abandon

Check for commits on the feature branch:
  Run: git log --oneline {base_branch}..{branch}

If output is empty (no commits):
  Run: git branch -d {branch}
  Note: "Branch deleted — no commits."

If output has commits:
  Note: "Branch {branch} preserved — {N} commit(s) exist. Clean up manually."

## Step 5 — Archive

Update feature file:
  - status       = ABANDONED
  - updated      = current ISO date
  Append to ## Pipeline History: "[timestamp] | ABANDONED | orchestrator | developer request"

Write updated file to: {archive_dir}/{feature_id}-{slug}.md
Verify write succeeded.

Replace original file at {feature_dir}/{feature_id}-{slug}.md with:
  "# Archived — see {archive_dir}/{feature_id}-{slug}.md"

## Step 6 — Output

  "✓ {feature_id} — {title} abandoned.
   Archived: {archive_dir}/{feature_id}-{slug}.md
   Branch: {deleted | preserved — {branch}}"
