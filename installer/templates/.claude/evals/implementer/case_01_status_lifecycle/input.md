# Setup — implementer case_01_status_lifecycle

## What this case tests
The implementer correctly transitions a task from `pending` → `running` → `done`,
fills `## Design` with the files it changed, and fills `## Decisions log` with
at least one entry.

## How to set up

1. Create a run dir and seed a task file:
   ```
   mkdir -p .claude/state/plans/FEAT-901
   ```

2. Place the following content at `.claude/state/plans/FEAT-901/t1.md`:

```markdown
---
id: t1
run_id: FEAT-901
role: implementer
intent: "Add a GET /healthz endpoint that returns {\"status\": \"ok\"} with HTTP 200"
acceptance: "GET /healthz returns HTTP 200 with body {\"status\": \"ok\"}; no auth required"
edge_cases: ["endpoint reachable before DB is ready", "returns JSON not plain text"]
skills_to_load: [fastapi]
k: 6
adrs: []
depends_on: []
status: pending
blocker: null
definition_of_done: |
  - acceptance criteria met
  - no known unresolved blockers
  - code compiles / runs
---

## Goal
Add a minimal health-check endpoint to the FastAPI service so that load balancers and
uptime monitors can verify the service is alive without touching the database.

## Requirements
- [ ] GET /healthz returns 200 with body {"status": "ok"}
- [ ] No authentication required
- [ ] Response is JSON

## Constraints
- Must not require a database connection

## Edge cases
- Endpoint reachable before DB is ready
- Returns JSON not plain text

## Sketch

## Design

<!-- filled by implementer at runtime -->
- Files to add/change:
- API shape / contracts:
- Data model / migrations:

## Test outline
- Happy path: GET /healthz → 200, {"status": "ok"}
- Failure: N/A (endpoint has no failure mode)

## Decisions log

## Review

## Test results
```

## How to invoke the implementer

In Claude Code, run:
```
Use the implementer agent with task file .claude/state/plans/FEAT-901/t1.md
```

## How to score

```
python .claude/evals/run.py implementer/case_01_status_lifecycle FEAT-901
```
