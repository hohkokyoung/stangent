# Setup — implementer case_02_blocked_missing_adr

## What this case tests
When a task's `adrs` list references an ADR file that does not exist, the implementer
must flip to `blocked` with `blocker: "missing_adr: <id>"` and stop — it must NOT
write any code.

## How to set up

1. Create a run dir and seed a task file:
   ```
   mkdir -p .claude/state/plans/FEAT-902
   ```

2. Place the following content at `.claude/state/plans/FEAT-902/t1.md`:

```markdown
---
id: t1
run_id: FEAT-902
role: implementer
intent: "Add rate limiting to the POST /login endpoint"
acceptance: "POST /login returns 429 after 5 failed attempts within 60s"
edge_cases: ["counter resets after 60s window", "successful login resets counter"]
skills_to_load: [fastapi]
k: 6
adrs: [ADR-999]
depends_on: []
status: pending
blocker: null
definition_of_done: |
  - acceptance criteria met
  - no known unresolved blockers
  - code compiles / runs
---

## Goal
Protect the login endpoint against brute-force attacks by rate-limiting by IP.

## Requirements
- [ ] Return 429 after 5 failed attempts within a 60-second window
- [ ] Counter resets after 60s

## Constraints
- Must follow ADR-999 (rate limiting strategy)

## Edge cases
- Counter resets after 60s window
- Successful login resets counter

## Sketch

## Design

## Test outline
- Happy path: 4 failures → still 200/401; 5th failure → 429
- Boundary: exactly 5 attempts

## Decisions log

## Review

## Test results
```

Note: **Do NOT create `.claude/adrs/ADR-999-*.md`**. The ADR must be missing so
the implementer triggers the blocked path.

## How to invoke the implementer

In Claude Code, run:
```
Use the implementer agent with task file .claude/state/plans/FEAT-902/t1.md
```

## How to score

```
python .claude/evals/run.py implementer/case_02_blocked_missing_adr FEAT-902
```
