"""
Assertions for reviewer case_02 — hardcoded secret (CRITICAL) + unimplemented AC.
"""

import re


def assert_fail_verdict(response: str):
    """Must FAIL — has both a CRITICAL security finding and an unimplemented AC."""
    text = response.lower()
    has_fail = "overall: fail" in text or bool(
        re.search(r'##\s*review verdict.*?fail', text, re.DOTALL)
    )
    if not has_fail:
        return "Reviewer did not issue FAIL despite a CRITICAL secret and a missing AC."
    return None


def assert_secret_is_critical(response: str):
    """Hardcoded secret must be classified as CRITICAL."""
    text = response.upper()
    critical_pos = text.find("CRITICAL")
    secret_pos   = text.lower().find("hardcoded") or text.lower().find("token")
    if critical_pos == -1:
        return "Hardcoded secret was not classified as CRITICAL."
    return None


def assert_secret_references_file_and_line(response: str):
    """Must cite the exact location: export.py:47."""
    if "export.py:47" not in response and "export.py line 47" not in response.lower():
        return (
            "Reviewer mentioned the hardcoded secret but did not cite "
            "the exact location (src/routes/export.py:47)."
        )
    return None


def assert_missing_rate_limit_flagged(response: str):
    """Must flag that the rate limiting AC was not implemented."""
    text = response.lower()
    mentions_rate = "rate limit" in text or "rate-limit" in text or "rate_limit" in text
    if not mentions_rate:
        return "Reviewer did not flag the missing rate-limiting AC."
    return None


def assert_no_enhancement_suggestions(response: str):
    """Reviewer must not suggest enhancements — only check against the spec."""
    text = response.lower()
    enhancement_phrases = [
        "you could also", "consider adding", "it would be nice",
        "in the future", "enhancement", "improvement",
    ]
    for phrase in enhancement_phrases:
        if phrase in text:
            return (
                f"Reviewer made an enhancement suggestion ({phrase!r}). "
                "Reviewer must only check against the spec — no gold-plating."
            )
    return None


assertions = [
    assert_fail_verdict,
    assert_secret_is_critical,
    assert_secret_references_file_and_line,
    assert_missing_rate_limit_flagged,
    assert_no_enhancement_suggestions,
]
