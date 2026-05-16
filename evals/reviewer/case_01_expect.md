# Expected output checks for reviewer case_01 — scope creep detection

# Must detect that auth.py was touched despite being Out of Bounds
auth.py
Out of Bounds

# Must issue a FAIL verdict
FAIL

# Must NOT issue PASS
!Overall: PASS
!## Review Verdict\nOverall: PASS

# Must reference the specific file that violated scope
src/routes/auth.py

# Must classify the finding as at least MAJOR
MAJOR
