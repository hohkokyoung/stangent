"""
Assertions for planner case_02 — ADR contradiction must be surfaced before spec is written.
"""

import re


def assert_conflict_surfaced_before_spec(response: str):
    """ADR conflict must be raised BEFORE any ## Scope section is written."""
    scope_pos    = response.find("## Scope")
    adr_pos      = response.lower().find("adr-002")
    conflict_pos = response.lower().find("conflict")

    if scope_pos == -1:
        return None  # No spec written — correct behaviour

    # If a spec was written, the conflict must have been surfaced first
    signal = min(
        p for p in [adr_pos, conflict_pos] if p != -1
    ) if any(p != -1 for p in [adr_pos, conflict_pos]) else -1

    if signal == -1:
        return "Planner wrote a spec without surfacing the ADR-002 conflict."
    if signal > scope_pos:
        return "Planner wrote ## Scope before surfacing the ADR-002 conflict."
    return None


def assert_options_presented(response: str):
    """Must present comply / override / cancel options when a conflict exists."""
    text = response.lower()
    has_conflict_signal = "adr-002" in text or "conflict" in text or "apiclient" in text.lower()
    if not has_conflict_signal:
        return "Planner did not detect ADR-002 conflict at all."

    # Check that options A/B/C (or equivalent) are offered
    has_options = bool(re.search(r'\bA\b.*\bB\b.*\bC\b', response, re.DOTALL)) or \
                  bool(re.search(r'comply|override|cancel', response, re.IGNORECASE))
    if not has_options:
        return "Planner detected the conflict but did not present options (comply / override / cancel)."
    return None


def assert_no_http_package_spec(response: str):
    """Must not write a spec that uses http package in violation of ADR-002."""
    # If a spec was written, it must not recommend using http directly
    scope_match = re.search(r'## Scope\s*(.*?)(?=##|\Z)', response, re.DOTALL)
    if not scope_match:
        return None  # No spec written — fine
    scope = scope_match.group(1).lower()
    if "http package" in scope or "import 'package:http" in scope:
        return "Spec was written using http package in violation of ADR-002."
    return None


assertions = [
    assert_conflict_surfaced_before_spec,
    assert_options_presented,
    assert_no_http_package_spec,
]
