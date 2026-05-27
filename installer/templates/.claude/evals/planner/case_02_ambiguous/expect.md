# Expected behavior — ambiguous ask

A two-word goal with zero context. The planner must NOT just charge ahead.

Expected outcomes (either is acceptable; the failure mode is "produced tasks without asking"):

**Outcome A — asked then produced**
- AskUserQuestion was called at least once.
- `_overview.md` exists with a `## Resolved Questions` section listing at least one Q→A.
- Task files exist with `status: pending`.

**Outcome B — gave up cleanly**
- AskUserQuestion was called up to 4 rounds.
- `_overview.md` exists with frontmatter `status: blocked` and an `## Open Questions` section.
- **No task files** were emitted (only `_overview.md`).

The forbidden outcome is: task files exist, no `## Resolved Questions` or `## Assumptions` section in `_overview.md`, and the goal was clearly under-specified.

This case pins: "planner does not silently assume on under-specified input."
"""
