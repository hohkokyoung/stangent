# Python Profile
> Stangent language profile for Python projects.
> Satisfies all fields required by profiles/_base.md.

---

## Identity

```
name:             python
version:          1.0.0
file_extensions:  [".py"]
src_root:         src/
```

Detection triggers (checked by init.py in order):
- `pyproject.toml` present
- `requirements.txt` present
- `setup.py` present

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
  pytest --tb=short -q --json-report --json-report-file=.stangent/test_report.json

test_coverage:
  pytest --tb=short -q --cov --cov-report=json:.stangent/coverage.json --json-report --json-report-file=.stangent/test_report.json

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
  - pyproject.toml      # [tool.pytest.ini_options] section
  - setup.cfg
  - conftest.py
```

If no lint config is found: generate `ruff.toml` with sensible defaults
and show developer for confirmation before first lint run.

---

## Anchor Files (Pass 2 Codebase Reading)

Read these files if they exist. They establish project structure and conventions.

```
anchor_files:
  - pyproject.toml
  - requirements.txt
  - setup.py
  - setup.cfg
  - main.py
  - app.py
  - run.py
  - src/main.py
  - src/app.py
  - src/models/
  - src/routes/
  - src/services/
  - src/api/
  - src/db/
  - src/auth/
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
```

---

## Review Checklist

The reviewer works through this list in order. Mark [x] if true, [ ] false: reason.

```
review_checklist:
  1.  All acceptance criteria implemented and each has at least one test
  2.  No raw SQL: no f-string/format()/concatenation in query strings
  3.  All query parameters use parameterized statements
  4.  No N+1 query patterns (loop containing a DB call without batching)
  5.  All user inputs validated at system boundary (not deep in business logic)
  6.  No bare except clauses — specific exception types caught
  7.  Async functions: no blocking I/O (requests, open, sleep) in async context
  8.  No hardcoded credentials, API keys, tokens, or secrets
  9.  No hardcoded environment-specific URLs or magic numbers
  10. No unused imports
  11. No commented-out code
  12. No dead code or unreachable branches
  13. Type hints present on all public functions and methods
  14. Error states handled and logged at appropriate level
  15. New environment variables added to .env.example with inline comment
  16. Files changed match ## Files to Touch (or ## Files Changed explains deviation)
  17. No code outside feature scope (check ## Out of Bounds)
```

---

## Query Patterns

### Danger Patterns (FAIL — blocks review)
```
danger_patterns:
  - 'cursor\.execute\(["\'].*(?:SELECT|INSERT|UPDATE|DELETE).*["\']'
    # raw SQL string passed directly — must use parameterized form

  - 'f["\'].*(?:SELECT|INSERT|UPDATE|DELETE|WHERE).*\{.*\}'
    # f-string SQL injection risk

  - '"(?:SELECT|INSERT|UPDATE|DELETE|WHERE).*"\s*\+'
    # string concatenation building a SQL query

  - '\.format\(.*\).*(?:SELECT|INSERT|UPDATE|DELETE)'
    # .format() used to construct SQL

  - 'execute\(\s*["\'].*\+.*["\']'
    # string concatenation in execute()
```

### Warning Patterns (WARN — human review required)
```
warn_patterns:
  - 'for .+:\n.+(?:execute|query|filter)\('
    # loop containing a DB call — potential N+1

  - 'text\(["\'].*\{.*\}'
    # SQLAlchemy text() with format string — review parameterization

  - '\.raw\('
    # Django ORM raw() — review for injection

  - 'cursor\.execute\([^,)]+\)'
    # execute() with single argument — no params tuple — review
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
  dependency_management:
    order:
      - requirements.txt
      - requirements-dev.txt
      - requirements/base.txt
      - requirements/dev.txt
      - pyproject.toml
    rule: >
      Always search for an existing requirements file before creating a new one.
      If found, append the new dependency there. Never create requirements.txt
      if one already exists elsewhere in the project tree.
```

---

## API Extraction

```
api_extraction: true
```

When `api_extraction: true`, the SRS agent looks for:

- **FastAPI**: `@app.get`, `@app.post`, `@router.get`, etc. — extracts path, method,
  Pydantic request/response models
- **Flask**: `@app.route`, `@blueprint.route` — extracts path, methods
- **Django**: `path()` / `re_path()` in `urls.py` — extracts path, view name

---

## i18n

`i18n_aware: false` — Python projects in this framework are API/backend focused.
Enable and add checklist items if a frontend is added.

---

## Default ruff.toml (generated if no lint config found)

```toml
line-length = 100
target-version = "py310"

[lint]
select = [
  "E",   # pycodestyle errors
  "W",   # pycodestyle warnings
  "F",   # pyflakes
  "I",   # isort
  "B",   # flake8-bugbear
  "S",   # flake8-bandit (security)
  "UP",  # pyupgrade
]
ignore = ["S101"]  # allow assert in tests

[lint.per-file-ignores]
"tests/**" = ["S", "B"]
```

---

## Tool Installation Check (run by init.py)

```
required_tools:
  - ruff        # pip install ruff
  - pytest      # pip install pytest
  - bandit      # pip install bandit
  - pip-audit   # pip install pip-audit
  - detect-secrets  # pip install detect-secrets

optional_tools:
  - pytest-cov          # pip install pytest-cov
  - pytest-json-report  # pip install pytest-json-report
```
