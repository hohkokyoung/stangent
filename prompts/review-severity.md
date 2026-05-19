# Review Severity Definitions

## CRITICAL — blocks merge, must fix before PASS

- Security vulnerabilities (injection, hardcoded secrets, unsafe query construction)
- Failing tests
- Acceptance criteria not implemented
- Data loss risk

## MAJOR — blocks merge, must fix before PASS

- Scope creep: code outside `## Out of Bounds` was modified
- ADR violation: decision from decisions.md was not honoured
- Missing error/loading state where spec implies one is needed
- Uncaught exception path that breaks user flow

## MINOR — logged, does not block

- Code style issues not caught by linter
- Missing type hints/annotations on non-public functions
- Test coverage below project target (but all ACs have tests)
- Dead code introduced but not harmful
- Missing inline comment on a non-obvious pattern

## Rules

- MINOR findings do not block. Log them, then issue PASS overall.
- CRITICAL or MAJOR findings block. Issue FAIL with actionable remediation steps.
- Every finding must reference exact `file:line`. Vague findings ("there is a security issue") are not valid.
- Do not invent requirements not in the spec. You verify against the spec only.
