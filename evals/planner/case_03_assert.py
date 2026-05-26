"""
Assertions for planner case_03 — memory-informed planning.
Planner must use failure patterns and developer preferences from srs.jsonl security_summary / decisions.json.
"""

import re


def assert_sql_risk_flagged(response: str):
    """Must proactively flag SQL injection risk from memory without being asked."""
    text = response.lower()
    signals = ["sql injection", "parameterized", "parameterised", "raw query", "injection risk"]
    if not any(s in text for s in signals):
        return (
            "Planner did not flag the SQL injection risk from srs.jsonl security_summary / decisions.json ## Failure Patterns. "
            "This pattern was in src/models/ which is in scope for this feature."
        )
    return None


def assert_test_location_applied_silently(response: str):
    """Must apply co-located test preference silently — not ask about it."""
    text = response.lower()
    # Check it did not ask about test location
    asked_about_tests = bool(re.search(
        r"where.{0,30}(test|spec)|test.{0,30}(location|directory|folder)|"
        r"(tests/|test directory).{0,30}\?",
        text
    ))
    if asked_about_tests:
        return (
            "Planner asked about test file location, but developer preference "
            "was already recorded in srs.jsonl security_summary / decisions.json. Should apply silently."
        )
    return None


def assert_parameterized_query_in_spec(response: str):
    """Spec must mention parameterized queries given the SQL injection risk."""
    text = response.lower()
    has_safe_query = any(s in text for s in [
        "parameterized", "parameterised", "sqlalchemy", "prepared statement",
        "bind", "no raw sql", "query parameter"
    ])
    # Only fail if spec exists AND there's no safe query mention
    has_spec = "## acceptance criteria" in text
    if has_spec and not has_safe_query:
        return (
            "Spec was written without addressing SQL injection risk. "
            "Given the failure pattern in srs.jsonl security_summary / decisions.json, an AC or constraint about "
            "parameterized queries is expected."
        )
    return None


assertions = [
    assert_sql_risk_flagged,
    assert_test_location_applied_silently,
    assert_parameterized_query_in_spec,
]
