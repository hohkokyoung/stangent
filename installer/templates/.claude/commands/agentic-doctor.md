---
description: Diagnose install health — deps, dirs, config, MCP, vectors.db, hooks, skills, ADRs, git
argument-hint: "[--json]"
---

# /agentic-doctor

Run a battery of fast, deterministic checks against this project's agentic install.

## Procedure

1. Run:
   ```
   python .claude/hooks/lib/doctor.py
   ```
   (Add `--json` to `$ARGUMENTS` for machine-readable output.)
2. Show the script's stdout verbatim.
3. If the script exited non-zero, **stop**. Do not attempt to "fix" failures automatically — the user should triage and decide.
4. If everything is `ok` or just `warn`, print a one-line summary and the next step the user is most likely to want (e.g. *"vectors.db missing — run /agentic-index"* or *"all clear — try /agentic-plan <goal>"*).

## What it checks

- Python ≥ 3.10
- Required deps: `pyyaml`, `fastembed`, `sqlite-vec`
- Optional: `voyageai` + `VOYAGE_API_KEY`
- Directory tree: `agents/`, `commands/`, `skills/`, `hooks/`, `mcp/`, `evals/`, `templates/`, `adrs/`, `state/`
- Config files: `.agentic.yml` parses, `settings.json` parses (and doesn't contain dead `mcpServers` block)
- `.mcp.json` at project root: present, parses, no `REPLACE_WITH_` placeholders
- `vectors.db` exists + readable + reports chunk/skill counts
- Hooks compile (`pre_tool_use.py`, `post_tool_use.py`)
- Skills: each has `SKILL.md`; flags any >3000 tokens
- ADRs: count by status (proposed / accepted / superseded)
- Git: repo present, current branch, working-tree clean status

## Output legend

```
[ok]    — check passed
[warn]  — non-blocking issue (e.g. optional dep missing, working tree dirty)
[FAIL]  — blocks normal operation; fix before /agentic-plan or /agentic-build
```

Exit code: 0 if zero FAILs, 1 otherwise.

## Constraints

- Read-only with respect to project files. Doctor diagnoses; it never edits.
- Fast: target < 2 seconds total. No network calls. No MCP spawning.
- Adding a new check: extend `run_all()` in `doctor.py` with another function returning a `_check(name, status, detail)` dict.
