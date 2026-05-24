Audit and repair consistency between the features registry and files on disk.
Detects ghost entries, orphan files, and ID gaps — and optionally fixes them.

Usage:
  /compact              — audit only: report all issues, make no changes
  /compact --fix        — remove ghost entries, register orphan files
  /compact --renumber   — fill ID gaps by renumbering (implies --fix, asks confirmation)
  /compact --dry-run    — same as audit, explicit label

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir    = config.paths.feature_dir
  - archive_dir    = config.paths.archive_dir
  - registry_path  = config.paths.registry_path
  - branch_prefix  = config.pipeline.branch_prefix  (default: "stangent/")

Parse flags from "$ARGUMENTS":
  fix_mode      = "$ARGUMENTS" contains "--fix" or "--renumber"
  renumber_mode = "$ARGUMENTS" contains "--renumber"
  dry_run       = "$ARGUMENTS" contains "--dry-run"

If dry_run: treat as audit only (no changes), label output "DRY RUN".

---

## Step 2 — Load registry

Read `{registry_path}` (features_registry.json). Parse JSON.

If missing or unparseable:
  Output: "[FAIL] Registry not found or invalid JSON at {registry_path}. Cannot audit."
  Stop.

Hold:
  - registry_features = registry.features  (map of feature_id → entry)
  - registry_next_id  = registry.next_id
  - registry_prefix   = registry.prefix   (default: "FEAT")
  - registry_padding  = registry.padding  (default: 3)

---

## Step 3 — Scan files on disk

Glob `{feature_dir}/FEAT-*.md` and `{archive_dir}/FEAT-*.md`.

For each file found:
  - Extract feature_id from filename: the leading FEAT-NNN portion
    (e.g. `FEAT-007-login-screen.md` → `FEAT-007`)
  - Read frontmatter: status, title, branch, tier, retry_count, spec_version, created, updated
  - Store as disk_features map: feature_id → { path, status, title, ... }

---

## Step 4 — Cross-reference

**Ghost entries** — in registry but no file on disk:
  For each feature_id in registry_features:
    If feature_id NOT in disk_features:
      Add to ghosts list: { feature_id, registry entry (title, status) }

**Orphan files** — file on disk but not in registry:
  For each feature_id in disk_features:
    If feature_id NOT in registry_features:
      Add to orphans list: { feature_id, path, status, title }

**ID gaps** — missing numbers between lowest and highest known ID:
  Collect all known IDs: union of registry_features keys + disk_features keys.
  Parse numeric part of each (e.g. FEAT-007 → 7).
  If none found: no gap analysis possible.
  Otherwise:
    min_id = minimum numeric value
    max_id = maximum numeric value
    gaps   = [n for n in range(min_id, max_id+1) if "{prefix}-{n:0{padding}d}" not in known]
    Build gaps list: ["{prefix}-{n:0{padding}d}" for n in gaps]

**next_id drift** — registry.next_id should be > max known ID:
  If max_id exists and registry_next_id <= max_id:
    Note as drift: next_id_drift = True, expected_next = max_id + 1

---

## Step 5 — Report findings

Output:

```
━━━ COMPACT AUDIT ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Registry: {registry_path}
  Known entries:  {count of registry_features}
  Files on disk:  {count of disk_features}  ({feature_dir} + {archive_dir})
  Registry next:  FEAT-{registry_next_id:0{padding}d}
```

If no issues found:
  Output: "  Everything looks clean — no ghosts, orphans, or gaps."
  Skip to Step 9.

If ghost entries:
```
GHOST ENTRIES ({n}) — in registry, no file on disk:
  FEAT-XXX  "{title}"  [{status}]
  FEAT-XXX  "{title}"  [{status}]
  → Fix: /compact --fix  removes these from the registry.
```

If orphan files:
```
ORPHAN FILES ({n}) — file exists, not in registry:
  FEAT-XXX  {path}  [{status}]  "{title}"
  FEAT-XXX  {path}  [{status}]  "{title}"
  → Fix: /compact --fix  registers these in the registry.
```

If gaps:
```
ID GAPS ({n}) — missing feature IDs:
  {FEAT-006, FEAT-007, FEAT-008}  (between FEAT-005 and FEAT-009)
  → Fix: /compact --renumber  reassigns IDs to fill gaps.
  Note: renumber only moves COMPLETE and ABANDONED features.
```

If next_id drift:
```
NEXT_ID DRIFT — registry.next_id is {registry_next_id}, but max known ID is FEAT-{max_id}.
  → Will correct to {max_id + 1} when --fix or --renumber is applied.
```

---

## Step 6 — Audit-only exit

If dry_run OR (not fix_mode AND not renumber_mode):
  If issues were found:
    Output: "Run /compact --fix to repair ghost entries and orphans."
    If gaps: Output: "Run /compact --renumber to also fill ID gaps (implies --fix)."
  Stop.

---

## Step 7 — Apply --fix (ghost removal + orphan registration)

(Skip this step if no ghosts and no orphans and no next_id_drift.)

**7a. Remove ghost entries:**

For each ghost entry:
  Confirm: output "  Removing ghost: {feature_id} ({title}) [{status}] from registry."

  Acquire registry lock (write `.stangent/features_registry.lock` with
  `{"locked_at": "{ISO timestamp}", "branch": "compact"}`).
  Read registry. Delete registry.features[feature_id]. Write registry back.
  Release lock (delete `.stangent/features_registry.lock`).

  Output: "  [FIXED] Removed ghost entry {feature_id}."

**7b. Register orphan files:**

For each orphan file:
  Output: "  Registering orphan: {feature_id} ({title}) [{status}]."

  Acquire registry lock.
  Read registry. Add entry:
    registry.features[feature_id] = {
      "title":        "{title from frontmatter}",
      "status":       "{status from frontmatter}",
      "branch":       "{branch from frontmatter, or ''}",
      "retry_count":  {retry_count from frontmatter, or 0},
      "replan_count": {replan_count from frontmatter, or 0},
      "spec_version": {spec_version from frontmatter, or 1},
      "created":      "{created from frontmatter, or today's date}",
      "updated":      "{today's date}"
    }
  Write registry back. Release lock.
  Output: "  [FIXED] Registered orphan {feature_id}."

**7c. Correct next_id drift (if any):**

  If next_id_drift:
    Acquire registry lock.
    Read registry. Set registry.next_id = max_id + 1. Write back. Release lock.
    Output: "  [FIXED] Corrected next_id to {max_id + 1}."

---

## Step 8 — Apply --renumber (fill ID gaps)

(Only if renumber_mode AND gaps is non-empty.)

**8a. Safety check — which features can be renumbered:**

Only COMPLETE and ABANDONED features are safe to renumber.
For any feature that has gaps to fill by being moved:

  Collect renumber_candidates: all disk_features where status IN
  (COMPLETE, ABANDONED) — sorted by current numeric ID ascending.

  Collect blocked: all disk_features where status NOT IN (COMPLETE, ABANDONED).
  If blocked is non-empty:
    Output:
    ```
    RENUMBER BLOCKED — {n} active feature(s) cannot be renumbered:
      FEAT-XXX  [{status}]  "{title}"  ← must be COMPLETE or ABANDONED first
    ```
    If after skipping blocked features there are still gaps between COMPLETE/ABANDONED IDs:
      Output: "Proceeding with safe renumbers only (active features keep their IDs)."
    Else:
      Output: "No safe renumbers possible while active features exist. Resolve them first."
      Stop renumber phase, continue to Step 9.

**8b. Build renumber plan:**

Re-assign IDs to fill gaps, keeping relative order of features:

  Take all known IDs (registry + disk), sorted by numeric value.
  Walk them in order. Assign new sequential IDs starting from 1 (or the
  first non-blocked ID — blocked features keep their existing ID).

  Build plan: list of (old_id, new_id, path, title, status, branch)
  Exclude entries where old_id == new_id (nothing to do).

  If plan is empty:
    Output: "No renumbers needed — IDs are already sequential."
    Skip to Step 9.

**8c. Show renumber plan and warn about branches:**

Output:
```
RENUMBER PLAN ({n} moves):
  FEAT-009 → FEAT-006  "{title}"  [COMPLETE]   branch: {branch or 'none'}
  FEAT-010 → FEAT-007  "{title}"  [ABANDONED]  branch: {branch or 'none'}
```

Check git branches: run `git branch --list {branch_prefix}*`

For each plan entry where branch is set:
  If branch exists in git:
    Output: "  WARNING: branch '{branch}' still exists in git — cannot be renamed automatically.
             You will need to: git branch -m {branch} {new_branch_name}"
    Mark as has_branch_warning = true.

Output:
```
This will:
  - Rename feature files on disk
  - Update feature_id: in frontmatter of each file
  - Update registry keys and next_id
  - NOT rename any git branches (manual step required — see warnings above)

Type "yes i want to renumber" to confirm, or anything else to cancel:
```

Read confirmation. If response != "yes i want to renumber" (exact, case-insensitive):
  Output: "Renumber cancelled."
  Skip to Step 9.

**8d. Execute renumber plan:**

For each (old_id, new_id, path, title, status, branch) in plan:

  1. Derive new filename:
       old_slug = filename with old_id prefix removed (e.g. "FEAT-009-login.md" → "-login.md")
       new_filename = new_id + old_slug   (e.g. "FEAT-006-login.md")
       new_path = same directory as old path, with new_filename

  2. Read file content.
     Replace `feature_id: {old_id}` with `feature_id: {new_id}` in frontmatter.
     Write to new_path.
     Delete old_path.

  3. Acquire registry lock.
     Read registry.
     old_entry = registry.features[old_id]
     Delete registry.features[old_id].
     registry.features[new_id] = old_entry
     Write registry back. Release lock.

  Output: "  [RENAMED] {old_id} → {new_id}  ({old_filename} → {new_filename})"

After all renames:
  Acquire registry lock. Read registry.
  Set registry.next_id = max(all new IDs numerically) + 1.
  Write registry back. Release lock.
  Output: "  [FIXED] next_id set to {new next_id}."

---

## Step 9 — Summary

Output:

```
━━━ COMPACT SUMMARY ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Ghosts removed:    {N}
  Orphans registered:{N}
  Features renamed:  {N}
  next_id corrected: {yes | no}

Registry is now: {count} features, next = FEAT-{next_id:0{padding}d}
```

If has_branch_warning:
  Output:
  ```
  ACTION REQUIRED — rename git branches manually:
    git branch -m <old-branch> <new-branch>
  ```

If renumber applied but blocked features were skipped:
  Output: "Tip: resolve active features then re-run /compact --renumber to fill remaining gaps."

If no changes were made (audit mode or nothing to fix):
  Output: "No changes made."
