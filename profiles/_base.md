# Base Profile Contract
> Every language profile must define all fields declared in this document.
> A profile that omits a required field will cause init.py validation to fail.

---

## Required Fields

### Identity
```
name:          string    # e.g. "python", "flutter"
version:       semver    # e.g. "1.0.0"
file_extensions: list    # e.g. [".py"] or [".dart"]
src_root:      path      # default source root, e.g. "src/" or "lib/"
                         # overridden by config.json if set
```

### Commands
All commands must exit 0 on success. Output goes to the specified path so
sub-agents can read structured results.

```
commands:
  lint:            string   # linter command, outputs JSON to .stangent/lint_report.json
  lint_fix:        string   # auto-fix command (optional but recommended)
  format:          string   # formatter command
  test:            string   # test runner, outputs to .stangent/test_report.json
  test_coverage:   string   # test + coverage, outputs to .stangent/coverage.json
  security_sast:   string   # SAST scan, outputs to .stangent/sast_report.json
  dep_audit:       string   # dependency CVE audit, outputs to .stangent/dep_audit.json
  secrets_scan:    string   # secrets detection, outputs to .stangent/secrets_report.json
```

### Config Detection
Files the profile looks for to use existing project lint/test config.
If none exist, the profile's defaults apply.

```
lint_config_files:  list   # files indicating existing lint config
test_config_files:  list   # files indicating existing test config
```

### Codebase Reading
```
anchor_files:  list   # Pass 2 reads — always read these if they exist
exclude_dirs:  list   # always skip these in Pass 1 tree scan
```

### Review Checklist
Ordered list of items the reviewer works through. Each item is a statement
that should be true. Reviewer marks [x] true or [ ] false: reason.

```
review_checklist: list   # ordered strings, most critical first
```

### Query Patterns
Regex patterns for the query_analyzer sub-agent.

```
query_patterns:
  danger_patterns: list   # FAIL — unsafe query construction
  warn_patterns:   list   # WARN — review manually
```

### Conventions
```
conventions:
  test_file_pattern: string   # glob pattern for test files
  test_dir:          string   # where tests live
  commit_prefix:     string   # Conventional Commits prefix for this profile
```

---

## Optional Fields

```
i18n_aware:       bool    # adds i18n checks to review_checklist
api_extraction:   bool    # srs_agent extracts API contracts

monorepo:                 # only for multi-language projects
  roots:
    - name:    string
      path:    string
      profile: string
```

---

## Profile Validation Checklist (profile author self-check before shipping)

- [ ] All required fields present
- [ ] All `commands.*` fields are valid shell commands (dry-run check)
- [ ] `anchor_files` paths are relative, no leading `/`
- [ ] `exclude_dirs` has no trailing slashes
- [ ] `review_checklist` has at least 8 items
- [ ] `query_patterns.danger_patterns` has at least 2 entries
- [ ] `conventions.test_file_pattern` is a valid glob
