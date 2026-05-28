---
description: Bootstrap tests for an existing project (brownfield). Scans existing screens/flows, asks which ones to cover, generates baseline test artifacts.
argument-hint: "init"
---

# /agentic-test

## Subcommands

| Command | What it does |
|---|---|
| `/agentic-test init` | Brownfield bootstrap — scan existing flows, ask user, generate baseline tests |

---

## /agentic-test init

Use this on a project that already has code but no tests. It scans what exists, asks which flows to cover, and generates baseline test artifacts using the detected test framework.

### Procedure

**Step 1 — Read config**

Read `.claude/state/project.yml` for `test_framework`. If missing or `unknown`, stop with:
```
Run /agentic-index first so the test framework can be detected.
```

Read `.claude/.agentic.yml` to confirm `playwright` or `maestro` is in `enabled_skills`.

**Step 2 — Scan existing flows**

For **playwright** (web):
- Glob for route definitions: `pages/`, `app/`, `src/routes/`, `src/views/`, router config files.
- List up to 10 distinct user-facing routes/screens found.

For **maestro** (mobile):
- Glob for screen files: `lib/screens/`, `lib/pages/`, `src/screens/`, `*.screen.dart`, `*Screen.kt`, `*ViewController.swift`.
- List up to 10 distinct screens found.

**Step 3 — Ask the user**

Use `AskUserQuestion` (1 round, up to 3 questions):

1. Which flows/screens should have baseline tests? (show the list found in step 2)
2. Is the dev server / emulator already running? If not, what command starts it?
3. What is the app entry point? (URL for web, bundle ID for mobile)

**Step 4 — Generate baseline tests**

For each selected flow/screen:

- **playwright**: Use Playwright MCP tools to navigate to the screen, snapshot, screenshot, then generate a `.spec.ts` covering: page loads without error, key elements visible, primary action reachable. Place in `tests/e2e/<screen_name>.spec.ts` (or existing test dir).

- **maestro**: Use Maestro MCP tools to launch the app, navigate to the screen, inspect hierarchy, screenshot, then generate a flow YAML covering: screen loads, key elements visible, primary action reachable. Place in `.maestro/<screen_name>/baseline.yaml`.

**Step 5 — Run and report**

Run the generated tests:
- Playwright: `npx playwright test --reporter=list`
- Maestro: `mcp__maestro__run_flow_files` for each generated file

Report results:
```
Baseline test summary
---------------------
✓ login_screen      — 1 flow, all pass
✓ dashboard         — 1 flow, all pass
✗ settings_screen   — 1 flow, FAILED: element "Save" not found
  → blocker noted, fix manually or re-run after implementer addresses it

Generated artifacts:
  tests/e2e/login_screen.spec.ts
  tests/e2e/dashboard.spec.ts
  tests/e2e/settings_screen.spec.ts   ← failing baseline, kept for reference
```

### What this does NOT do

- Does not create a plan or task files — this is a one-time bootstrap, not a feature build.
- Does not fix failing tests — it captures the current state as a baseline. Failures indicate existing gaps.
- Does not run in CI — wire that up manually once you have passing baselines.

### After init

Every new `/agentic-plan` will automatically include test tasks using the same framework. The generated baseline files serve as regression anchors.
