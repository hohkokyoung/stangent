Remove Stangent from the current project.

Usage:
  /uninit            — interactive removal with confirmation
  /uninit --soft     — remove tooling only, keep feature data (.stangent/ intact)
  /uninit --hard     — remove everything including .stangent/ (destroys all feature history)

Default (no flag) prompts you to choose soft or hard.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist:
  Check if any stangent artefacts exist (see Step 2).
  If nothing found: output "Stangent is not initialised in this project." and stop.
  If artefacts found: warn and continue (will clean up partial installation).

Extract (if config exists):
  - feature_dir   = config.paths.feature_dir
  - archive_dir   = config.paths.archive_dir
  - contracts_dir = config.paths.contracts_dir  (default: ".stangent/contracts/")
  - registry_path = config.paths.registry_path

---

## Step 2 — Inventory

Scan for everything Stangent installed:

**Tooling (always removable):**
  - `.claude/agents/stangent*.md`     (list all matching files)
  - `.claude/agents/subagents/stangent*.md`
  - `.claude/commands/abandon.md`, `adr.md`, `cleanup.md`, `doctor.md`,
    `feature.md`, `gateway.md`, `implement.md`, `plan.md`, `resume.md`,
    `review.md`, `srs.md`, `status.md`, `uninit.md`
    (only count those that exist)
  - `.stangent/gateway/gateway.py`
  - PreToolUse hook entry in `.claude/settings.json`

**Project data (only removed in --hard mode):**
  - `.stangent/` directory (entire tree)
  - Stangent block in `.gitignore`

Build counts:
  - agent_files      : N files
  - command_files    : N files
  - has_gateway_hook : yes/no
  - feature_count    : number of .md files in feature_dir + archive_dir (0 if no config)
  - has_stangent_dir : yes/no

---

## Step 3 — Choose mode

If "--soft" in "$ARGUMENTS": mode = SOFT
If "--hard" in "$ARGUMENTS": mode = HARD

If neither flag:
  Output:
    "Stangent uninit — {project_root}

     What would you like to remove?

     [soft] Remove tooling only — keeps your feature files, SRS, and decisions.
            Safe to re-init later with 'python init.py'.
            Removes: {N} agent files, {N} command files, gateway hook in settings.json

     [hard] Remove everything — deletes .stangent/ and all feature history.
            WARNING: {feature_count} feature file(s) will be permanently deleted.
            This cannot be undone.

     Type 'soft', 'hard', or 'cancel':"

  Wait for response.
  - "soft"   → mode = SOFT
  - "hard"   → mode = HARD
  - anything else → output "Uninit cancelled." and stop.

---

## Step 4 — Final confirmation

**SOFT mode:**
  Output:
    "This will remove:
       • {N} agent files in .claude/agents/
       • {N} command files in .claude/commands/
       • Gateway hook from .claude/settings.json
       • .stangent/gateway/gateway.py

     Your feature files, SRS.md, decisions.md, and config.json are kept.
     Re-init later with: python <path-to-stangent>/init.py

     Type 'yes' to confirm, anything else to cancel:"

**HARD mode:**
  Count all files in `.stangent/` recursively.
  Output:
    "WARNING — this will permanently delete:
       • {N} agent files in .claude/agents/
       • {N} command files in .claude/commands/
       • Gateway hook from .claude/settings.json
       • .stangent/ — {file_count} file(s) including all feature history, SRS, and decisions
       • Stangent entries from .gitignore

     {feature_count} feature(s) will be lost. This cannot be undone.

     Type 'yes, delete everything' (exact phrase) to confirm, anything else to cancel:"

Wait for response.
- SOFT: if "yes" → proceed. Else: output "Uninit cancelled." and stop.
- HARD: if "yes, delete everything" (exact) → proceed. Else: output "Uninit cancelled." and stop.

---

## Step 5 — Remove agent files

For each file matching `.claude/agents/stangent*.md`:
  Delete it.
  Output: "  Deleted .claude/agents/{filename}"

For each file matching `.claude/agents/subagents/stangent*.md`:
  Delete it.
  Output: "  Deleted .claude/agents/subagents/{filename}"

If the `.claude/agents/subagents/` directory is now empty: remove it.

---

## Step 6 — Remove command files

The following command filenames are installed by Stangent:
  abandon.md, adr.md, cleanup.md, doctor.md, feature.md, gateway.md,
  implement.md, plan.md, resume.md, review.md, srs.md, status.md, uninit.md

For each that exists in `.claude/commands/`:
  Delete it.
  Output: "  Deleted .claude/commands/{filename}"

---

## Step 7 — Remove gateway hook from settings.json

Read `.claude/settings.json`.
If not found: skip.

Remove the PreToolUse hook entry whose hooks[].command contains "gateway.py".
If the PreToolUse array is now empty, remove the key.
If hooks is now empty, remove the key.

Write the updated JSON back.
Output: "  Removed gateway hook from .claude/settings.json"

If other settings remain in settings.json: output "  (other settings preserved)"

---

## Step 8 — Remove gateway.py (both modes)

If `.stangent/gateway/gateway.py` exists: delete it.
Output: "  Deleted .stangent/gateway/gateway.py"

If `.stangent/gateway/active.json` exists: delete it.
If `.stangent/gateway/active.json.paused` exists: delete it.

If `.stangent/gateway/` directory is now empty: remove it.

---

## Step 9 — HARD mode only: remove .stangent/

Delete `.stangent/` and all contents recursively.
Output: "  Deleted .stangent/"

Remove the stangent block from `.gitignore`:
  Find the block that starts with "# Stangent" and remove it plus all
  lines until the next blank line or non-stangent entry.
  Output: "  Removed stangent entries from .gitignore"

---

## Step 10 — Summary

**SOFT:**
  ```
  Stangent tooling removed from {project_root}.

  Kept:
    .stangent/config.json
    .stangent/features/     ({N} feature files)
    .stangent/SRS.md
    .stangent/decisions.md

  To re-install: python <path-to-stangent>/init.py
  ```

**HARD:**
  ```
  Stangent fully removed from {project_root}.

  All agent files, commands, gateway hooks, and .stangent/ data have been deleted.

  To start fresh: python <path-to-stangent>/init.py
  ```
