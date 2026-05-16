# Expected output checks for reviewer case_02 — security finding + missing AC

# Must catch the hardcoded token
hardcoded
export.py:47

# Must catch the missing rate limiting AC
rate limit
rate-limit

# Must issue FAIL
FAIL

# Must NOT issue PASS
!Overall: PASS

# Hardcoded secret must be CRITICAL
CRITICAL

# Missing AC must be flagged
Acceptance Criteria
