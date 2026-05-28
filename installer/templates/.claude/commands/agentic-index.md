---
description: (Re)embed all skills/*/references/*.md chunks into .claude/state/vectors.db. Also detects project stack and writes test_framework to .claude/state/project.yml.
argument-hint: ""
---

# /agentic-index

Embed the references corpus into sqlite-vec and detect the project stack.

## Procedure

### Step 1 — Embed references

Run:
```
python .claude/hooks/lib/retriever.py reindex
```

The script:
1. Reads `.claude/.agentic.yml` to get `enabled_skills` + embedding provider.
2. Walks `.claude/skills/<skill>/references/*.md` for each enabled skill.
3. Chunks each file at ~400 tokens.
4. Embeds each chunk (provider from `.agentic.yml`, fallback `fastembed`).
5. Writes to `.claude/state/vectors.db` (table `chunks` with columns: id, skill, file, anchor, text, embedding).
6. v1: full re-embed each run. No hash cache.

Print per-skill chunk counts and total time.

### Step 2 — Detect project stack

Inspect the project root to determine the test framework. Check in this order:

| Signal | Detected framework |
|---|---|
| `pubspec.yaml` exists | `maestro` |
| `android/` or `ios/` dir exists (without pubspec) | `maestro` |
| `package.json` with `next`, `react`, `vue`, `svelte`, `nuxt` in dependencies | `playwright` |
| `package.json` exists (any web JS project) | `playwright` |
| None of the above | `unknown` |

Write the result to `.claude/state/project.yml`:
```yaml
test_framework: playwright   # or: maestro | unknown
detected_at: <ISO timestamp>
```

If `.claude/state/project.yml` already exists, overwrite only the `test_framework` and `detected_at` fields (preserve any other fields).

Print: `[agentic-index] detected test_framework: <value>`

If `unknown`, print:
```
[agentic-index] could not detect stack. Set test_framework manually in .claude/state/project.yml
```

### Step 3 — Confirm enabled skills

Print a reminder if the detected `test_framework` skill is not in `enabled_skills` in `.agentic.yml`:
```
[agentic-index] warning: test_framework=playwright but skill 'playwright' is not in enabled_skills. Add it to .agentic.yml and re-run /agentic-index.
```

## Constraints

- Do not partial-embed. Full rebuild only in v1.
- Do not touch task files or `_overview.md`.
- Do not modify `.agentic.yml` — it is system-owned.
