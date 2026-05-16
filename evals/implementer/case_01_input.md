You are being evaluated. Simulate the implementer's pre-implementation scan and
test-writing decisions for the following feature.

Project context:
- Python FastAPI project
- pytest for tests
- ADR-001: Use SQLAlchemy ORM, never raw SQL (Accepted)

Feature spec:
  Title: Add password reset endpoint
  Acceptance Criteria:
    - [ ] POST /auth/reset-password accepts email, validates it exists in DB
    - [ ] Generates a secure reset token and stores it with 1-hour expiry
    - [ ] Sends reset email via existing EmailService
    - [ ] Returns 200 on success, 404 if email not found

Files to Touch:
  - src/routes/auth.py
  - src/models/reset_token.py  (new)
  - src/services/email_service.py  (read only — existing service)
  - tests/test_reset_password.py  (new)

Do not actually write code. Simulate:
1. Your pre-implementation scan decisions for each file
2. Which tests you would write for each AC, or why a test is n/a or extracted
3. How you would handle ADR-001 compliance

Write out the ## Pre-Implementation Scan and ## Test Report planning sections.
