# Expected behavior — cross-stack feature

A well-specified ask that genuinely spans three stacks. Expectations:

- **Task count**: 4–6 tasks (not 1, not 10).
- **Skill coverage**: across all tasks, `skills_to_load` covers every stack mentioned — at least one task touches `fastapi`, one `flutter`, one `supabase`. (A single task may carry multiple skills, e.g. a reviewer.)
- **Role mix**: at least one `implementer` task; ideally one `tester` task. Reviewer task optional.
- **Dependency edges**: the migration / table-creation task should precede the endpoint task; endpoint task should precede the flutter task. The graph must not have a cycle.
- **No file/class/function names** in any `intent` or `acceptance` — those belong to the implementer.
- All `status: pending`. All `run_id` consistent. `_overview.md` exists.

This case pins: "planner spans stacks coherently, gets the dependency order right, and stays out of implementation details."
