---
description: Bootstrap tests for an existing project (brownfield). Ask the developer which flows to cover, then generate and run baseline tests using the detected test framework.
argument-hint: "init"
---

# /agentic-test

## Subcommands

| Command | What it does |
|---|---|
| `/agentic-test init` | Brownfield bootstrap — ask developer which flows to cover, generate and run baseline tests |

---

## /agentic-test init

Use this on a project that already has code but no tests. It asks the developer which flows matter most, then generates baseline test artifacts using the configured test skill.

### Procedure

**Step 1 — Verify test framework is configured**

Read `.claude/state/project.yml` for `test_framework`. If missing or `unknown`:
```
[agentic-test] No test framework detected. Run /agentic-index first.
```

Read `.claude/.agentic.yml` and check that a skill matching `test_framework` is in `enabled_skills`. If not:
```
[agentic-test] test_framework=<value> but no matching skill is in enabled_skills.
Add the skill to .agentic.yml and re-run /agentic-index.
```

**Step 2 — Ask the developer**

Use `AskUserQuestion` (1 round, up to 3 questions):

1. What are the most important user-facing flows or entry points in this project? (list them — e.g. "login, dashboard, checkout" or "GET /users, POST /orders")
2. Is the app / server / environment already running? If not, what command starts it?
3. Is there an existing test directory or naming convention to follow?

Do not attempt to infer flows from file paths — the developer knows their project. Only scan files if the developer explicitly references a directory or file in their answers.

**Step 3 — Generate baseline tests**

For each flow the developer listed, invoke a **tester** subagent with:
- The flow description as the task intent
- The `test_framework` from `project.yml`
- The matching test skill in `skills_to_load`
- Context: "This is a brownfield baseline — explore the running app/service and generate one test that covers the happy path. Do not assume file structure."

Wait for each tester to complete before starting the next.

**Step 4 — Report**

Print a summary:
```
Baseline test summary
---------------------
✓ <flow name>  — generated <artifact path>, all pass
✗ <flow name>  — generated <artifact path>, FAILED: <reason>
  → failing baseline kept for reference; fix or re-run after the issue is addressed

Generated artifacts:
  <path1>
  <path2>
```

### What this does NOT do

- Does not create plan or task files.
- Does not fix failing tests — it captures the current state as a baseline.
- Does not wire up CI — do that manually once you have passing baselines.

### After init

Every new `/agentic-plan` will automatically include tester tasks using the same framework. The generated baseline files serve as regression anchors.
