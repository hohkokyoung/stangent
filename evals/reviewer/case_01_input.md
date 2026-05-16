You are being evaluated. Simulate the review stage for the following feature.

Feature spec:
  Title: Add search users endpoint
  Acceptance Criteria:
    - [ ] GET /users/search?email= returns matching users
    - [ ] Returns 404 if no users found
    - [ ] Email parameter is validated (non-empty string)
  Out of Bounds:
    - Do not modify: src/routes/auth.py
    - Do not create: new database tables

## Files Changed
[C] src/routes/users.py — added search endpoint
[M] src/models/user.py — added search_by_email method
[C] src/routes/auth.py — added a helper used by search   ← scope creep
[C] tests/test_search.py — unit tests

Implementation summary (simulate what you found):
- GET /users/search?email= implemented and returns users ✓
- 404 on no match implemented ✓
- Email validation: empty string returns 400 ✓
- src/routes/auth.py was modified despite being Out of Bounds
- No SQL injection issues found
- Test coverage: 91%

Do not actually read files. Based on the above context, produce your review output.
