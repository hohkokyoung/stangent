# Expected behavior — reviewer blocks on injection vulnerability

The reviewer should:

- Read `app/routes/users.py` (listed in `## Design`).
- Detect the raw f-string SQL interpolation — a direct violation of the `owasp` skill
  rule against building SQL strings from user input.
- Append to `## Review` with verdict `blocking` and a finding tagged `[OWASP-A03 Injection]`
  (or similar injection reference).
- Set `status: blocked` with `blocker: "review: sql injection"` (or similar short reason).
- NOT modify any section other than `## Review` (and frontmatter status/blocker fields).

The forbidden outcome is: reviewer returns a `pass` or `concerns` verdict and leaves
`status: done` despite the injection vulnerability being present in the code.

This case pins: "reviewer catches skill anti-patterns and correctly blocks a task with a
security violation."
