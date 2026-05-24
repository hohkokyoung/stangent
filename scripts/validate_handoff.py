#!/usr/bin/env python3
"""
Stangent Handoff Validator

Called by the orchestrator at stage transitions to enforce structural
guarantees before moving to the next pipeline stage.

Usage:
    python validate_handoff.py <feature_file_path> <stage> <config_path>

Stages:
    post_planning      — after planner returns SPEC_WRITTEN
    post_implementing  — after implementer returns IMPLEMENTED
    post_reviewing     — after reviewer returns PASS/FAIL

Exit codes:
    0 — validation passed, safe to transition
    1 — validation failed (errors printed to stdout)
"""
import sys
import re
import json
import subprocess
from pathlib import Path


# ── Parsing helpers ───────────────────────────────────────────────────────────

def parse_frontmatter(content: str) -> dict:
    if not content.startswith("---"):
        return {}
    end = content.find("\n---", 3)
    if end == -1:
        return {}
    fm: dict[str, str] = {}
    for line in content[3:end].splitlines():
        if ":" in line:
            k, _, v = line.partition(":")
            fm[k.strip()] = v.strip()
    return fm


def get_section(content: str, section_name: str) -> str:
    pattern = rf"^## {re.escape(section_name)}\s*\n(.*?)(?=\n## |\Z)"
    m = re.search(pattern, content, re.MULTILINE | re.DOTALL)
    return m.group(1).strip() if m else ""


def section_has_content(content: str, section_name: str) -> bool:
    section = get_section(content, section_name)
    # Strip HTML comments
    section = re.sub(r"<!--.*?-->", "", section, flags=re.DOTALL).strip()
    # Exclude placeholder-only lines
    placeholders = {"-", "- none", "- [ ]"}
    lines = [l.strip() for l in section.splitlines() if l.strip() and l.strip() not in placeholders]
    return bool(lines)


def get_report_status(content: str, section_name: str) -> str:
    """Extract **Status:** VALUE from a sub-agent report section."""
    section = get_section(content, section_name)
    m = re.search(r"\*\*Status:\*\*\s*(\S+)", section)
    return m.group(1) if m else ""


def get_confidence_score(content: str, section_name: str) -> int | None:
    section = get_section(content, section_name)
    m = re.search(r"score:\s*(\d+)", section)
    return int(m.group(1)) if m else None


def get_changed_files(content: str) -> list[tuple[str, str]]:
    """Return [(tag, path), ...] from ## Files Changed. tag = C, M, or D."""
    section = get_section(content, "Files Changed")
    results = []
    for line in section.splitlines():
        m = re.match(r"-\s*\[([CMD])\]\s*(.+)", line.strip())
        if m:
            results.append((m.group(1), m.group(2).strip()))
    return results


_STUB_PATTERNS: list[tuple[str, str]] = [
    (r"raise NotImplementedError",        "raise NotImplementedError"),
    (r"throw UnimplementedError\(\)",     "throw UnimplementedError()"),
    (r"^\s*\.\.\.\s*$",                   "ellipsis-only body (...)"),
    (r"#\s*(?:TODO|FIXME|HACK|stub)\b",   "TODO/FIXME/stub comment"),
]

_BARE_PASS_RE = re.compile(
    r"(?:def|async def)\s+\w+[^:]*:\s*\n\s+pass\s*\n", re.MULTILINE
)


def check_stubs(
    changed_files: list[tuple[str, str]],
    project_root: Path,
) -> list[str]:
    """
    Return warning strings for each stub pattern found in [C] and [M] files.
    Skips files that don't exist or are not source files.
    """
    source_extensions = {
        ".py", ".dart", ".ts", ".tsx", ".js", ".jsx",
        ".go", ".java", ".kt", ".rb", ".swift",
    }
    warnings: list[str] = []

    for tag, rel_path in changed_files:
        if tag == "D":
            continue
        # path may be absolute or relative to project_root
        path = Path(rel_path)
        if not path.is_absolute():
            path = project_root / rel_path
        if not path.exists() or path.suffix not in source_extensions:
            continue
        try:
            src = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue

        hits: list[str] = []
        for pattern, label in _STUB_PATTERNS:
            if re.search(pattern, src, re.MULTILINE):
                hits.append(label)
        if _BARE_PASS_RE.search(src):
            hits.append("bare `pass` function body")

        if hits:
            warnings.append(
                f"{rel_path} — possible stub(s) detected: {', '.join(hits)}"
            )

    return warnings


# ── Stage validators ──────────────────────────────────────────────────────────

def validate_post_planning(
    content: str,
    fm: dict,
    feature_file_path: Path,
    config: dict,
) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []

    tier = (fm.get("tier") or "standard").strip().lower()

    # Required spec sections (both tiers)
    for section in ["Scope", "Out of Bounds", "Files to Touch"]:
        if not section_has_content(content, section):
            errors.append(f"## {section} is empty — planner must populate this section")

    # Acceptance Criteria: at least one real checkbox item
    ac_section = get_section(content, "Acceptance Criteria")
    ac_items = re.findall(r"- \[[ x]\]", ac_section)
    if not ac_items:
        errors.append("## Acceptance Criteria has no items — add at least one testable criterion")

    # Standard-tier-only sections — Direct tier intentionally omits these
    if tier == "standard":
        # ## Codebase Context should be populated for standard features
        if not section_has_content(content, "Codebase Context"):
            warnings.append(
                "## Codebase Context is empty for a standard-tier feature — "
                "implementer will fall back to Pass 1/2 scan (more tokens)"
            )

    # Contract file must exist and be valid (both tiers)
    feature_id = fm.get("id", "")
    if feature_id:
        contract_path = feature_file_path.parent.parent / "contracts" / f"{feature_id}.json"
        if not contract_path.exists():
            errors.append(
                f"Contract file missing: {contract_path} "
                f"— planner must write this in Phase 4.5 (D5 in Direct Mode)"
            )
        else:
            try:
                contract = json.loads(contract_path.read_text(encoding="utf-8"))
                for key in ("feature_id", "allowed_paths", "blocked_paths", "allowed_agents"):
                    if key not in contract:
                        errors.append(f"Contract JSON missing required key: '{key}'")
                if not contract.get("allowed_paths"):
                    errors.append(
                        "Contract allowed_paths is empty — no files scoped for this feature"
                    )
            except json.JSONDecodeError as e:
                errors.append(f"Contract JSON is invalid: {e}")
    else:
        errors.append("Feature frontmatter missing 'id' field")

    # Confidence check (skipped for direct tier — no Planner Confidence section written)
    if tier == "standard":
        threshold = config.get("confidence_thresholds", {}).get("planner", 70)
        score = get_confidence_score(content, "Planner Confidence")
        if score is not None and score < threshold:
            warnings.append(
                f"Planner confidence score {score} is below threshold {threshold} — "
                f"review flags in ## Planner Confidence before proceeding"
            )

    return errors, warnings


def validate_post_implementing(
    content: str,
    fm: dict,
    feature_file_path: Path,
    config: dict,
) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []

    # All AC checkboxes must be checked
    ac_section = get_section(content, "Acceptance Criteria")
    unchecked = re.findall(r"- \[ \]", ac_section)
    if unchecked:
        errors.append(
            f"{len(unchecked)} acceptance criterion/criteria still unchecked — "
            f"implementer must mark all ACs [x] before handoff"
        )

    # Files Changed must have actual entries
    changed_files = get_changed_files(content)
    if not changed_files:
        errors.append(
            "## Files Changed has no entries — implementer must record all changed files"
        )

    # Content check: stub detection in created/modified source files
    if changed_files:
        project_root = feature_file_path.parent.parent.parent
        stub_warnings = check_stubs(changed_files, project_root)
        for w in stub_warnings:
            warnings.append(f"Possible stub in implementation — {w}")

    # At least one test file must be in Files Changed
    test_tags = [
        (tag, p) for tag, p in changed_files
        if tag in ("C", "M") and (
            "test" in p.lower()
            or "spec" in p.lower()
            or "_test." in p
            or ".test." in p
        )
    ]
    if changed_files and not test_tags:
        warnings.append(
            "No test files found in ## Files Changed — "
            "verify tests were written for the new functionality"
        )

    # Sub-agent reports must not be PENDING or FAIL
    linter_status = get_report_status(content, "Linter Report")
    if linter_status == "FAIL":
        errors.append(
            "## Linter Report status is FAIL — fix all linter issues before handing off"
        )
    elif linter_status in ("PENDING", ""):
        errors.append(
            "## Linter Report status is PENDING — linter sub-agent was not run"
        )

    test_status = get_report_status(content, "Test Report")
    if test_status == "FAIL":
        errors.append(
            "## Test Report status is FAIL — fix failing tests before handing off"
        )
    elif test_status in ("PENDING", ""):
        errors.append(
            "## Test Report status is PENDING — unit tester sub-agent was not run"
        )

    query_status = get_report_status(content, "Query Analysis Report")
    if query_status == "FAIL":
        errors.append(
            "## Query Analysis Report status is FAIL — fix all DANGER query findings before handing off"
        )

    # At least one git commit must exist on this branch
    try:
        result = subprocess.run(
            ["git", "log", "--oneline", "-1"],
            capture_output=True, text=True, timeout=10,
        )
        if not result.stdout.strip():
            errors.append(
                "No git commit found on this branch — implementer must commit before handing off"
            )
    except Exception:
        pass  # git unavailable — skip this check

    # Confidence check
    threshold = config.get("confidence_thresholds", {}).get("implementer", 75)
    score = get_confidence_score(content, "Implementer Confidence")
    if score is not None and score < threshold:
        warnings.append(
            f"Implementer confidence score {score} is below threshold {threshold} — "
            f"review flags in ## Implementer Confidence before proceeding"
        )

    return errors, warnings


def validate_post_reviewing(
    content: str,
    fm: dict,
    feature_file_path: Path,
    config: dict,
) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []

    # Review Verdict must contain a machine-readable Overall line
    verdict_section = get_section(content, "Review Verdict")
    overall_match = re.search(r"\*\*Overall:\*\*\s*(PASS|FAIL)", verdict_section)
    if not overall_match:
        errors.append(
            '## Review Verdict must contain "**Overall:** PASS" or "**Overall:** FAIL" — '
            "reviewer output is not machine-readable by the orchestrator"
        )

    # Content check: Review Checklist must have checked items
    checklist_section = get_section(content, "Review Checklist")
    checked_count = len(re.findall(r"- \[x\]", checklist_section, re.IGNORECASE))
    min_items = config.get("pipeline", {}).get("min_review_checklist_items", 5)
    if checked_count == 0:
        errors.append(
            "## Review Checklist has no checked items — "
            "reviewer must complete the checklist before issuing a verdict"
        )
    elif overall_match and overall_match.group(1) == "PASS" and checked_count < min_items:
        errors.append(
            f"## Review Checklist has only {checked_count} checked item(s) — "
            f"minimum {min_items} required for a PASS verdict "
            f"(configure via pipeline.min_review_checklist_items)"
        )

    # reviewer_agent_version must be set in frontmatter
    if not fm.get("reviewer_agent_version", "").strip():
        errors.append(
            "reviewer_agent_version is not set in frontmatter — reviewer did not update it"
        )

    # Confidence check
    threshold = config.get("confidence_thresholds", {}).get("reviewer", 80)
    score = get_confidence_score(content, "Reviewer Confidence")
    if score is not None and score < threshold:
        warnings.append(
            f"Reviewer confidence score {score} is below threshold {threshold} — "
            f"review flags in ## Reviewer Confidence before proceeding"
        )

    return errors, warnings


# ── Main ─────────────────────────────────────────────────────────────────────

VALIDATORS = {
    "post_planning":     validate_post_planning,
    "post_implementing": validate_post_implementing,
    "post_reviewing":    validate_post_reviewing,
}


def main() -> None:
    if len(sys.argv) < 4:
        print("Usage: validate_handoff.py <feature_file_path> <stage> <config_path>")
        sys.exit(1)

    feature_file_path = Path(sys.argv[1])
    stage             = sys.argv[2]
    config_path       = Path(sys.argv[3])

    if not feature_file_path.exists():
        print(f"[Handoff] ERROR: feature file not found: {feature_file_path}")
        sys.exit(1)

    if stage not in VALIDATORS:
        print(f"[Handoff] Unknown stage: {stage!r}. Valid stages: {list(VALIDATORS)}")
        sys.exit(1)

    content = feature_file_path.read_text(encoding="utf-8")
    fm      = parse_frontmatter(content)

    config: dict = {}
    if config_path.exists():
        try:
            config = json.loads(config_path.read_text(encoding="utf-8"))
        except Exception:
            pass

    errors, warnings = VALIDATORS[stage](content, fm, feature_file_path, config)

    for w in warnings:
        print(f"[Handoff] WARN  {w}")

    if errors:
        print(f"\n[Handoff] BLOCKED — {stage} validation failed ({len(errors)} error(s)):\n")
        for i, e in enumerate(errors, 1):
            print(f"  {i}. {e}")
        print("\nFix these issues before the pipeline can advance.")
        sys.exit(1)

    if warnings:
        print(f"[Handoff] PASS (with warnings) — {stage} validation passed")
    else:
        print(f"[Handoff] PASS — {stage} validation passed")
    sys.exit(0)


if __name__ == "__main__":
    main()
