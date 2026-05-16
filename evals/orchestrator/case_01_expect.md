# Expected output checks for orchestrator case_01 — escalation after max retries

# Must escalate — not retry again
ESCALATED

# Must set status to ESCALATED
status = ESCALATED

# Must NOT attempt another retry
!retry
!attempt 3
!IMPLEMENTING

# Must output the exact findings that blocked it
HMAC
src/webhooks/payment.py:23

# Must tell the developer how to resume
CONFIRMED
/resume FEAT-004

# Must delete or disarm the gateway
active.json

# Must output retry count
2
