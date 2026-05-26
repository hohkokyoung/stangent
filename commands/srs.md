Display or rebuild the feature requirements log (srs.jsonl).

Usage:
  /srs                 — display all entries in srs.jsonl as a readable summary
  /srs <FEAT-ID>       — display the srs.jsonl entry for one specific feature
  /srs rebuild         — rebuild srs.jsonl from all COMPLETE feature files

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir = config.paths.feature_dir
  - config_path = (absolute path to .stangent/config.json)

SRS data lives at: `.stangent/srs.jsonl` (one JSON line per completed feature).
It is written automatically by the reviewer agent on PASS.

## Step 2 — Determine mode

Parse "$ARGUMENTS":
- empty → display mode (all features)
- `<FEAT-ID>` alone → display mode (single feature)
- `rebuild` → rebuild mode

## Step 3 — Execute

**Display mode (all features):**

Read `.stangent/srs.jsonl`. If missing or empty: output
"No features in srs.jsonl yet. Features are added automatically when
the reviewer passes them." and stop.

Format as a readable summary:
```
# System Requirements Log  ({N} features)

| FEAT-ID | Title | Security | Updated |
|---------|-------|----------|---------|
| FEAT-001 | ... | PASS | 2026-05-12 |

## FEAT-001 — {title}
**Scope:** {scope}
**ACs:** {ac count} accepted
**Env vars:** {list | none}
**Security:** {security_summary}
```

**Display mode (single feature):**

Read `.stangent/srs.jsonl`, find entry where `feat_id == <FEAT-ID>`.
If not found: output "No srs.jsonl entry for {FEAT-ID}. Either the
feature is not COMPLETE, or run /srs rebuild." and stop.
Display the full entry in readable format.

**Rebuild mode:**

1. Glob `{feature_dir}/*.md` for all feature files.
2. For each feature file whose frontmatter `status == COMPLETE`:
   - Read the feature file.
   - Extract: `feat_id`, `title` (from frontmatter), scope from
     `## Scope`, ACs from checked `- [x]` items in `## Acceptance
     Criteria`, env vars from `## New Environment Variables`,
     security verdict from `## Review` security line.
   - Build srs.jsonl entry:
     ```json
     {"feat_id":"FEAT-NNN","title":"...","scope":"...","acs":["..."],"env_vars":["KEY"],"security_summary":"PASS|...","updated":"ISO"}
     ```
3. Write all entries to `.stangent/srs.jsonl` (overwrite).
4. Output: "Rebuilt srs.jsonl with {N} features."

## Step 4 — Output

Display mode: render the formatted summary.
Rebuild mode: confirm write with count.
