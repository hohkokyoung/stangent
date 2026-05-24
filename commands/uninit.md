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

Stangent writes `.stangent/.installed.json` at init time listing every file
it installed. Read it first — it is the source of truth.

If `.stangent/.installed.json` exists:
  Read it. Use the `files.commands`, `files.agents`, `files.subagents`,
  `files.skills`, `files.stangent_internal` lists as the inventory.

If `.stangent/.installed.json` does NOT exist (old install or corruption):
  Fall back to globs:
    - `.claude/agents/stangent*.md`
    - `.claude/agents/subagents/stangent*.md`
    - `.claude/commands/*.md` — for each, check if a same-named file
      exists in the stangent source `commands/` dir. If yes: it's ours.
    - `.claude/skills/*.md` — same check against stangent source `skills/`.
    - `.stangent/gateway/gateway.py`
    - `.stangent/observer/observer.py`
    - `.stangent/scripts/build_index.py`
    - `.stangent/scripts/validate_handoff.py`

Always include:
  - PreToolUse and PostToolUse hook entries in `.claude/settings.json`

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
     Re-init later with: python {config.stangent_source_path}/init.py

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

## Step 6 — Remove skill files

Use the `files.skills` list from `.stangent/.installed.json` (or fallback
glob from Step 2).

For each path:
  Delete it.
  Output: "  Deleted {path}"

If the `.claude/skills/` directory is now empty: remove it.

---

## Step 7 — Remove command files

Use the `files.commands` list from `.stangent/.installed.json` (or fallback
glob from Step 2 if manifest absent).

For each path in the inventory:
  Delete it.
  Output: "  Deleted {path}"

---

## Step 8 — Remove gateway hook from settings.json

Read `.claude/settings.json`.
If not found: skip.

Remove the PreToolUse hook entry whose hooks[].command contains "gateway.py".
If the PreToolUse array is now empty, remove the key.
If hooks is now empty, remove the key.

Write the updated JSON back.
Output: "  Removed gateway hook from .claude/settings.json"

If other settings remain in settings.json: output "  (other settings preserved)"

---

## Step 8b — Remove stangent_internal files (both modes)

Use the `files.stangent_internal` list from `.stangent/.installed.json`.
For each path: delete it. Output: "  Deleted {path}"

Always also remove these (whether or not in manifest):
  - `.stangent/gateway/active.json`
  - `.stangent/gateway/active.json.paused`
  - `.stangent/.installed.json` (the manifest itself)

For each removed file's parent directory: if now empty, remove the directory.

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

  To re-install: python {config.stangent_source_path}/init.py
  ```

**HARD:**
  ```
  Stangent fully removed from {project_root}.

  All agent files, commands, gateway hooks, and .stangent/ data have been deleted.

  To start fresh: python {config.stangent_source_path}/init.py
  ```
