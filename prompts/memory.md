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

Only the orchestrator writes to memory, and only after a feature resolves
(COMPLETE or ESCALATED). Individual agents do not write — they surface
observations in the feature file and the orchestrator consolidates.

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

Append to the relevant section. Never rewrite existing entries — only add.
Use the formats defined in each section of memory.md.

If memory.md does not exist, skip gracefully. The orchestrator creates it
during feature initialisation via the init scaffold.

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
| {feature_id} | {title} | {retry_count} | {comma-separated key files touched} | {COMPLETE/ESCALATED} |
```
