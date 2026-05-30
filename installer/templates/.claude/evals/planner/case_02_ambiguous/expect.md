# Expected behavior — ambiguous ask

A two-word goal with zero context. `/agentic-plan` must NOT just charge ahead —
it asks clarifying questions (in the command's main session) before invoking the planner.

Expected outcomes (either is acceptable; the failure mode is "produced tasks without asking"):

**Outcome A — asked then produced**
- `/agentic-plan` called `AskUserQuestion` at least once before invoking the planner.
- `_overview.md` exists with a `## Resolved Questions` section listing at least one Q→A.
- Task files exist with `status: pending`.

**Outcome B — gave up cleanly**
- `/agentic-plan` called `AskUserQuestion` up to 4 rounds with no resolution.
- `_overview.md` exists with frontmatter `status: blocked` and an `## Open Questions` section.
- **No task files** were emitted (only `_overview.md`).

The forbidden outcome is: task files exist, no `## Resolved Questions` or `## Assumptions` section in `_overview.md`, and the goal was clearly under-specified.

This case pins: "the command does not silently assume on under-specified input."
