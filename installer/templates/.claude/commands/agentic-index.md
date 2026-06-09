---
description: (Re)embed all skills/*/references/*.md chunks into .claude/state/vectors.db. Also detects project stack(s) and writes test_framework + project_index_globs to .claude/state/project.yml.
argument-hint: ""
---

# /agentic-index

Embed the references corpus into sqlite-vec and detect the project stack.

## Procedure

### Step 1 — Embed references

Run:
```
PYEXE=$(ls .venv/bin/python venv/bin/python .env/bin/python 2>/dev/null | head -1) && ${PYEXE:-python3} .claude/hooks/lib/retriever.py reindex
```

The script:
1. Reads `.claude/.agentic.yml` to get `enabled_skills` + embedding provider.
2. Walks `.claude/skills/<skill>/references/*.md` for each enabled skill.
3. Chunks each file at ~400 tokens.
4. Embeds each chunk (provider from `.agentic.yml`, fallback `fastembed`).
5. Writes to `.claude/state/vectors.db` (table `chunks` with columns: id, skill, file, anchor, text, embedding).
6. v1: full re-embed each run. No hash cache.

Print per-skill chunk counts and total time.

### Step 2 — Detect project stack(s)

Inspect the project root to determine the test framework and default project index globs. **Evaluate every signal independently and accumulate all matching globs** — do not stop at the first match. This supports monorepos where multiple stacks coexist (e.g. Flutter mobile + FastAPI backend).

#### Signal table (evaluate ALL rows, accumulate matches)

| Signal | Contributes to `test_framework` | Globs to add |
|---|---|---|
| `pubspec.yaml` exists | `maestro` | `["**/*.dart"]` |
| `android/` or `ios/` dir exists (without `pubspec.yaml`) | `maestro` | `["**/*.kt", "**/*.swift"]` |
| `package.json` with `next`, `react`, `vue`, `svelte`, `nuxt`, `angular`, `vite` in deps | `playwright` | `["**/*.ts", "**/*.tsx", "**/*.js", "**/*.jsx"]` |
| `package.json` exists (no browser-framework deps above — backend/CLI JS) | _(no test framework change)_ | `["**/*.ts", "**/*.js"]` |
| `requirements.txt` or `pyproject.toml` exists | `pytest` | `["**/*.py"]` |
| `go.mod` exists | `go_test` | `["**/*.go"]` |
| `Cargo.toml` exists | `cargo_test` | `["**/*.rs"]` |
| `Gemfile` exists | `rspec` | `["**/*.rb"]` |
| `pom.xml` or `build.gradle` exists (without `android/` dir) | `junit` | `["**/*.java", "**/*.kt"]` |
| `*.csproj` or `*.sln` exists | `dotnet_test` | `["**/*.cs"]` |
| `mix.exs` exists | `ex_unit` | `["**/*.ex", "**/*.exs"]` |
| `composer.json` exists | _(no test framework change)_ | `["**/*.php"]` |

#### Rules for `test_framework`
- If any mobile signal matched → `maestro`
- Else if any browser-frontend JS signal matched → `playwright`
- Else use the first matched from: `pytest` → `go_test` → `cargo_test` → `rspec` → `junit` → `dotnet_test` → `ex_unit`
- Else → `unknown`

#### Rules for `project_index_globs`
- Union of all matched glob lists, deduplicated
- If no signals matched → `[]`

Write the result to `.claude/state/project.yml`:
```yaml
test_framework: maestro           # or: playwright | unknown
detected_at: <ISO timestamp>
project_index_globs:              # union of all matched globs; used by retriever.py when project_index.include in .agentic.yml is empty
  - "**/*.dart"
  - "**/*.py"
```

If `.claude/state/project.yml` already exists, overwrite only the `test_framework`, `detected_at`, and `project_index_globs` fields (preserve any other fields).

Print: `[agentic-index] detected test_framework: <value>`
Print: `[agentic-index] project_index_globs: <globs>`

If no signals matched, print:
```
[agentic-index] could not detect stack. Set test_framework and project_index_globs manually in .claude/state/project.yml
```

### Step 3 — Index project files

This step runs automatically inside the same `python3 .claude/hooks/lib/retriever.py reindex` call from Step 1.

The retriever:
1. Reads `project_index.include` from `.agentic.yml` (manual override). If non-empty, uses those globs. Otherwise falls back to `project_index_globs` from `.claude/state/project.yml` (auto-detected in Step 2).
2. If no globs are configured, prints a skip notice and continues.
3. Runs an incremental, hash-cached pass over matching project files — only re-embeds files whose content changed.
4. Each chunk is stored with `skill="project"`, `file=<relative path>`, `anchor=<definition header>`, and `text=<raw code body>`.
5. Prints: `[retriever] project: N indexed, M skipped (unchanged), K removed (stale), S skipped (non-UTF-8)`

Print a reminder to run `/agentic-index` again if the user later adds `project_index.include` to `.agentic.yml` manually.

### Step 4 — Confirm enabled skills

Print a reminder if the detected `test_framework` skill is not in `enabled_skills` in `.agentic.yml`:
```
[agentic-index] warning: test_framework=playwright but skill 'playwright' is not in enabled_skills. Add it to .agentic.yml and re-run /agentic-index.
```

## Constraints

- Do not partial-embed. Full rebuild only in v1.
- Do not touch task files or `_overview.md`.
- Do not modify `.agentic.yml` — it is system-owned.
