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
     Fix: run python <your-stangent-path>/init.py to create it.
     (If you don't have the stangent source: git clone https://github.com/hohkokyoung/stangent.git ~/stangent)"
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
  - `.stangent/prompts/classifier.md`
  - `.stangent/prompts/context-budget.md`
  - `.stangent/prompts/efficiency-rules.md`
  - `.stangent/prompts/load-profiles.md`
  - `.stangent/prompts/memory.md`
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
  - `.claude/agents/stangent-debug.md`
  - `.claude/agents/subagents/stangent-linter.md`
  - `.claude/agents/subagents/stangent-unit-tester.md`
  - `.claude/agents/subagents/stangent-query-analyzer.md`
  - `.claude/agents/subagents/stangent-security-scanner.md`
  - `.claude/agents/subagents/stangent-performance-reviewer.md`
  - `.claude/agents/subagents/stangent-quality-reviewer.md`

For each:
  - Exists → [PASS] {path}
  - Missing → [FAIL] {path} — not installed. Fix: re-run init.py

---

## Step 6 — Gateway and observer scripts

Check `.stangent/gateway/gateway.py` exists.
  - Exists → [PASS] .stangent/gateway/gateway.py
  - Missing → [FAIL] gateway.py — not found. Fix: re-run init.py

Check `.stangent/observer/observer.py` exists.
  - Exists → [PASS] .stangent/observer/observer.py
  - Missing → [WARN] observer.py — not found. Fix: re-run init.py
    (observer is required for /stats token breakdown)

---

## Step 7 — Hook registration

Read `.claude/settings.json`.
  - If not found: [FAIL] .claude/settings.json — missing. Fix: re-run init.py. Skip to Step 8.
  - Parse JSON.

  - Check hooks.PreToolUse contains an entry whose hooks[].command includes "gateway.py".
    Found → [PASS] .claude/settings.json — PreToolUse gateway hook present
    Not found → [FAIL] .claude/settings.json — gateway hook missing.
    Fix: re-run init.py

  - Check hooks.PostToolUse contains an entry whose hooks[].command includes "observer.py".
    Found → [PASS] .claude/settings.json — PostToolUse observer hook present
    Not found → [WARN] .claude/settings.json — observer hook missing.
    Fix: re-run init.py (required for /stats)

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

## Step 8.5 — Registry consistency (quick compact audit)

(Only if registry is valid from Step 8.)

Glob `{feature_dir}/FEAT-*.md` and `{archive_dir}/FEAT-*.md`.
Extract all feature_ids from filenames (leading FEAT-NNN portion).
Cross-reference with registry.features keys.

  - Ghost entries (in registry, no file): count them
  - Orphan files (file exists, not in registry): count them
  - ID gaps: parse numeric parts of all known IDs; count missing numbers between min and max

If ghost_count > 0 OR orphan_count > 0 OR gap_count > 0:
  Output: "[WARN] Registry consistency — {ghost_count} ghost(s), {orphan_count} orphan(s), {gap_count} gap(s)
           Fix: /compact --fix  (add --renumber to fill gaps)"
Else:
  Output: "[PASS] Registry consistency — no ghosts, orphans, or gaps"

Check registry.next_id > max known numeric ID:
  If next_id <= max_id:
    Output: "[WARN] Registry next_id ({registry.next_id}) <= max known ID ({max_id}). Fix: /compact --fix"

---

## Step 9 — Memory file

Check `config.paths.memory_path` (default: `.stangent/memory.md`):
  - Exists → [PASS] .stangent/memory.md
  - Missing → [WARN] .stangent/memory.md — not found. Fix: re-run init.py
    (memory.md is created on init — agents degrade gracefully without it
    but cross-feature learning will not accumulate)

---

## Step 10 — Cross-stack coordination (double-stack projects only)

Read `config.profiles`. If it contains both a backend profile (`fastapi` or `python`)
AND `flutter`, run these checks. Otherwise: skip this step entirely.

**10a. meta.md cascade rules**
Check `.stangent/meta.md`:
  - Exists → [PASS] .stangent/meta.md — cascade rules present
  - Missing → [WARN] .stangent/meta.md — not found.
    Fix: copy templates/meta_flutter_fastapi.md to .stangent/meta.md and fill
    in your route-to-service mappings. Without it, the planner will not
    automatically pull Flutter service files into spec scope when FastAPI
    routes are touched.

**10b. Cross-stack type reference**
Check `.stangent/prompts/cross-stack-types.md`:
  - Exists → [PASS] prompts/cross-stack-types.md — type mapping available
  - Missing → [FAIL] prompts/cross-stack-types.md — not found.
    Fix: re-run init.py (this file is installed by default).

**10c. FastAPI profile active**
If `flutter` is in `config.profiles` and a backend profile is `python`
(not `fastapi`):
  - [WARN] Backend profile is `python` but FastAPI-specific checks (async I/O,
    Pydantic v2 patterns, response_model enforcement) require the `fastapi` profile.
    Fix: re-run init.py --profile fastapi,flutter

**10d. Supabase prompt (only if `config.integrations.supabase.enabled = true`)**
Check `.stangent/prompts/supabase.md`:
  - Exists → [PASS] prompts/supabase.md — Supabase security rules available
  - Missing → [FAIL] prompts/supabase.md — not found.
    Fix: re-run init.py (this file is required when Supabase integration is enabled)

**10e. Supabase MCP (only if `config.integrations.supabase.mcp_server` is not null)**
Read `.claude/settings.local.json`. Check `mcpServers[{config.integrations.supabase.mcp_server}]` exists:
  - Exists → [PASS] Supabase MCP — configured in settings.local.json
  - Missing → [WARN] Supabase MCP — mcp_server set in config but not found in .claude/settings.local.json.
    Fix: re-run init.py to set up Supabase MCP, or add the entry manually.

---

## Step 11 — Gateway state consistency

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

## Step 12 — Report summary

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
Run: python {config.stangent_source_path}/init.py
to repair all failures.

{If WARN only, no FAIL}
Run: python {config.stangent_source_path}/init.py
to repair warnings (safe to run on an existing project — it only adds missing files).

{If all PASS}
All checks passed. Stangent is correctly configured.
```
