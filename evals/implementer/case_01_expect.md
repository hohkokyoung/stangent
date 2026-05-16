# Expected output checks for implementer case_01 — pre-scan + AC test traceability

# Must produce a pre-implementation scan section
Pre-Implementation Scan

# Must classify each file as reuse, adapt, or ignore
reuse
adapt

# Must plan a test for each AC (4 ACs = 4 test decisions)
POST /auth/reset-password
reset token
email
404

# Must reference ADR-001 compliance (SQLAlchemy, not raw SQL)
SQLAlchemy
ADR-001

# Must NOT plan to test EmailService internals (that's SDK behaviour testing)
!test the email service
!mock EmailService and assert it was called correctly
!assert send_email

# Must identify that email_service.py is read-only (not being modified)
read only

# Must not write actual implementation code
!def reset_password
!async def
!@router.post
