# How to Add a New Language Profile

This guide walks you through adding a new language to Stangent.
Target time: ~30 minutes for a well-supported language.

After following this guide, any project using that language can run
`python init.py --profile <name>` and get the full Stangent pipeline.

---

## Step 1 — Copy the base template

```bash
cp profiles/_base.md profiles/<language>.md
```

Replace `<language>` with the lowercase name: `java`, `node`, `csharp`, `go`, etc.

---

## Step 2 — Fill in Identity

```
name:             <language>
version:          1.0.0
file_extensions:  [".ext", ".ext2"]   # all source file extensions
src_root:         src/                # typical source root
```

---

## Step 3 — Fill in Commands

Every command must:
- Exit 0 on success, non-zero on failure
- Write JSON output to `.stangent/<report_name>.json`

Find the equivalents for your language:

| Field | Purpose | Find equivalent for |
|-------|---------|-------------------|
| `lint` | Static analysis | eslint, golangci-lint, checkstyle, rubocop... |
| `lint_fix` | Auto-fix lint | --fix flag or equivalent |
| `format` | Code formatter | prettier, gofmt, dotnet-format... |
| `test` | Run tests with JSON output | jest --json, go test -json... |
| `test_coverage` | Tests + coverage JSON | jest --coverage, go test -coverprofile... |
| `security_sast` | SAST security scan | semgrep, gosec, spotbugs... |
| `dep_audit` | Dependency CVE check | npm audit --json, govulncheck... |
| `secrets_scan` | Secrets detection | detect-secrets (works for any language) |

**Tip:** `detect-secrets` works universally. Reuse it as-is for `secrets_scan`.

---

## Step 4 — Fill in Config Detection

List the files whose presence means this profile applies:

```
lint_config_files:
  - .eslintrc.json
  - .eslintrc.js
  - eslint.config.js

test_config_files:
  - jest.config.js
  - package.json        # check for "jest" key
```

---

## Step 5 — Fill in Anchor Files

These are read in Pass 2 (codebase reading). Choose files that reveal:
- Project structure (entry points, main files)
- Key conventions (config files, main module definitions)
- Dependency declarations (package.json, go.mod, pom.xml...)

```
anchor_files:
  - package.json
  - src/index.js
  - src/app.js
  - src/routes/
  - src/models/
```

---

## Step 6 — Fill in Exclude Dirs

Anything that should never be scanned:

```
exclude_dirs:
  - node_modules
  - .git
  - dist
  - build
  - .stangent
  - coverage
```

---

## Step 7 — Write the Review Checklist

This is the most important section. The reviewer runs through every item.
Write at least 12 items, most-critical first.

Good checklist items are:
- Specific and checkable (not "code quality is good")
- Binary (pass or fail, not subjective)
- Relevant to common bugs in this language

Example structure:
```
review_checklist:
  1.  All acceptance criteria implemented and each has at least one test
  2.  [language-specific security check]
  3.  [language-specific injection/safety check]
  4.  [common footgun in this language]
  5.  No hardcoded credentials, tokens, secrets, or API keys
  6.  No hardcoded environment-specific URLs or magic numbers
  7.  No unused imports
  8.  No commented-out code blocks
  9.  No dead code or unreachable branches
  10. Error states handled appropriately
  11. New env vars added to .env.example with inline comment
  12. Files changed match spec (or deviation is explained)
  13. No code outside feature scope (check ## Out of Bounds)
```

---

## Step 8 — Write Query Patterns

If your language commonly interacts with databases, define patterns.
If not (e.g. a pure UI framework): set both lists to `[]`.

For common ORMs and query builders, find the raw query escape hatches
and flag those. Find the string interpolation patterns that indicate injection risk.

Err on the side of WARN over FAIL for patterns that could be false positives.
The query_analyzer sub-agent applies judgment — patterns are a starting point.

---

## Step 9 — Fill in Conventions

```
conventions:
  test_file_pattern:  *.test.js        # glob for test files
  test_dir:           __tests__/       # or src/ if colocated
  commit_prefix:      feat | fix | test | refactor | docs | chore
```

---

## Step 10 — Add detection to init.py

Open `init.py`. Find the `PROFILES` dict. Add:

```python
"<language>": {
    "detect_files": ["package.json"],   # files that trigger this profile
    "required_tools": ["node", "npm", "detect-secrets"],
    "optional_tools": [],
    "src_root": "src/",
},
```

The `detect_files` list is checked in order — first match wins.
Put the most specific file first (e.g. `go.mod` before `*.go`).

---

## Step 11 — Write eval test cases

Evals are organised by **agent name**, not by language. A new language profile
is tested by verifying that the planner (and other agents) handle it correctly
when given context from that language's project.

Create: `evals/planner/case_<language>_01_input.md`

Describe a simple feature request for a project using this language.
Include a brief project context: which files exist, what frameworks are used,
what the src_root looks like.

Create: `evals/planner/case_<language>_01_expect.md`

List phrases the planner output must contain, and phrases it must not.

Create: `evals/planner/case_<language>_01_assert.py`

Write at least 4 assertions (see `evals/planner/case_01_assert.py` for the pattern):
- Question count ≤ 5
- No questions about things answerable from context
- Spec has falsifiable ACs
- Out of Bounds references specific file paths, not vague areas

---

## Step 12 — Run evals

```bash
python evals/eval_runner.py --agent planner
```

All cases should pass before the profile is considered ready.

---

## Step 13 — Document tool installation

At the bottom of your profile file, add a `## Tool Installation Check` section
listing required and optional tools with install commands.
This is what `init.py --verify` checks.

---

## Checklist

- [ ] `profiles/<language>.md` created with all required fields
- [ ] All commands tested manually at least once
- [ ] Review checklist has ≥ 12 items, most-critical first
- [ ] Query patterns defined (or explicitly set to `[]`)
- [ ] Detection added to `init.py` PROFILES dict
- [ ] Eval cases added under `evals/planner/` (prefixed with language name)
- [ ] All evals pass: `python evals/eval_runner.py`
- [ ] Tool installation section documented
