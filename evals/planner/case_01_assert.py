"""
Assertions for planner case_01.
Each function takes the full response string and returns:
  None   — assertion passed
  str    — assertion failed (the string is the failure message)
"""

import re


def assert_question_count(response: str):
    """Planner must ask at most 5 questions."""
    # Count numbered questions in the response
    questions = re.findall(r'^\s*\d+\.\s+.+\?', response, re.MULTILINE)
    if len(questions) > 5:
        return f"Planner asked {len(questions)} questions. Maximum is 5."
    return None


def assert_no_state_management_question(response: str):
    """Must not ask about state management — ADR-001 already decided Riverpod."""
    bad_phrases = [
        "which state management",
        "what state management",
        "provider or riverpod",
        "riverpod or provider",
        "bloc or",
        "or bloc",
        "state management library",
        "state management solution",
    ]
    for phrase in bad_phrases:
        if phrase in response.lower():
            return (
                f"Planner asked about state management: {phrase!r}. "
                "This is answered by ADR-001 and should not be re-asked."
            )
    return None


def assert_has_falsifiable_ac(response: str):
    """ACs must be testable/falsifiable — not vague."""
    vague_patterns = [
        r"- \[ \] (the app|app) (should |must |will )?work",
        r"- \[ \] (everything|all features?) (should |must )?function",
        r"- \[ \] user (can|should) (use|access) (the )?login",
    ]
    for pattern in vague_patterns:
        if re.search(pattern, response, re.IGNORECASE):
            return f"Acceptance criterion appears vague/non-testable: matched pattern {pattern!r}"
    return None


def assert_out_of_bounds_is_specific(response: str):
    """Out of Bounds must not be vague generalities."""
    # Find the Out of Bounds section
    match = re.search(r'## Out of Bounds\s*(.*?)(?=##|\Z)', response, re.DOTALL)
    if not match:
        return "No ## Out of Bounds section found"

    oob_content = match.group(1).strip()
    if not oob_content or oob_content == "-":
        return "Out of Bounds section is empty — must list explicit exclusions"

    vague = ["other screens", "other features", "everything else", "nothing else"]
    for v in vague:
        if v in oob_content.lower():
            return (
                f"Out of Bounds contains vague item: {v!r}. "
                "Must reference specific files or behaviours."
            )
    return None


def assert_adr_applied(response: str):
    """Must reference ADR-001 in ## Architectural Decisions Applied."""
    match = re.search(r'## Architectural Decisions Applied\s*(.*?)(?=##|\Z)', response, re.DOTALL)
    if not match:
        # May not have this section if no ADRs. But ADR-001 exists in context.
        if "ADR-001" not in response:
            return "ADR-001 (Riverpod state management) was not referenced anywhere in the spec."
    else:
        section = match.group(1)
        if "ADR-001" not in section and "ADR-001" not in response:
            return "ADR-001 must appear in ## Architectural Decisions Applied"
    return None


def assert_no_code_written(response: str):
    """Planner must not write implementation code."""
    # Check for code blocks containing Dart or Python syntax
    code_blocks = re.findall(r'```(?:dart|python|kotlin|swift)?\s*(.*?)```', response, re.DOTALL)
    for block in code_blocks:
        # Allow YAML/config-style blocks but not implementation code
        if any(kw in block for kw in ["void main(", "class ", "def ", "import ", "Widget "]):
            return "Planner wrote implementation code. Planner must only write specs."
    return None


# Register all assertions — the eval runner calls these
assertions = [
    assert_question_count,
    assert_no_state_management_question,
    assert_has_falsifiable_ac,
    assert_out_of_bounds_is_specific,
    assert_adr_applied,
    assert_no_code_written,
]
