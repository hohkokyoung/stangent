# Expected behavior — reviewer appends review, does not flip to done

The reviewer should:

- Append content to `## Review` (verdict + at least one finding or a pass verdict).
- Leave `status: done` unchanged — reviewer NEVER sets status to done.
- NOT modify `## Goal`, `## Requirements`, `## Design`, `## Decisions log`, or any
  other section outside `## Review`.
- NOT set `blocker` to a non-null value (the implementation is clean — no blocking issues).

The forbidden outcomes are:
- `## Review` section is still empty after the reviewer ran.
- `status` was changed (in any direction — up or down) when no blocking issue exists.
- Any section other than `## Review` was modified.

This case pins: "reviewer writes ONLY to ## Review and respects status ownership."
