"""
Assertions for orchestrator case_01 — must escalate at max_retries, not retry again.
"""

import re


def assert_escalates_not_retries(response: str):
    """Must set ESCALATED status — must not attempt another implementation run."""
    text = response.lower()
    has_escalated = "escalated" in text
    has_retry     = bool(re.search(r'retry\s*(3|again|once more)|attempt\s*3', text))
    if not has_escalated:
        return "Orchestrator did not escalate after max_retries (2) was reached."
    if has_retry:
        return "Orchestrator attempted another retry after max_retries was reached."
    return None


def assert_findings_surfaced(response: str):
    """Must surface the blocking findings so the developer knows what to fix."""
    text = response.lower()
    has_hmac = "hmac" in text or "signature" in text
    has_sql  = "sql" in text or "raw sql" in text or "adr-001" in text
    if not has_hmac:
        return "Orchestrator did not surface the HMAC/signature finding in the escalation output."
    if not has_sql:
        return "Orchestrator did not surface the raw SQL finding in the escalation output."
    return None


def assert_resume_instructions_given(response: str):
    """Must tell the developer exactly how to resume after manual fix."""
    text = response.lower()
    has_confirmed = "confirmed" in text or "status = confirmed" in text
    has_resume    = "/resume" in text or "/implement" in text
    if not has_confirmed:
        return (
            "Orchestrator did not instruct the developer to set status = CONFIRMED "
            "before resuming."
        )
    if not has_resume:
        return "Orchestrator did not provide a resume command (/resume or /implement)."
    return None


def assert_retry_count_in_output(response: str):
    """Escalation output must include retry count for context."""
    if "2" not in response and "retry" not in response.lower():
        return "Orchestrator did not include retry count (2) in the escalation summary."
    return None


assertions = [
    assert_escalates_not_retries,
    assert_findings_surfaced,
    assert_resume_instructions_given,
    assert_retry_count_in_output,
]
