---
description: Create or manage Architectural Decision Records (ADRs)
argument-hint: "new <title> | list | accept <id> | supersede <old-id> <new-id>"
---

# /agentic-adr

Manage project-level architectural decisions. ADRs override skill defaults and bind every future task once `status: accepted`.

## Subcommands

### `new <title>`

1. Allocate id: `python .claude/hooks/lib/adr_id.py next` (e.g. `ADR-003`).
2. Create `.claude/adrs/<id>-<slug>.md` by copying `.claude/adrs/_template.md`.
3. Set frontmatter: `id`, `title`, `status: proposed`, `date` (UTC YYYY-MM-DD), `supersedes: null`.
4. Print the path and tell the user: *"Edit it, then re-run `/agentic-adr accept <id>` to make it binding."*

### `list`

Read every file matching `.claude/adrs/ADR-*.md`. Print a table:

```
ID       STATUS       TITLE                                SUPERSEDES
ADR-001  accepted     All timestamps are UTC               -
ADR-002  proposed     Money stored as integer cents        -
ADR-003  superseded   JWT in cookie                        replaced by ADR-005
```

### `accept <id>`

Edit `.claude/adrs/<id>-*.md` frontmatter: `status: proposed` → `status: accepted`. Refuse if the file is missing or if it's already accepted/superseded.

### `supersede <old-id> <new-id>`

1. Verify both files exist; `<new-id>` must be `status: accepted` or `proposed`.
2. Edit `<old-id>`'s frontmatter: `status: superseded`.
3. Edit `<new-id>`'s frontmatter: `supersedes: <old-id>`.
4. Print confirmation.

## Constraints

- Never delete ADR files. Superseded ADRs stay for traceability.
- Never modify a `superseded` ADR's body — write a new ADR instead.
- All edits go through these subcommands or are manual file edits — no agent invocation needed.
