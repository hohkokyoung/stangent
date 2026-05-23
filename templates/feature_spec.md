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
created: {{ISO_DATE}}
updated: {{ISO_DATE}}
planner_agent_version:
implementer_agent_version:
reviewer_agent_version:
srs_agent_version:
---

<!-- ═══════════════════════════════════════════════════════════════════════════
     PLANNER OWNED
     Written by: planner agent
     Read by: all agents
     Locked after: status passes AWAITING_CONFIRMATION
     ═══════════════════════════════════════════════════════════════════════ -->

## Scope
<!-- 2–5 sentences. What this feature does. No implementation detail. -->


## Acceptance Criteria
<!-- Each item must map to at least one unit test. -->
<!-- Implementer checks these off. Reviewer verifies each box. -->
- [ ]
- [ ]

## Out of Bounds
<!-- HARD CONSTRAINT. Implementer reads this before writing a single line. -->
<!-- If implementation would touch any item here: ASK_DEVELOPER first. -->
-

## Depends On
<!-- Feature IDs that must be COMPLETE before this feature can start. -->
<!-- Orchestrator enforces this. Use "none" if no dependencies. -->
- none

## Files to Touch
<!-- Planner's best-guess list. Implementer updates ## Files Changed with actuals. -->
-

## Codebase Context
<!-- Written by planner after Phase 1 codebase scan. Read by implementer before Pass 2. -->
<!-- If this section is populated, implementer skips Pass 2 (anchor file re-read). -->

### Top Relevant Files
<!-- Format: path — what it contains — relevance to this feature -->

### Key Patterns Observed
<!-- 3 patterns: naming conventions, architecture patterns, dependencies -->

### Interfaces to Respect
<!-- Contracts / interfaces / types the feature must not break -->

## Architectural Decisions Applied
<!-- ADR IDs from .stangent/decisions.md relevant to this feature. -->
<!-- Format: "ADR-NNN — Title" (applied normally) -->
<!--         "ADR-NNN — OVERRIDDEN — Reason: ..." (developer-approved override) -->
<!-- Implementer must honour applied entries. OVERRIDDEN entries: follow the spec instead. -->
-

## New Environment Variables
<!-- Variables this feature introduces. Added to .env.example by implementer. -->
- none

## Risks & Mitigations
<!-- Written by planner (Phase 1e + Phase 4a). Read by implementer before touching any file. -->
<!-- Format: **Risk:** {description}  **Mitigation:** {approach} -->
<!-- or:      **Risk:** {description}  **Approach:** {developer-chosen option} -->
<!-- Write "none identified." if risk_list is empty. -->

## Planner Confidence
<!-- Written by planner in Phase 4.6. Read by orchestrator before AWAITING_CONFIRMATION. -->
<!-- validate_handoff.py checks score against config.confidence_thresholds.planner -->
score:
flags:
  - context_budget_hit:
  - unanswered_questions:
  - adr_conflicts_overridden:
  - files_not_found:
  - symbol_index_misses:


<!-- ═══════════════════════════════════════════════════════════════════════════
     IMPLEMENTER OWNED
     Written by: implementer agent
     Read by: reviewer, srs_agent
     ═══════════════════════════════════════════════════════════════════════ -->

## Pre-Implementation Scan
<!-- Existing code found that is relevant. Prevents duplication. -->
<!-- Format: file:line — what was found — reuse / adapt / ignore -->


## Implementation Log
<!-- Narrative of what was done. Key decisions made. Why alternatives were rejected. -->


## Files Changed
<!-- Actual files created / modified / deleted. Replaces planner's estimate. -->
<!-- Format: [C] created | [M] modified | [D] deleted -->


## Future Considerations
<!-- Ideas that are OUT OF BOUNDS for this feature. Captured here, not implemented. -->
<!-- These may become future FEAT entries. -->

## Implementer Confidence
<!-- Written by implementer at end of Phase 4. Read by orchestrator before REVIEWING. -->
<!-- validate_handoff.py checks score against config.confidence_thresholds.implementer -->
score:
flags:
  - context_budget_hit:
  - ask_developer_used:
  - out_of_bounds_conflicts:
  - files_outside_touch_list:
  - test_coverage_dropped:


<!-- ═══════════════════════════════════════════════════════════════════════════
     SUB-AGENT OWNED
     Each section written exclusively by its named sub-agent.
     ═══════════════════════════════════════════════════════════════════════ -->

## Linter Report
**Status:** PENDING
**Agent version:**
**Config used:** _(project config | stangent default)_
**Command run:**
**Exit code:**
**Findings:**
<!-- PASS — no issues -->
<!-- or list: file:line — rule — description -->


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
<!-- none | or list -->


## Query Analysis Report
**Status:** PENDING
**Agent version:**
**Skipped:** _(yes — no DB layer touched | no)_
**Danger findings (FAIL):**
<!-- none | or list: file:line — pattern — description -->
**Warning findings (WARN):**
<!-- none | or list -->


<!-- ═══════════════════════════════════════════════════════════════════════════
     REVIEWER OWNED
     Written by: reviewer agent
     Read by: orchestrator (for retry/pass decision), srs_agent
     ═══════════════════════════════════════════════════════════════════════ -->

## Scope Verdict
**Status:** PENDING
**Agent version:**
**In bounds:** _(yes | no)_
**Scope creep found:**
<!-- none | or list: file:line — what was added — why it is out of bounds -->


## Review Checklist
<!-- Populated from language profile. Reviewer checks each item. -->
<!-- Format: [x] passed | [ ] failed: reason -->


## Security Report
**Status:** PENDING
**Agent version:**
**Secrets scan:** PENDING
**SAST scan:** PENDING
**Dependency audit:** PENDING
**Hardcoded config scan:** PENDING
**Findings:**
<!-- CRITICAL: must fix before merge -->
<!-- MAJOR: must fix before merge -->
<!-- MINOR: log and continue -->


## Review Verdict
**Status:** PENDING
**Agent version:**

**CRITICAL issues:** none
<!-- List with file:line references if any -->

**MAJOR issues:** none
<!-- List with file:line references if any -->

**MINOR issues:** none
<!-- Log only — does not block -->

**Overall:** PENDING
<!-- PASS | FAIL -->
<!-- If FAIL: provide exact, actionable instructions for the implementer retry. -->
<!-- Be specific: "Fix line 42 in auth_service.dart — raw SQL string must use parameters" -->
<!-- NOT: "improve security" -->

## Reviewer Confidence
<!-- Written by reviewer at end of Phase 7. Read by orchestrator before SRS_UPDATE. -->
<!-- validate_handoff.py checks score against config.confidence_thresholds.reviewer -->
score:
flags:
  - ambiguous_findings:
  - ask_developer_used:
  - cross_stack_drift_found:
  - files_changed_unreadable:


<!-- ═══════════════════════════════════════════════════════════════════════════
     SRS AGENT OWNED
     Written by: srs_agent
     ═══════════════════════════════════════════════════════════════════════ -->

## SRS Reference
**Agent version:**
**SRS section:** _(e.g. 3.2)_
**API contracts documented:** _(yes | no | n/a)_
**Env vars documented:** _(yes | no | n/a)_
**SRS commit:**


<!-- ═══════════════════════════════════════════════════════════════════════════
     PIPELINE OWNED
     Written exclusively by the orchestrator.
     Never edited by any other agent.
     ═══════════════════════════════════════════════════════════════════════ -->

## Pipeline History
| Timestamp | Event | Agent | Detail |
|-----------|-------|-------|--------|
|           | CREATED | orchestrator | |
