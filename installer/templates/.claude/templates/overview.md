---
run_id: <run-id>
status: pending     # pending | blocked  (blocked = planner gave up after 4 AskUserQuestion rounds)
created: <UTC date>
---

# <one-line goal>

## Goal
<the user's goal, restated in your own words to confirm understanding>

## Requirements
- [ ] explicit
- [ ] inferred (note the inference)

## Constraints
- <constraint>

## Edge cases
- <edge case>

## Assumptions

<!-- one line per assumption the planner made on the user's behalf -->
- ASSUMPTION: <statement>. Source: planner. Override by re-running /agentic-update-plan.

## Resolved Questions

<!-- Q→A from each AskUserQuestion round, in order -->
- Q: <question> | A: <answer>

## Open Questions

<!-- only present if status is blocked — the gaps that couldn't be closed in 4 rounds -->
- <unresolved blocking gap>

## ADRs in scope

<!-- union of every task's `adrs:` (accepted ADRs that bind some task in this run) -->
- ADR-001 — All timestamps are UTC

## Amendments

<!-- append-only log; one block per /agentic-update-plan invocation -->

## Task index

| ID | Role | Skills | ADRs | Status | Intent |
|---|---|---|---|---|---|
| t1 | implementer | supabase | ADR-001, ADR-003 | pending | <intent> |
