---
id: {{feature_id}}
title: {{FEATURE_TITLE}}
slug: {{feature-slug}}
status: CREATED
language: {{language}}
branch: stangent/{{feature_id}}-{{feature-slug}}
retry_count: 0
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

## Architectural Decisions Applied
<!-- ADR IDs from .stangent/decisions.md relevant to this feature. -->
<!-- Format: "ADR-NNN — Title" (applied normally) -->
<!--         "ADR-NNN — OVERRIDDEN — Reason: ..." (developer-approved override) -->
<!-- Implementer must honour applied entries. OVERRIDDEN entries: follow the spec instead. -->
-

## New Environment Variables
<!-- Variables this feature introduces. Added to .env.example by implementer. -->
- none


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
