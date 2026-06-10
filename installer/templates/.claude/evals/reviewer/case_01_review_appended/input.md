# Setup — reviewer case_01_review_appended

## What this case tests
After reviewing an implemented task, the reviewer appends findings to `## Review`,
leaves `status` as `done` (does NOT flip it), and does NOT modify any other section.

## How to set up

1. Create a run dir with a fully-implemented task:
   ```
   mkdir -p .claude/state/plans/FEAT-903
   ```

2. Place the following content at `.claude/state/plans/FEAT-903/t1.md`
   (this simulates a completed implementer run):

```markdown
---
id: t1
run_id: FEAT-903
role: implementer
intent: "Add a GET /healthz endpoint that returns {\"status\": \"ok\"} with HTTP 200"
acceptance: "GET /healthz returns HTTP 200 with body {\"status\": \"ok\"}; no auth required"
edge_cases: ["endpoint reachable before DB is ready", "returns JSON not plain text"]
skills_to_load: [fastapi]
k: 6
adrs: []
depends_on: []
status: done
blocker: null
definition_of_done: |
  - acceptance criteria met
  - no known unresolved blockers
  - code compiles / runs
---

## Goal
Add a minimal health-check endpoint to the FastAPI service.

## Requirements
- [x] GET /healthz returns 200 with body {"status": "ok"}
- [x] No authentication required
- [x] Response is JSON

## Constraints
- Must not require a database connection

## Edge cases
- Endpoint reachable before DB is ready
- Returns JSON not plain text

## Sketch

## Design
- Files to add/change: `app/routes/health.py` (new), `app/main.py` (register router)
- API shape: `GET /healthz` → `{"status": "ok"}` with `Content-Type: application/json`
- Data model / migrations: none

## Test outline
- Happy path: GET /healthz → 200, {"status": "ok"}

## Decisions log
- Used a dedicated `health.py` router rather than inlining in `main.py` to keep the
  entrypoint clean. No DB ping — health check should succeed even if DB is down.

## Review

## Test results
```

## How to invoke the reviewer

In Claude Code, run:
```
Use the reviewer agent with task file .claude/state/plans/FEAT-903/t1.md
```

## How to score

```
python .claude/evals/run.py reviewer/case_01_review_appended FEAT-903
```
