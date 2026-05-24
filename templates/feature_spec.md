---
id: {{feature_id}}
title: {{FEATURE_TITLE}}
slug: {{feature-slug}}
status: CREATED
language: {{language}}
branch: stangent/{{feature_id}}-{{feature-slug}}
retry_count: 0
replan_count: 0
spec_version: 1
tier: standard
created: {{ISO_DATE}}
updated: {{ISO_DATE}}
planner_agent_version:
implementer_agent_version:
reviewer_agent_version:
srs_agent_version:
---

<!-- PLANNER OWNED — locked after AWAITING_CONFIRMATION -->

## Scope
<!-- 2–5 sentences, no implementation detail -->


## Acceptance Criteria
<!-- Each item maps to at least one unit test. Implementer ticks, reviewer verifies. -->
- [ ]
- [ ]

## Out of Bounds
<!-- Hard constraint. Implementer ASKs before touching anything listed here. -->
-

## Depends On
<!-- FEAT IDs that must be COMPLETE first, or "none". -->
- none

## Files to Touch
<!-- Best-guess list; implementer writes actuals to ## Files Changed. -->
-

## Codebase Context
<!-- Populated by planner Phase 1c2; lets implementer skip Pass 2. -->
### Top Relevant Files
### Key Patterns Observed
### Interfaces to Respect

## Architectural Decisions Applied
<!-- "ADR-NNN — Title" | "ADR-NNN — OVERRIDDEN — Reason: ..." -->
-

## New Environment Variables
- none

## Risks & Mitigations
<!-- **Risk:** {description}  **Mitigation:** {approach} | **Approach:** {chosen option} -->
<!-- "none identified." if empty. -->

## Planner Confidence
score:
flags:
  - context_budget_hit:
  - unanswered_questions:
  - adr_conflicts_overridden:
  - files_not_found:
  - symbol_index_misses:


<!-- IMPLEMENTER OWNED -->

## Pre-Implementation Scan
<!-- file:line — what was found — reuse / adapt / ignore -->


## Implementation Log


## Files Changed
<!-- [C] created | [M] modified | [D] deleted -->


## Future Considerations
<!-- Out-of-scope ideas tracked here, not implemented. -->


## Implementer Confidence
score:
flags:
  - context_budget_hit:
  - ask_developer_used:
  - out_of_bounds_conflicts:
  - files_outside_touch_list:
  - test_coverage_dropped:


<!-- SUB-AGENT OWNED -->

## Linter Report
**Status:** PENDING
**Agent version:**
**Config used:**
**Command run:**
**Exit code:**
**Findings:**


## Test Report
**Status:** PENDING
**Agent version:**
**Command run:**
**Exit code:**
**Coverage before:**
**Coverage after:**
**Delta:**

**AC Coverage:**
| Acceptance Criterion | Test Name | Status |
|----------------------|-----------|--------|
|                      |           |        |

**Failing tests:**


## Query Analysis Report
**Status:** PENDING
**Agent version:**
**Skipped:**
**Danger findings (FAIL):**
**Warning findings (WARN):**


<!-- REVIEWER OWNED -->

## Scope Verdict
**Status:** PENDING
**Agent version:**
**In bounds:**
**Scope creep found:**


## Review Checklist
<!-- Populated from profile.review_checklist. [x] passed | [ ] failed: reason -->


## Security Report
**Status:** PENDING
**Agent version:**
**Secrets scan:** PENDING
**SAST scan:** PENDING
**Dependency audit:** PENDING
**Hardcoded config scan:** PENDING
**Findings:**


## Review Verdict
**Status:** PENDING
**Agent version:**

**CRITICAL issues:** none
**MAJOR issues:** none
**MINOR issues:** none
**Overall:** PENDING
<!-- On FAIL: actionable per file:line, not "improve security". -->

## Reviewer Confidence
score:
flags:
  - ambiguous_findings:
  - ask_developer_used:
  - cross_stack_drift_found:
  - files_changed_unreadable:


<!-- SRS AGENT OWNED -->

## SRS Reference
**Agent version:**
**SRS section:**
**API contracts documented:**
**Env vars documented:**
**SRS commit:**


<!-- PIPELINE OWNED (orchestrator only) -->

## Pipeline History
| Timestamp | Event | Agent | Detail |
|-----------|-------|-------|--------|
|           | CREATED | orchestrator | |
