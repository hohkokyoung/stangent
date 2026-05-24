# Project Memory

Stangent maintains a persistent memory file at `.stangent/memory.md`.
This file accumulates knowledge across features — failure patterns, developer
preferences, and project-level insights. Read it at the start of every run.
Write to it only when you have new, concrete information to record.

---

## When to read

**Planner:** Read before asking clarifying questions.
- Check Failure Patterns against the files in your planned scope.
  If any match, flag the risk proactively in ## Scope or as a question.
- Check Developer Preferences — apply them silently. Do not re-ask things
  already captured there.
- Check Project Insights — use them to inform which areas need extra attention.

**Implementer:** Read before the pre-implementation scan.
- Check Failure Patterns for the files you are about to touch.
  If a file has failed linting or tests before, read it more carefully.
- Check Developer Preferences — apply commit style, diff size preferences, etc.

**Reviewer:** Read before running the review checklist.
- Check Failure Patterns — give extra scrutiny to areas that have failed before.

**Orchestrator:** Read after every stage completes.
- Write new entries when a feature reaches COMPLETE or ESCALATED.

---

## When to write

Memory is written by the orchestrator (on COMPLETE or ESCALATED) and by the
`/abandon` command (on ABANDONED). Individual agents do not write — they surface
observations in the feature file and the orchestrator or command consolidates.

**Write a Failure Pattern entry if:**
- The feature needed > 0 retries AND the failure was in a specific file or area
- A security scanner finding appeared in the same area as a previous finding

**Write a Developer Preference entry if:**
- The developer explicitly rejected an implementation choice during confirmation
- The developer asked to change something that agents do by default
  (e.g. split a diff, change commit message style, skip a sub-agent)
- Only write preferences that will apply to future features, not one-off decisions

**Write a Project Insight entry if:**
- A pattern emerged that is not captured by any ADR but is clearly consistent
  (e.g. "coverage is always low in this module", "this file is always in scope")
- An ADR was overridden more than once — it may need revision

**Write a Feature History entry for every completed feature.**

---

## How to write

Append to the relevant section via `Edit` (never `Write` the whole file).
Never rewrite existing entries — only add. Use the formats defined below.

If memory.md does not exist, skip gracefully. The orchestrator creates it
during feature initialisation via the init scaffold.

---

## Growth control (rolling window)

`memory.md` is read by the planner, implementer, reviewer, and
orchestrator on every run. Unbounded growth means every feature pays a
larger memory-read cost than the last. After appending, the orchestrator
applies the following caps using `pipeline.memory_row_cap` from
`.stangent/config.json` (default 100):

- **## Feature History** — keep the most recent `memory_row_cap` rows.
  Drop older rows from the top of the table (preserving the header).
- **## Failure Patterns** — keep the most recent `memory_row_cap` rows
  using the same drop-from-top rule. Patterns with `count > 1` are
  *sticky*: never dropped, regardless of age.
- **## Developer Preferences** and **## Project Insights** are not
  capped — they grow slowly and are high-signal.

Implementation note for the orchestrator: after the append in STEP 7e /
ESCALATE, count rows in each capped section. If `rows > memory_row_cap`,
issue one `Edit` that replaces the over-cap leading rows with just the
header. Do **not** rewrite the whole file.

If `memory_row_cap` is absent from config: use 100 (do not warn — older
configs are valid).

---

## Format reference

### Failure Pattern entry
```
| {feature_id} | {stage: LINT/TEST/REVIEW/SECURITY} | {description of pattern} | {file or area} | {count: increment if same pattern repeats} |
```

### Developer Preference entry
```
- {preference statement, present tense} (learned from {feature_id})
```

### Project Insight entry
```
- {insight statement} (observed across {feature_id_list})
```

### Feature History entry
```
| {feature_id} | {title} | {retry_count} | {replan_count} | {comma-separated key files touched} | {COMPLETE/ESCALATED/ABANDONED} |
```
