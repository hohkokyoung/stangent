---
description: Prune .claude/state/ cruft — empty review dirs from aborted reviews, and run artifacts (plans, logs, review outputs) older than a retention window. Dry-run unless --apply.
argument-hint: "[days:<N>] [--apply]"
---

# /agentic-clean-state

Tidy **`.claude/state/`**. State accumulates across runs and nothing prunes it:
empty review directories left behind by aborted reviews, plus old plan dirs,
per-run logs, and review outputs. This command reports and removes that cruft.

> This is **STATE** hygiene. For **CODE** cleanup (refactoring oversized or
> duplicated source), use `/agentic-cleanup` — a different command.

## Arguments

| Argument | Meaning |
|---|---|
| `days:<N>` | Retention window in days (default **30**). Plan dirs, logs, and review outputs older than this are pruned. Empty review dirs are pruned regardless of age. |
| `--apply` | Actually delete. Without it the command is a **dry-run** — it only reports what it would remove. |

## Procedure

1. Resolve the interpreter and run a **dry-run** first:
   ```
   PYEXE=$(ls .venv/bin/python venv/bin/python .env/bin/python 2>/dev/null | head -1)
   ${PYEXE:-python3} .claude/hooks/lib/state.py clean [--max-age-days <N>]
   ```
2. Show the candidate list to the developer.
3. Only if the developer confirms (or passed `--apply`), re-run with `--apply`:
   ```
   ${PYEXE:-python3} .claude/hooks/lib/state.py clean [--max-age-days <N>] --apply
   ```
4. Print a one-line summary of what was removed.

## Safety notes

- `.claude/state/` is gitignored — these are local working artifacts, not history.
- **Never touched:** `vectors.db`, `project.yml`, `skills_digest.md`, and the
  `current_*.txt` dispatch state.
- Populated plan/review dirs are removed only when older than the retention window.
