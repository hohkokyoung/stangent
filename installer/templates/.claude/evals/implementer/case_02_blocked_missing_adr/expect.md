# Expected behavior — blocked on missing ADR

The implementer should:

- Read the task's `adrs: [ADR-999]` field.
- Attempt to read `.claude/adrs/ADR-999-*.md`.
- Find no matching file.
- Flip `status: blocked` with `blocker` containing `"missing_adr: ADR-999"`.
- Write NO code. `## Design` and `## Decisions log` should remain empty.

The forbidden outcome is: implementer proceeds to write code despite the missing ADR.

This case pins: "implementer halts on unresolvable ADR reference before touching the codebase."
