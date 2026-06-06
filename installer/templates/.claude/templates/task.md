---
id: <t-id>                       # t1, t2, ... unique within the run
run_id: <run-id>                 # e.g. FEAT-007
role: implementer                # implementer | reviewer | tester | sketcher
intent: "<one-line statement of what this task achieves>"
acceptance: "<single-sentence testable criteria>"
edge_cases: ["<e1>", "<e2>"]
skills_to_load: [<skill_names>]  # SKILL.md files injected verbatim + retrieval scope; "project" is a valid pseudo-skill
k: null                          # retrieve() chunk count; null = default 6; set 10 when "project" + multiple skill patterns
adrs: []                         # accepted ADR ids relevant to THIS task only
depends_on: []                   # justified edges only (no over-serialization)
status: pending                  # pending | running | done | blocked
blocker: null                    # set when status=blocked, naming the failing bullet
definition_of_done: |
  - acceptance criteria met
  - no known unresolved blockers
  - code compiles / runs
  - reviewer/tester has no blocking failures (if applicable to this task)
---

## Goal
<one paragraph problem statement — what this task is actually about>

## Requirements
- [ ] explicit requirement 1
- [ ] inferred requirement (note the inference)

## Constraints
- <constraint>

## Edge cases
- <edge case>

## Sketch

<!-- filled by sketcher at runtime; implementer uses this as visual reference -->

## Design

<!-- filled by implementer at runtime -->
- Files to add/change:
- API shape / contracts:
- Data model / migrations:

## Test outline

<!-- consumed by tester; happy / boundary / failure / ADR conformance -->
- Happy path:
- Boundary:
- Failure:

## Decisions log

<!-- implementer appends entries; cite ADRs and skills that shaped non-obvious choices -->

## Review

<!-- reviewer appends ONLY here -->

## Test results

<!-- tester appends ONLY here -->
