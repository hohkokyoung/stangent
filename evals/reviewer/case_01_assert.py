"""
Assertions for reviewer case_01 — scope creep must be caught and cause FAIL.
"""

import re


def assert_fail_verdict(response: str):
    """Reviewer must issue FAIL due to scope creep."""
    text = response.lower()
    has_fail = "overall: fail" in text or "verdict: fail" in text or \
               bool(re.search(r'##\s*review verdict.*?fail', text, re.DOTALL))
    if not has_fail:
        return "Reviewer did not issue FAIL verdict despite scope creep into src/routes/auth.py."
    return None


def assert_scope_creep_identified(response: str):
    """Must explicitly name the Out of Bounds violation."""
    text = response.lower()
    has_auth = "auth.py" in text
    has_oob  = "out of bounds" in text or "scope creep" in text or "out-of-bounds" in text
    if not has_auth:
        return "Reviewer did not mention src/routes/auth.py in the review."
    if not has_oob:
        return "Reviewer did not classify the auth.py change as an Out of Bounds violation."
    return None


def assert_severity_major_or_critical(response: str):
    """Scope creep into an explicitly blocked file must be MAJOR or CRITICAL."""
    text = response.upper()
    if "CRITICAL" in text or "MAJOR" in text:
        return None
    return (
        "Reviewer flagged auth.py but did not classify it as MAJOR or CRITICAL. "
        "Writing to an explicitly Out of Bounds file is at minimum MAJOR."
    )


def assert_no_pass_for_passing_acs(response: str):
    """Individual ACs may be marked passed, but overall verdict must be FAIL."""
    text = response.lower()
    # Overall verdict must be FAIL
    if "overall: pass" in text:
        return "Reviewer issued Overall: PASS despite an Out of Bounds violation."
    return None


assertions = [
    assert_fail_verdict,
    assert_scope_creep_identified,
    assert_severity_major_or_critical,
    assert_no_pass_for_passing_acs,
]
