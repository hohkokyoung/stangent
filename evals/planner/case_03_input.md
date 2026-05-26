You are being evaluated. Simulate the planning stage for the following feature request.

Project context:
- Python FastAPI backend
- Existing files: src/main.py, src/routes/users.py, src/models/user.py, src/models/test_user.py
- .stangent/decisions.json contains:
  [
    {
      "id": "ADR-001",
      "title": "Co-locate tests with source files",
      "status": "accepted",
      "decision": "Test files live beside their source file (e.g. src/models/test_user.py), not in a top-level tests/ directory.",
      "rationale": "Learned from FEAT-001: co-located tests are easier to find and keep in sync.",
      "consequences": ["No tests/ directory at project root."],
      "applies_to": ["src/**"],
      "created": "2024-01-15"
    }
  ]
- .stangent/srs.jsonl contains one line:
  {"feat_id":"FEAT-001","title":"User model","scope":"Create User SQLAlchemy model","acs":["User has id, email, created_at"],"env_vars":[],"security_summary":"CRITICAL: raw string formatting in query — SQL injection risk in src/models/user.py. Fixed with parameterized queries.","updated":"2024-01-15"}

Feature request: add an endpoint to search users by email

Do not actually read files. Simulate what you would do based on the project context above.
Write out the full feature spec you would produce.
