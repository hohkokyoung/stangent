Update the System Requirements Specification.

Usage:
  /srs                      — process all completed features since last SRS update
  /srs <FEAT-ID>            — update SRS for one specific completed feature
  /srs --dry-run            — show what would change without writing
  /srs --dry-run <FEAT-ID>  — dry-run for one specific feature

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir     = config.paths.feature_dir
  - srs_path        = config.paths.srs_path
  - log_dir         = config.paths.log_dir
  - config_path     = (absolute path to .stangent/config.json)

## Step 2 — Determine mode

Parse "$ARGUMENTS":
- `--dry-run` alone → standalone dry-run mode
- `--dry-run <FEAT-ID>` → single-feature dry-run mode
- `<FEAT-ID>` alone → single-feature mode
- empty → standalone mode (process all pending features)

Set `dry_run = true` if `--dry-run` flag is present.

In single-feature mode (with or without --dry-run): find {feature_dir}/$FEAT-ID*.md
If not found: output "Feature $FEAT-ID not found." and stop.
If status ≠ COMPLETE: output "Feature must be COMPLETE before SRS update." and stop.

## Step 3 — Run SRS agent (or dry-run)

**If dry_run = false:**
Read the full contents of: .claude/agents/stangent-srs.md

Execute the SRS agent with:
  - feature_id         : (resolved FEAT-ID, empty in standalone mode)
  - feature_file_path  : (resolved path, empty in standalone mode)
  - config_path        : (absolute path to .stangent/config.json)

**If dry_run = true:**
Do NOT execute the SRS agent. Instead, simulate what it would do:

1. Read the current SRS (or note it doesn't exist).
2. For each feature to process: generate the SRS subsection content that would be written.
3. For each existing SRS section that would be updated: show a diff block:
   ```diff
   --- current
   +++ proposed
   @@ section heading @@
   -old line
   +new line
   ```
4. List any PRESERVE blocks that would be retained.
5. Do NOT write to any file. Do NOT commit. Do NOT update frontmatter.

Output the diff and a summary: "X sections would be added, Y sections would be updated."
Then: "Run /srs (without --dry-run) to apply."

## Step 4 — Output result

**If dry_run = false:**
On UPDATED:
  Output:
    "✓ SRS updated — version {new_version}
     Sections updated: {list}
     Committed: docs(SRS): {commit_message}"

On SKIPPED:
  Output: "SRS is already up to date. No completed features since last update."

On FAILED:
  Output the error with: "Re-run /srs to retry."

**If dry_run = true:**
Output the diff from Step 3 dry-run. No further action.
