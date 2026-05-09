Show the Stangent pipeline dashboard for this project.

Usage:
  /status              — full dashboard of all features
  /status <FEAT-ID>    — detailed view of one feature

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir   = config.paths.feature_dir
  - archive_dir   = config.paths.archive_dir
  - srs_path      = config.paths.srs_path
  - max_retries   = config.pipeline.max_retries

## Step 2 — Determine mode

If "$ARGUMENTS" contains a FEAT-ID → SINGLE FEATURE MODE
Otherwise → DASHBOARD MODE

---

## DASHBOARD MODE

Read all files matching {feature_dir}/FEAT-*.md
Read all files matching {archive_dir}/FEAT-*.md modified in the last 7 days.

Group by status. Output:

```
━━━ STANGENT STATUS ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

ACTIVE
  FEAT-XXX  Title                       IMPLEMENTING    retry N/M
  ...

AWAITING YOUR INPUT
  FEAT-XXX  Title                       AWAITING_CONFIRMATION
  FEAT-XXX  Title                       PAUSED          since [timestamp]
  ...

BLOCKED
  FEAT-XXX  Title                       BLOCKED BY: FEAT-YYY ([their status])
  ...

ESCALATED
  FEAT-XXX  Title                       ESCALATED       retry M/M
  ...

COMPLETE (last 7 days)
  FEAT-XXX  Title                       COMPLETE        [date]
  ...

ABANDONED
  FEAT-XXX  Title                       ABANDONED       [date]
  ...

━━━ SUMMARY ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Active: N  |  Blocked: N  |  Escalated: N  |  Complete (7d): N
  SRS: version X.Y.Z — last updated [date]
```

If no features exist:
  Output: "No features yet. Start one with: /feature <description>"

---

## SINGLE FEATURE MODE

Find {feature_dir}/$ARGUMENTS*.md — also check archive_dir.
If not found: output "Feature $ARGUMENTS not found." and stop.

Output:

```
━━━ $ARGUMENTS: {title} ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Status:    {status}
Branch:    {branch}
Retries:   {retry_count} / {max_retries}
Created:   {created}
Updated:   {updated}

Acceptance Criteria:
  [x] Done item
  [ ] Pending item

Sub-agent Results:
  Linter:          {status}
  Tests:           {status}    coverage: X% → Y%
  Query analysis:  {status}
  Security:        {status}

Review:            {status}

Last pipeline event:
  {most recent ## Pipeline History row}

Run log: {log_dir}/{feature_id}.jsonl
```
