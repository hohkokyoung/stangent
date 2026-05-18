# FastAPI Profile
> Stangent language profile for FastAPI backend projects.
> Satisfies all fields required by profiles/_base.md.
> Covers async route handlers, Pydantic v2, SQLAlchemy 2.x async, and dependency injection.

---

## Identity

```
name:             fastapi
version:          1.0.0
file_extensions:  [".py"]
src_root:         src/
```

Detection triggers (checked by init.py in order):
- `fastapi` found in `pyproject.toml` or `requirements.txt` (content check)
- `from fastapi import` or `import fastapi` in any `.py` file under `src/`

Falls back to `python` profile if no FastAPI dependency is detected.
Use `--profile fastapi` to select explicitly.

---

## Commands

```
lint:
  ruff check . --output-format=json --exit-non-zero-on-fix > .stangent/lint_report.json 2>&1

lint_fix:
  ruff check . --fix && ruff format .

format:
  ruff format .

test:
  pytest --tb=short -q --json-report --json-report-file=.stangent/test_report.json --asyncio-mode=auto

test_coverage:
  pytest --tb=short -q --cov --cov-report=json:.stangent/coverage.json --json-report --json-report-file=.stangent/test_report.json --asyncio-mode=auto

security_sast:
  bandit -r . -f json -o .stangent/sast_report.json --exclude .venv,venv,tests,.stangent

dep_audit:
  pip audit --format=json 2>/dev/null > .stangent/dep_audit.json || pip audit --format=json > .stangent/dep_audit.json

secrets_scan:
  detect-secrets scan --all-files --exclude-files "\.stangent/.*" > .stangent/secrets_report.json
```

---

## Config Detection

```
lint_config_files:
  - ruff.toml
  - .ruff.toml
  - pyproject.toml      # [tool.ruff] section

test_config_files:
  - pytest.ini
  - pyproject.toml      # [tool.pytest.ini_options] section — check for asyncio_mode
  - setup.cfg
  - conftest.py
```

If no `pytest.ini` / `pyproject.toml` pytest config is found: generate a minimal
`pyproject.toml` section with `asyncio_mode = "auto"` so async tests work without
`@pytest.mark.asyncio` on every function.

---

## Anchor Files (Pass 2 Codebase Reading)

Read these files if they exist. They establish project structure and conventions.

```
anchor_files:
  - pyproject.toml
  - requirements.txt
  - main.py
  - app.py
  - src/main.py
  - src/app.py
  - src/core/config.py       # pydantic-settings BaseSettings
  - src/core/security.py     # auth helpers
  - src/db/session.py        # async DB session factory
  - src/db/base.py           # SQLAlchemy declarative base
  - src/dependencies.py      # shared Depends() functions
  - src/deps.py
  - src/routes/
  - src/routers/
  - src/api/
  - src/models/
  - src/schemas/
  - src/services/
  - tests/conftest.py
```

---

## Exclude Dirs (Pass 1 Tree Scan)

```
exclude_dirs:
  - __pycache__
  - .venv
  - venv
  - env
  - .env
  - .git
  - build
  - dist
  - .stangent
  - .pytest_cache
  - .mypy_cache
  - .ruff_cache
  - htmlcov
  - "*.egg-info"
  - node_modules
  - alembic/versions       # migration files — rarely relevant to feature scope
```

---

## Review Checklist

The reviewer works through this list in order. Mark [x] if true, [ ] false: reason.

```
review_checklist:
  1.  All acceptance criteria implemented and each has at least one test
  2.  All route handler functions are `async def` — sync def blocks the event loop
  3.  No blocking I/O inside async route handlers:
        no `requests.*`, no `time.sleep()`, no sync file I/O,
        no synchronous SQLAlchemy sessions (Session, not AsyncSession)
  4.  All request bodies declared as Pydantic models — no raw `dict` or `Any` params
  5.  All endpoints declare `response_model=` — undeclared responses bypass validation
        and can leak internal fields (e.g. hashed passwords in ORM models)
  6.  Database sessions injected via `Depends(get_db)` — not module-level singletons
        or manually instantiated inside route handlers
  7.  `HTTPException` used with correct status codes — bare `raise Exception` returns 500
  8.  `CORS(allow_origins=["*"])` — flag as MAJOR if found without a documented reason
        in the spec; acceptable in dev, must be restricted for production endpoints
  9.  Authentication/authorisation applied at router level via dependency, not
        repeated per endpoint (copy-paste auth is a scope creep and miss risk)
  10. No N+1 queries in list endpoints — use `.options(selectinload(...))`,
        `.options(joinedload(...))`, or batch queries; flag any unloaded relationship
        access inside a loop
  11. No raw SQL string construction — no f-strings, no `.format()`, no `+` concatenation
        in `text()` or `execute()` calls
  12. Pagination applied on list endpoints that could return unbounded rows
        (missing `limit=` / `offset=` or `Params` from fastapi-pagination)
  13. Query parameters validated with explicit type hints and `Query()` constraints
        where bounds matter (e.g. `limit: int = Query(default=20, le=100)`)
  14. All config values read from `pydantic-settings` BaseSettings, not scattered
        `os.environ.get()` calls inside route handlers or service functions
  15. No hardcoded credentials, API keys, tokens, or secrets
  16. No hardcoded environment-specific URLs or connection strings
  17. Error responses use a consistent schema — not mixed plain strings and dicts
  18. New environment variables added to `.env.example` with inline comment
  19. Background tasks used for fire-and-forget work — not blocking the response
  20. Files changed match ## Files to Touch (or ## Files Changed explains deviation)
  21. No code outside feature scope (check ## Out of Bounds)
```

---

## Query Patterns

### Danger Patterns (FAIL — blocks review)
```
danger_patterns:
  - 'text\(f["\'].*\{.*\}'
    # SQLAlchemy text() with f-string — SQL injection risk

  - 'text\(["\'].*\.format\('
    # SQLAlchemy text() with .format() — SQL injection risk

  - 'text\(["\'].*["\'].*\+'
    # SQLAlchemy text() with string concatenation

  - 'execute\(f["\'].*\{.*\}'
    # execute() with f-string directly

  - 'execute\(["\'].*["\'].*\+'
    # execute() with string concatenation

  - 'cursor\.execute\([^,)]+\)'
    # raw cursor execute() with single argument — no params tuple

  - 'engine\.execute\('
    # deprecated SQLAlchemy 1.x pattern — use async session
```

### Warning Patterns (WARN — human review required)
```
warn_patterns:
  - 'async for .+:\n.+await .+\.execute'
    # async loop containing a DB call — potential N+1

  - 'for .+:\n.+await .+(session|db)\.'
    # loop with awaited session call — potential N+1

  - 'text\(["\'][^"\']*\{[^"\']*\}'
    # SQLAlchemy text() — review for parameterization even if not f-string

  - '\.all\(\)'
    # unbounded .all() — verify pagination is applied upstream

  - 'session\.execute\(select\(.*\)\)'
    # SELECT without .options() — check for lazy-loaded relationships
```

---

## Conventions

```
conventions:
  test_file_pattern:  test_*.py
  test_dir:           tests/
  commit_prefix:      feat | fix | test | refactor | docs | chore
  import_style:       absolute
  docstring_style:    google
  async_test_marker:  pytest-asyncio (asyncio_mode = auto)
```

---

## API Extraction

```
api_extraction: true
```

When `api_extraction: true`, the SRS agent extracts:

- **FastAPI routers**: `@router.get`, `@router.post`, `@router.put`, `@router.patch`,
  `@router.delete` — extracts path, method, tags
- **Request models**: Pydantic model used as `Body()` parameter or type-annotated body
- **Response models**: `response_model=` value on the decorator
- **Path/query params**: function signature types

---

## Performance Checklist (reviewer Phase 4)

```
performance_checklist:
  - Blocking I/O in async context (requests, time.sleep, sync session)
  - Missing pagination on list endpoints
  - N+1 queries (loop + DB call without batching)
  - Missing DB indexes on filtered/joined columns (caught by query_analyzer + DBHub)
  - Large response payloads with no field selection (missing response_model exclusions)
```

---

## Tool Installation Check (run by init.py)

```
required_tools:
  - ruff              # pip install ruff
  - pytest            # pip install pytest
  - pytest-asyncio    # pip install pytest-asyncio
  - httpx             # pip install httpx  (async test client for FastAPI)
  - bandit            # pip install bandit
  - pip-audit         # pip install pip-audit
  - detect-secrets    # pip install detect-secrets

optional_tools:
  - pytest-cov              # pip install pytest-cov
  - pytest-json-report      # pip install pytest-json-report
  - asgi-lifespan           # pip install asgi-lifespan (async lifespan in tests)
```

---

## Default ruff.toml (generated if no lint config found)

```toml
line-length = 100
target-version = "py311"

[lint]
select = [
  "E",   # pycodestyle errors
  "W",   # pycodestyle warnings
  "F",   # pyflakes
  "I",   # isort
  "B",   # flake8-bugbear
  "S",   # flake8-bandit (security)
  "UP",  # pyupgrade
  "ASYNC", # flake8-async — catches blocking calls in async context
]
ignore = ["S101"]  # allow assert in tests

[lint.per-file-ignores]
"tests/**" = ["S", "B"]
```

---

## Supabase Checklist (only when `integrations.supabase.enabled = true`)

Read `.stangent/prompts/supabase.md` for full security rules and patterns.

```
supabase_review_checklist:
  1.  SUPABASE_SERVICE_ROLE_KEY not returned in any API response body or header
        — CRITICAL if found
  2.  SUPABASE_SERVICE_ROLE_KEY not logged (print, logger.info, logger.debug,
        f-string in any log call) — CRITICAL if found
  3.  Every protected route uses Depends(verify_supabase_jwt) or equivalent
        — MAJOR if a route is accessible without a valid Supabase JWT
  4.  JWT verification uses SUPABASE_JWT_SECRET from settings, not hardcoded
        — CRITICAL if hardcoded
  5.  supabase-py client initialized with service_role key only in server-side
        code — never use anon key for operations that require elevated privilege
        and never use service_role on code paths that return data to the client
  6.  Any new migration file enables RLS on new tables
        — MAJOR if RLS not enabled on a table containing user data
  7.  Any new table with user data has at least one RLS policy
        — MAJOR if table has RLS enabled but zero policies (blocks all access)
  8.  Supabase direct PG connection string in DATABASE_URL env var, not hardcoded
        — MAJOR if hardcoded
  9.  New environment variables (SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY,
        SUPABASE_JWT_SECRET, DATABASE_URL) added to .env.example
        — MINOR if missing from .env.example
  10. No anon key used for operations that should require service_role
        (e.g. reading another user's private data bypasses RLS intent)
        — MAJOR if found
```

---

## Default pytest config (appended to pyproject.toml if no asyncio config found)

```toml
[tool.pytest.ini_options]
asyncio_mode = "auto"
testpaths = ["tests"]
```
