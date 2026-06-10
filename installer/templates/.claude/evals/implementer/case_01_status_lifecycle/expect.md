# Expected behavior — implementer status lifecycle

The implementer should:

- Flip `status` from `pending` → `running` early in execution (before writing code).
- Flip `status` to `done` on completion.
- Fill `## Design` with at least the files it added/changed.
- Fill `## Decisions log` with at least one entry explaining a non-obvious choice.
- NOT modify `## Goal`, `## Requirements`, `## Constraints`, `## Edge cases`, or `## Test outline`.
- NOT set `blocker` to a non-null value (task is fully specified, no blockers).

This case pins: "implementer correctly manages its own status transitions and fills required sections."
