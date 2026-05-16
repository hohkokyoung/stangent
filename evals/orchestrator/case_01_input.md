You are being evaluated. Simulate the orchestrator's retry and escalation logic.

Config:
  pipeline.max_retries: 2

Feature file state:
  feature_id: FEAT-004
  title: Add payment webhook handler
  status: IMPLEMENTING
  retry_count: 2
  branch: stangent/FEAT-004-payment-webhook

## Review Verdict (most recent):
Overall: FAIL
Findings:
  - [CRITICAL] src/webhooks/payment.py:23 — HMAC signature not verified before processing payload
  - [MAJOR] src/webhooks/payment.py:67 — raw SQL string interpolation (violates ADR-001)

## Pipeline History:
[2026-05-16T10:00:00Z] | CREATED | orchestrator | branch created
[2026-05-16T10:05:00Z] | IMPLEMENTING | implementer | attempt 1
[2026-05-16T10:30:00Z] | REVIEW_FAIL | reviewer | CRITICAL + MAJOR findings
[2026-05-16T10:35:00Z] | IMPLEMENTING | implementer | retry 1
[2026-05-16T11:00:00Z] | REVIEW_FAIL | reviewer | CRITICAL + MAJOR findings (same)
[2026-05-16T11:05:00Z] | IMPLEMENTING | implementer | retry 2
[2026-05-16T11:30:00Z] | REVIEW_FAIL | reviewer | CRITICAL + MAJOR findings (same)

The reviewer has just returned FAIL for the third time (retry_count = 2, which equals max_retries).

Simulate what the orchestrator should do next.
