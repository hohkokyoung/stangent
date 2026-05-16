"""
Assertions for implementer case_01 — pre-implementation scan and test planning.
"""

import re


def assert_prescan_covers_all_files(response: str):
    """Pre-implementation scan must cover all files in ## Files to Touch."""
    expected_files = [
        "src/routes/auth.py",
        "src/models/reset_token.py",
        "src/services/email_service.py",
        "tests/test_reset_password.py",
    ]
    text = response.lower()
    missing = [f for f in expected_files if f.lower() not in text]
    if missing:
        return f"Pre-implementation scan did not mention: {', '.join(missing)}"
    return None


def assert_each_ac_has_test_decision(response: str):
    """Each of the 4 ACs must have a test decision: test / extracted / n/a."""
    ac_signals = [
        ("POST /auth/reset-password", ["test", "extracted", "n/a", "not applicable"]),
        ("reset token",               ["test", "extracted", "n/a", "not applicable"]),
        ("EmailService",              ["test", "extracted", "n/a", "not applicable"]),
        ("404",                       ["test", "extracted", "n/a", "not applicable"]),
    ]
    text = response.lower()
    for signal, decisions in ac_signals:
        if signal.lower() not in text:
            return f"AC involving '{signal}' was not addressed in test planning."
        # Find the area around the signal and check for a decision word
        idx = text.find(signal.lower())
        context = text[max(0, idx - 50):idx + 200]
        if not any(d in context for d in decisions):
            return (
                f"AC involving '{signal}' has no test decision "
                f"(expected: test / extracted / n/a)."
            )
    return None


def assert_adr_001_compliance_planned(response: str):
    """Must note SQLAlchemy ORM compliance per ADR-001."""
    text = response.lower()
    has_sqlalchemy = "sqlalchemy" in text
    has_adr        = "adr-001" in text
    if not has_sqlalchemy and not has_adr:
        return (
            "Implementer did not plan for ADR-001 compliance "
            "(SQLAlchemy ORM required, no raw SQL)."
        )
    return None


def assert_no_sdk_behaviour_tests(response: str):
    """Must not plan tests that test EmailService's internal behaviour."""
    text = response.lower()
    bad_patterns = [
        "assert send_email was called",
        "assert emailservice",
        "test the email service",
        "verify email service",
    ]
    for p in bad_patterns:
        if p in text:
            return (
                f"Implementer planned to test SDK/service internals: {p!r}. "
                "Tests should verify the feature behaviour, not that EmailService was called."
            )
    return None


def assert_no_implementation_code(response: str):
    """Must not write actual implementation code — planning only."""
    bad_patterns = [
        r'@router\.(post|get|put|delete)',
        r'async def \w+\(',
        r'def reset_password',
    ]
    for pattern in bad_patterns:
        if re.search(pattern, response, re.IGNORECASE):
            return f"Implementer wrote implementation code (matched: {pattern!r}). Should be planning only."
    return None


assertions = [
    assert_prescan_covers_all_files,
    assert_each_ac_has_test_decision,
    assert_adr_001_compliance_planned,
    assert_no_sdk_behaviour_tests,
    assert_no_implementation_code,
]
