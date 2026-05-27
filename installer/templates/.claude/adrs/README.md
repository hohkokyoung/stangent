# ADRs — Architectural Decision Records

Project-level architectural rules that bind every task. Use ADRs for decisions that should outlive any single feature:

- "All timestamps are UTC, stored as `timestamptz`, serialised as ISO-8601 with `Z`."
- "PII is never logged."
- "Auth header is `Authorization: Bearer <jwt>`; no cookies, no custom schemes."
- "Money is stored as integer cents in DB; rendered with `Intl.NumberFormat` in UI."

ADRs ride alongside skills in the agent context, but at higher precedence: skills describe how a stack works; ADRs override defaults with project-specific rules.

## Lifecycle

```
proposed  →  accepted  →  superseded
```

- `proposed`: drafted; not yet binding. Planner does NOT inject these.
- `accepted`: binding. Planner reads, lists relevant ones on each task as `adrs: [ADR-001]`. Implementer/reviewer/tester load them verbatim.
- `superseded`: replaced. Kept in the repo for history. The `supersedes:` field on the newer ADR points back at the older one.

## File format

See `.claude/templates/adr.md` for the canonical structure. New ADRs are created with `/agentic-adr new <title>`, which copies that template, allocates the next id, and sets `status: proposed`.

## Allocating an id

```
python .claude/hooks/lib/adr_id.py next       # prints ADR-003
```

Or use the slash command:

```
/agentic-adr new "All timestamps are UTC"
```

## Conflict precedence

```
system  >  role  >  ADRs (accepted, verbatim)  >  skills (verbatim)  >  retrieved chunks  >  task file
```

ADRs win on conflict because they're explicit project decisions overriding generic stack defaults. In practice, ADRs and skills should rarely overlap — skills describe HOW to use a stack, ADRs describe WHAT this project chose.
