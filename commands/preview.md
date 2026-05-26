Render a feature spec in a readable format, with diff against previous spec_version if available.

Usage:
  /preview <FEAT-ID>           — render the spec
  /preview <FEAT-ID> --diff    — show diff vs previous spec_version (uses git)

Useful when:
  - The spec is too long to read inline in the terminal
  - You want to see exactly what /refine changed
  - You're reviewing someone else's branch

---

## Step 1 — Load and locate

Read `.stangent/config.json`. Extract feature_dir, archive_dir.

Find the feature file: glob {feature_dir}/$ARGUMENTS*.md (also archive_dir).
If not found: output "Feature not found." and stop.

Read frontmatter: title, status, tier, spec_version, branch, retry_count, replan_count.

## Step 2 — Render

Output a clean summary (not raw markdown):

```
━━━ {feature_id}: {title} ━━━━━━━━━━━━━━━━━━━━━━

Status:    {status}        Tier: {tier}
Branch:    {branch}        Spec version: {spec_version}
Retries:   {retry_count}   Replans: {replan_count}

SCOPE
{## Scope content — wrapped to 80 chars}

ACCEPTANCE CRITERIA
{## Acceptance Criteria — checkbox state preserved}

OUT OF BOUNDS
{## Out of Bounds — bullet list}

FILES TO TOUCH
{## Files to Touch — bullet list}

ARCHITECTURAL DECISIONS
{## Architectural Decisions Applied — or "none" if Direct tier}

RISKS
{## Risks & Mitigations — or "none identified" or omitted for Direct tier}

DEPENDENCIES
{## Depends On}
```

If status is past IMPLEMENTING, also show:
```
IMPLEMENTATION SUMMARY
{## Implementation Log — first 500 chars}

FILES CHANGED
{## Files Changed}
```

If status is past REVIEWING, also show:
```
REVIEW VERDICT
{## Review — first 800 chars}
```

## Step 3 — Diff mode (only if --diff in $ARGUMENTS)

If `spec_version > 1` AND feature is in git:
  Find the commit where spec_version was last bumped:
    git log --diff-filter=M -G "spec_version: {spec_version - 1}" -- {feature_file_path}

  If found:
    Run: git diff {commit}~1..{commit} -- {feature_file_path}
    Filter the diff to only planner-owned sections (Scope, AC, Out of Bounds,
    Files to Touch, Risks). Show that.
  Else:
    Output: "No prior spec_version commit found — cannot show diff."

If `spec_version == 1`: output "This is spec v1 — no diff available." and stop.

## Step 4 — Footer

```
Full file: {feature_file_path}
Test:      /test {feature_id}    Refine: /refine {feature_id} <feedback>
PR:        /pr {feature_id}      Resume: /resume {feature_id}
```
