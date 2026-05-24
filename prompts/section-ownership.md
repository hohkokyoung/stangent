# Feature File Section Ownership

Each section of a feature file is owned by exactly one agent.
You MUST NOT write outside your assigned sections. You MUST NOT overwrite
another agent's section even if its content looks wrong.

| Section | Owned by |
|---------|----------|
| `## Scope` | planner |
| `## Acceptance Criteria` | planner |
| `## Out of Bounds` | planner |
| `## Depends On` | planner |
| `## Files to Touch` | planner |
| `## Architectural Decisions Applied` | planner |
| `## New Environment Variables` | planner |
| `## Risks & Mitigations` | planner |
| `## Codebase Context` | planner |
| `## Planner Confidence` | planner |
| `## Pre-Implementation Scan` | implementer |
| `## Implementation Log` | implementer |
| `## Files Changed` | implementer |
| `## Future Considerations` | implementer |
| `## Implementer Confidence` | implementer |
| `## Linter Report` | linter |
| `## Test Report` | unit_tester |
| `## Query Analysis Report` | query_analyzer |
| `## Security Report` | security_scanner |
| `## Performance Review` | performance_reviewer (returned text, written by reviewer into ## Review Checklist) |
| `## Quality Review` | quality_reviewer (returned text, written by reviewer into ## Review Checklist) |
| `## Scope Verdict` | reviewer |
| `## Review Checklist` | reviewer |
| `## Review Verdict` | reviewer |
| `## Reviewer Confidence` | reviewer |
| `## SRS Reference` | srs_agent |
| `## Pipeline History` | orchestrator |
| `## Context Checkpoint` | any agent (only on context budget exhaustion) |

**Frontmatter field ownership:**

| Field | Owned by |
|-------|----------|
| `status`, `retry_count`, `branch`, `*_agent_version` | orchestrator |
| `title`, `slug`, `language`, `planner_agent_version`, `updated` | planner |
| `implementer_agent_version` | implementer (set on commit) |
| `reviewer_agent_version` | reviewer (set on verdict) |
| `srs_agent_version` | srs_agent (set on SRS update) |
