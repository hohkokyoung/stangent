# Expected behavior — minimal task

The planner should recognise this as a trivial, fully-specified ask and:

- Emit **1 task** (maybe 2 if it includes a tester task; not more).
- The implementer task's `skills_to_load` is exactly `[fastapi]`.
- The task's `adrs` is `[]` (no project ADRs in the eval env).
- The task's `status` is `pending`.
- The planner should **NOT** ask AskUserQuestion — there is no real ambiguity.
- `_overview.md` exists.

This case pins: "no over-decomposition for trivially-specified asks; no unnecessary questioning."
