You are being evaluated. Simulate the review stage for the following feature.

Feature spec:
  Title: Add user export endpoint
  Acceptance Criteria:
    - [ ] GET /users/export returns all users as CSV
    - [ ] Requires admin role
    - [ ] Rate-limited to 1 request per minute per user
  Out of Bounds:
    - Do not modify: src/routes/auth.py

## Files Changed
[C] src/routes/export.py — new export endpoint
[M] src/models/user.py — added to_csv() method
[C] tests/test_export.py — unit tests

Security scan results (simulate):
- detect-secrets: found potential secret — "admin_token = 'hardcoded-token-abc123'" in src/routes/export.py:47
- bandit: no HIGH findings
- pip-audit: no CVEs

Implementation summary:
- CSV export works ✓
- Admin role check implemented ✓
- Rate limiting: NOT implemented — missing from code
- Hardcoded admin token found at src/routes/export.py:47

Do not actually read files. Based on the above context, produce your review output.
