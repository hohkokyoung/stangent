Cleanly abandon a feature. Reverts code changes, archives the feature file,
and removes the branch.

Usage: /abandon <FEAT-ID>
Example: /abandon FEAT-003

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir   = config.paths.feature_dir
  - archive_dir   = config.paths.archive_dir
  - base_branch   = config.pipeline.pr_target_branch (default: "main" if not set)

## Step 2 — Validate

If "$ARGUMENTS" is empty: output "Usage: /abandon <FEAT-ID>" and stop.

Find the feature file: glob {feature_dir}/$ARGUMENTS*.md
If not found: output "Feature $ARGUMENTS not found." and stop.

Read frontmatter: status, title, branch, spec_version, replan_count, retry_count.

Check status:
  - COMPLETE  → output "Feature is already complete. Cannot abandon.
                         If you want to undo it, use: /abandon {feature_id} after checking out its branch." and stop.
  - ABANDONED → output "Already abandoned." and stop.
  - Any other → proceed.

## Step 3 — Check branch state

Run: git log --oneline {base_branch}..{branch}
Store result as `commit_log`. Count lines as `commit_count`.

Run: git branch --list {branch}
If output is empty: branch does not exist locally — set `branch_exists = false`.
Else: set `branch_exists = true`.

## Step 4 — Confirm

Build a context block:

  If spec_version > 1 or replan_count > 0:
    spec_note = "Spec version: {spec_version} (refined {replan_count} time(s))"
  Else:
    spec_note = "Spec version: 1 (never refined)"

  If commit_count > 0:
    code_note = "{commit_count} commit(s) on branch — code changes exist and will be reverted."
  Else:
    code_note = "No commits on branch — nothing to revert."

Present to developer:
  "Abandon {feature_id} — {title}?
   Status:       {status}
   Branch:       {branch}
   {spec_note}
   Retries:      {retry_count}
   Files changed: {## Files Changed count, or 'none yet'}

   {code_note}

   This will:
   • Revert all commits on {branch} back to {base_branch} (if any exist)
   • Set status = ABANDONED
   • Move feature file to {archive_dir}/
   • Delete the feature branch

   Type 'yes' to confirm, anything else to cancel."

Wait for response. If not "yes": output "Abandon cancelled." and stop.

## Step 5 — Revert code changes

Delete `.stangent/gateway/active.json` if it exists.
Delete `.stangent/gateway/active.json.paused` if it exists.
(Disarms the gateway before running git commands — without active.json the
gateway allows all tool calls unconditionally.)

If `branch_exists = false`: skip the rest of this step entirely.

Check current branch: git rev-parse --abbrev-ref HEAD

If not already on the feature branch:
  Run: git checkout {branch}
  If this fails: output "Could not switch to branch {branch}. Aborting." and stop.

If `commit_count > 0`:
  Run: git revert --no-edit {base_branch}..HEAD

  This creates one revert commit per original commit, restoring the codebase
  to its pre-feature state without destroying history.

  If git revert fails (merge conflict):
    Output:
      "⚠ Revert hit a conflict on {branch}.
       Resolve the conflict manually, then run:
         git revert --continue
       Then re-run /abandon {feature_id} to finish archiving."
    Stop.

  Note: "Reverted {commit_count} commit(s) — codebase restored to {base_branch} state."

Switch back to base branch:
  Run: git checkout {base_branch}

Delete the feature branch:
  Run: git branch -d {branch}
  If -d fails (unmerged): run git branch -D {branch}
  Note: "Branch {branch} deleted."

If `commit_count = 0`:
  If `branch_exists = true`: run git branch -d {branch}
  Note: "Branch deleted — no commits."

## Step 6 — Archive

Update feature file frontmatter:
  - status       = ABANDONED
  - updated      = current ISO date

Append to ## Pipeline History:
  "[timestamp] | ABANDONED | orchestrator | developer request — spec v{spec_version}, {replan_count} refinement(s), {retry_count} retry/retries, {commit_count} commit(s) reverted"

Delete `.stangent/contracts/{feature_id}.json` if it exists.

Write updated file to: {archive_dir}/{feature_id}-{slug}.md
Verify write succeeded.

Replace original file at {feature_dir}/{feature_id}-{slug}.md with:
  "# Archived — see {archive_dir}/{feature_id}-{slug}.md"

Run Registry Update procedure (status: ABANDONED).

Write to project memory (read `.stangent/prompts/memory.md` and follow the
write protocol — skip gracefully if memory.md prompt not found):

  Always append to ## Feature History:
  `| {feature_id} | {title} | {retry_count} | {replan_count} | {key files from ## Files to Touch} | ABANDONED |`

  If retry_count > 0 or replan_count > 0:
    Read ## Files to Touch for the affected area.
    Append to ## Failure Patterns:
    `| {area from Files to Touch} | {title} | ABANDONED after {retry_count} retries, {replan_count} refinements | 1 |`

## Step 7 — Output

  "✓ {feature_id} — {title} abandoned.
   Spec was at v{spec_version} ({replan_count} refinement(s)).
   Code: {commit_count} commit(s) reverted — {base_branch} is clean.
   Archived: {archive_dir}/{feature_id}-{slug}.md"
