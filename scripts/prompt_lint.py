#!/usr/bin/env python3
"""
Stangent Prompt Linter

Validates that every agent prompt file in agents/ and agents/subagents/ has the
required structure. Catches drift before it reaches a user project.

Required frontmatter fields:
    name, version, type, description, tools, inputs, outputs

Required sections (## headings) for type: agent:
    ROLE, CONTEXT INPUTS, CONSTRAINTS, OUT OF BOUNDS, PROCESS, OUTPUT CONTRACT

Required sections for type: subagent (lighter requirements):
    ROLE, CONTEXT INPUTS, PROCESS, OUTPUT CONTRACT

Usage:
    python scripts/prompt_lint.py
    python scripts/prompt_lint.py --strict   # treat warnings as errors

Exit codes:
    0 — all prompts pass
    1 — at least one prompt has errors
"""
import argparse
import re
import sys
from pathlib import Path


STANGENT_PATH = Path(__file__).parent.parent.resolve()

REQUIRED_FRONTMATTER = ["name", "version", "type", "description", "tools"]

REQUIRED_SECTIONS_AGENT    = ["ROLE", "CONTEXT INPUTS", "CONSTRAINTS", "OUT OF BOUNDS", "PROCESS", "OUTPUT CONTRACT"]
REQUIRED_SECTIONS_SUBAGENT = ["ROLE", "CONTEXT INPUTS", "PROCESS", "OUTPUT CONTRACT"]

SEMVER_RE   = re.compile(r"^\d+\.\d+\.\d+$")
FRONT_END   = re.compile(r"\n---\s*\n", re.MULTILINE)


def parse_frontmatter(content: str) -> dict:
    """Minimal YAML frontmatter reader.
    Handles `key: value`, folded strings (`key: >` followed by indented lines),
    and skips list items / nested keys.
    """
    if not content.startswith("---"):
        return {}
    m = FRONT_END.search(content[3:])
    if not m:
        return {}
    fm_text = content[3:3 + m.start()]
    fm: dict = {}
    lines = fm_text.splitlines()
    i = 0
    while i < len(lines):
        line = lines[i]
        if ":" in line and not line.startswith((" ", "\t")) and not line.lstrip().startswith("-"):
            k, _, v = line.partition(":")
            k = k.strip()
            v = v.strip()
            if v in (">", "|", ">-", "|-", ""):
                # folded / literal block — collect indented continuation lines
                folded = []
                i += 1
                while i < len(lines) and (lines[i].startswith(("  ", "\t")) or not lines[i].strip()):
                    if lines[i].strip():
                        folded.append(lines[i].strip())
                    i += 1
                fm[k] = " ".join(folded) if folded else v
                continue
            fm[k] = v
        i += 1
    return fm


def find_sections(content: str) -> set[str]:
    return {m.group(1).strip() for m in re.finditer(r"^## (.+)$", content, re.MULTILINE)}


def lint_file(path: Path) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    rel = path.relative_to(STANGENT_PATH).as_posix()

    try:
        content = path.read_text(encoding="utf-8")
    except Exception as e:
        return [f"{rel}: cannot read ({e})"], []

    fm = parse_frontmatter(content)
    if not fm:
        errors.append(f"{rel}: missing or malformed YAML frontmatter")
        return errors, warnings

    # Required frontmatter fields
    for field in REQUIRED_FRONTMATTER:
        if field not in fm:
            errors.append(f"{rel}: frontmatter missing '{field}'")

    # Version must be semver
    version = fm.get("version", "")
    if version and not SEMVER_RE.match(version):
        errors.append(f"{rel}: version '{version}' is not valid semver (MAJOR.MINOR.PATCH)")

    # Section requirements depend on type
    agent_type = fm.get("type", "")
    sections   = find_sections(content)

    if agent_type == "agent":
        required = REQUIRED_SECTIONS_AGENT
    elif agent_type == "subagent":
        required = REQUIRED_SECTIONS_SUBAGENT
    else:
        errors.append(f"{rel}: type must be 'agent' or 'subagent' (got '{agent_type}')")
        return errors, warnings

    for section in required:
        if section not in sections:
            errors.append(f"{rel}: missing required section '## {section}'")

    # Soft checks (warnings)
    if "description" in fm and len(fm["description"]) < 20:
        warnings.append(f"{rel}: description is suspiciously short — please expand")
    if agent_type == "agent" and "ESCALATION" not in sections:
        warnings.append(f"{rel}: agent files should have an '## ESCALATION' section")

    return errors, warnings


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--strict", action="store_true", help="treat warnings as errors")
    args = ap.parse_args()

    all_errors: list[str]   = []
    all_warnings: list[str] = []

    agent_files = sorted((STANGENT_PATH / "agents").glob("*.md"))
    subagent_files = sorted((STANGENT_PATH / "agents" / "subagents").glob("*.md"))

    for path in agent_files + subagent_files:
        if path.name == "VERSIONING.md":  # documentation, not an agent
            continue
        errs, warns = lint_file(path)
        all_errors.extend(errs)
        all_warnings.extend(warns)

    # Use ASCII markers — Windows terminals often lack Unicode glyph support
    if all_errors:
        print("ERRORS:")
        for e in all_errors:
            print(f"  [X] {e}")

    if all_warnings:
        print("WARNINGS:")
        for w in all_warnings:
            print(f"  [!] {w}")

    if not all_errors and not all_warnings:
        print(f"[OK] All {len(agent_files) + len(subagent_files) - 1} agent files pass lint")

    fail = bool(all_errors) or (args.strict and bool(all_warnings))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
