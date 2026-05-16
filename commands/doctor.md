Validate that Stangent is correctly wired up in this project.

Usage: /doctor
No arguments.

Checks config, directories, agents, gateway hook, registry, and active
gateway state. Outputs a pass/warn/fail report for each check.

---

## Step 1 — Config file

Check `.stangent/config.json`:
  - Exists?
  - Valid JSON?
  - Has `_stangent_version` field?
  - Has required top-level sections: provider, profiles, paths, pipeline, models?

If config is missing or invalid:
  Output:
    "[FAIL] .stangent/config.json — not found or invalid JSON
     Fix: run python <stangent>/init.py to create it."
  Stop — remaining checks depend on config.

If config is valid:
  Output: "[PASS] .stangent/config.json — v{_stangent_version} ({provider.name}, {profiles[]})"

---

## Step 2 — Required directories

Read paths from config. For each of the following, check it exists:

  - config.paths.feature_dir
  - config.paths.archive_dir
  - config.paths.log_dir
  - config.paths.profiles_dir
  - config.paths.templates_dir
  - config.paths.prompts_dir
  - config.paths.contracts_dir
  - ".stangent/gateway/"

For each:
  - Exists → [PASS] {path}
  - Missing → [WARN] {path} — missing. Fix: re-run init.py

---

## Step 3 — Profile files

For each profile in config.profiles:
  Check `.stangent/profiles/{profile}.md` exists.
  - Exists → [PASS] profiles/{profile}.md
  - Missing → [FAIL] profiles/{profile}.md — not found. Fix: re-run init.py

---

## Step 4 — Prompt fragments

Check that each of the following exists:

  - `.stangent/prompts/ask-developer.md`
  - `.stangent/prompts/context-budget.md`
  - `.stangent/prompts/load-profiles.md`
  - `.stangent/prompts/pipeline-states.md`
  - `.stangent/prompts/run-log-format.md`
  - `.stangent/prompts/section-ownership.md`

For each:
  - Exists → [PASS] prompts/{filename}
  - Missing → [WARN] prompts/{filename} — missing. Fix: re-run init.py

---

## Step 5 — Agent files

Check that each of the following exists:

  - `.claude/agents/stangent.md`
  - `.claude/agents/stangent-planner.md`
  - `.claude/agents/stangent-implementer.md`
  - `.claude/agents/stangent-reviewer.md`
  - `.claude/agents/stangent-srs.md`
  - `.claude/agents/stangent-adr.md`
  - `.claude/agents/subagents/stangent-linter.md`
  - `.claude/agents/subagents/stangent-unit-tester.md`
  - `.claude/agents/subagents/stangent-query-analyzer.md`
  - `.claude/agents/subagents/stangent-security-scanner.md`

For each:
  - Exists → [PASS] {path}
  - Missing → [FAIL] {path} — not installed. Fix: re-run init.py

---

## Step 6 — Gateway script

Check `.stangent/gateway/gateway.py` exists.
  - Exists → [PASS] .stangent/gateway/gateway.py
  - Missing → [FAIL] gateway.py — not found. Fix: re-run init.py

---

## Step 7 — PreToolUse hook

Read `.claude/settings.json`.
  - If not found: [FAIL] .claude/settings.json — missing. Fix: re-run init.py. Skip to Step 8.
  - Parse JSON.
  - Check hooks.PreToolUse contains an entry whose hooks[].command includes "gateway.py".
  - Found → [PASS] .claude/settings.json — PreToolUse gateway hook present
  - Not found → [FAIL] .claude/settings.json — gateway hook missing.
    Fix: re-run init.py (it will add the hook without touching your other settings)

---

## Step 8 — Feature registry

Check config.paths.registry_path exists.
  - Missing → [WARN] features_registry.json — not found. Fix: re-run init.py. Skip sub-checks.

If exists:
  - Parse JSON.
  - Valid JSON with "next_id", "prefix", "features" keys → [PASS] features_registry.json
    — {next_id - 1} feature(s) registered
  - Invalid or missing keys → [WARN] features_registry.json — malformed.
    Fix: re-run init.py or manually correct the file.

---

## Step 9 — Gateway state consistency

Check `.stangent/gateway/active.json`:
  - Not found → [PASS] gateway state — no active feature (permissive mode)

  If found:
    Read feature_id from active.json.
    Find the feature file: glob {feature_dir}/{feature_id}*.md
    If not found: [WARN] gateway state — active.json points to {feature_id} but no feature file found.
      Suggest: /cleanup to remove stale active.json

    If found:
      Read status from feature file frontmatter.
      If status is COMPLETE, ABANDONED, ESCALATED, or FAILED:
        [WARN] gateway state — active.json is stale ({feature_id} is {status})
        Suggest: /cleanup to clear it.
      Else:
        [PASS] gateway state — {feature_id} active, status: {status}

Check `.stangent/gateway/active.json.paused`:
  - Found → [WARN] gateway is paused (active.json.paused exists for {feature_id})
    Suggest: /gateway resume to re-enable enforcement

---

## Step 10 — Report summary

Count PASS, WARN, FAIL totals.

Output:

```
Stangent Doctor — {project_root.name}
──────────────────────────────────────
[PASS] .stangent/config.json — v1.0.0 (anthropic, python)
[PASS] .stangent/features/
[PASS] .stangent/archive/
...
[FAIL] .claude/agents/stangent.md — not installed
...

Summary: {PASS} passed  {WARN} warnings  {FAIL} failures

{If any FAIL}
Run: python <path-to-stangent>/init.py
to repair all failures.

{If WARN only, no FAIL}
Run: python <path-to-stangent>/init.py
to repair warnings (safe to run on an existing project — it only adds missing files).

{If all PASS}
All checks passed. Stangent is correctly configured.
```
