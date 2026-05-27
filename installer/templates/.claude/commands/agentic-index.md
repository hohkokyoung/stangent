---
description: (Re)embed all skills/*/references/*.md chunks into .claude/state/vectors.db
argument-hint: ""
---

# /agentic-index

Embed the references corpus into sqlite-vec.

## Procedure

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

## Output

Print per-skill chunk counts and total time.

## Constraints

- Do not partial-embed. Full rebuild only in v1.
- Do not touch task files or `_overview.md`.
