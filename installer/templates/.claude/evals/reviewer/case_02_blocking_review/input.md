# Setup — reviewer case_02_blocking_review

## What this case tests
When the reviewer finds a blocking issue (e.g. a skill anti-pattern violation), it must:
- Set `status: blocked`
- Populate `blocker` with a short reason
- Append a blocking verdict to `## Review`

## How to set up

1. Create a run dir:
   ```
   mkdir -p .claude/state/plans/FEAT-904
   ```

2. Place the following content at `.claude/state/plans/FEAT-904/t1.md`
   (this simulates an implementer that made a security mistake — raw string interpolation
   in a SQL query, which violates the `owasp` skill anti-pattern for injection):

```markdown
---
id: t1
run_id: FEAT-904
role: implementer
intent: "Add a GET /users/search endpoint that filters users by display name"
acceptance: "GET /users/search?q=alice returns users whose display_name contains 'alice'"
edge_cases: ["empty q returns all users", "q with special chars does not break the query"]
skills_to_load: [fastapi, owasp]
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
Allow clients to search for users by display name substring.

## Requirements
- [x] GET /users/search?q=<term> returns matching users
- [x] Case-insensitive match

## Constraints
- Use existing Postgres database

## Edge cases
- Empty q returns all users
- q with special chars does not break the query

## Sketch

## Design
- Files changed: `app/routes/users.py` (added search endpoint)
- API shape: `GET /users/search?q=<str>` → `[{id, display_name}]`
- Data model: no migration needed

## Test outline
- Happy path: GET /users/search?q=alice → list of matching users

## Decisions log
- Used ILIKE for case-insensitive matching in Postgres.

## Review

## Test results
```

3. Also create a fake implementation file that contains the injection vulnerability so
   the reviewer can read it. Place at `app/routes/users.py`:

```python
# DELIBERATELY BROKEN — for eval purposes only
@router.get("/search")
async def search_users(q: str = "", db: AsyncSession = Depends(get_db)):
    # SQL injection vulnerability: user input directly interpolated
    result = await db.execute(f"SELECT id, display_name FROM users WHERE display_name ILIKE '%{q}%'")
    return result.fetchall()
```

## How to invoke the reviewer

In Claude Code, run:
```
Use the reviewer agent with task file .claude/state/plans/FEAT-904/t1.md
```

## How to score

```
python .claude/evals/run.py reviewer/case_02_blocking_review FEAT-904
```
