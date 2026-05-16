Clean up stale stangent artefacts: dead feature branches, orphaned contracts, and stale active.json.

Usage:
  /cleanup          — interactive: list stale items and ask before removing each
  /cleanup --dry-run  — list stale items without removing anything

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir   = config.paths.feature_dir
  - archive_dir   = config.paths.archive_dir
  - contracts_dir = config.paths.contracts_dir  (default: ".stangent/contracts/")
  - branch_prefix = config.pipeline.branch_prefix  (default: "stangent/")
  - base_branch   = config.pipeline.pr_target_branch  (default: "main")

dry_run = "$ARGUMENTS" contains "--dry-run"

---

## Step 2 — Find stale branches

Run: `git branch --list {{branch_prefix}}*`

For each listed branch:
  1. Find the FEAT-ID: extract from branch name (e.g. `stangent/FEAT-003-login` → `FEAT-003`)
  2. Find the feature file: glob `{feature_dir}/FEAT-003*.md`
     Also check `{archive_dir}/FEAT-003*.md`
  3. Read feature status from frontmatter

Classify:
  - status = COMPLETE or ABANDONED → **stale** (branch should be merged/deleted)
  - feature file not found → **orphaned** (no feature file, branch left over)
  - status = ESCALATED or FAILED and branch has no commits since base → **stale**
  - anything else → **active** (skip)

---

## Step 3 — Find orphaned contracts

List all files in `{contracts_dir}/*.json`.
For each contract:
  - Extract feature_id from filename
  - Check if feature file exists in feature_dir or archive_dir
  - If not found: classify as **orphaned contract**

---

## Step 4 — Check for stale active.json

Check `.stangent/gateway/active.json`:
- If exists: read feature_id
- Check feature status
- If status is COMPLETE, ABANDONED, ESCALATED, or FAILED → classify as **stale active.json**
- Also check `.stangent/gateway/active.json.paused` — always offer to clean up

---

## Step 5 — Report and confirm

Output a report:

```
Stale branches ({N}):
  stangent/FEAT-003-login     — COMPLETE  (merged or delete-safe)
  stangent/FEAT-007-payments  — ABANDONED (no commits)

Orphaned contracts ({N}):
  .stangent/contracts/FEAT-005.json  — no feature file found

Stale active.json: {yes — feature_id FEAT-003 is COMPLETE | no}
```

If nothing stale: output "Nothing to clean up." and stop.

If dry_run: output "Dry run — nothing removed." and stop.

Ask: "Remove all of the above? (yes / no / select)"
- "yes" → remove all
- "no" → cancel
- "select" → list each item and ask yes/no individually

---

## Step 6 — Remove stale branches

For each confirmed stale branch:

  Check commits on branch beyond base:
    `git log --oneline {base_branch}..{branch}`

  If no commits: `git branch -d {branch}`
    Output: "Deleted {branch} — no commits."

  If commits exist:
    Ask: "{branch} has {N} commit(s). Delete anyway? (yes/no)"
    If "yes": `git branch -D {branch}`
      Output: "Force-deleted {branch} — {N} commit(s) discarded."
    If "no": output "Kept {branch}."

---

## Step 7 — Remove orphaned contracts

For each confirmed orphaned contract:
  Delete the file.
  Output: "Deleted {contract_file}"

---

## Step 8 — Remove stale active.json

If confirmed:
  Delete `.stangent/gateway/active.json` (if present).
  Delete `.stangent/gateway/active.json.paused` (if present).
  Output: "Cleared stale gateway state."

---

## Step 9 — Summary

```
Cleanup complete.
  Branches removed:  {N}
  Contracts removed: {N}
  Gateway cleared:   {yes | no}
```
