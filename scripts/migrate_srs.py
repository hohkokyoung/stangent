#!/usr/bin/env python3
"""
Migrate .stangent/SRS.md → .stangent/srs.jsonl

Extracts per-feature subsections from ## 3. Functional Requirements
and writes one compact JSONL line per COMPLETE feature.
Run once from the project root: python .stangent/scripts/migrate_srs.py
"""
import json
import re
import sys
from pathlib import Path

SRS_MD   = Path(".stangent/SRS.md")
SRS_JSONL = Path(".stangent/srs.jsonl")


def parse_functional_requirements(text: str) -> list[dict]:
    # Find the Functional Requirements section
    fr_match = re.search(
        r"^## 3\. Functional Requirements(.*?)(?=^## \d+\.|\Z)",
        text, re.MULTILINE | re.DOTALL,
    )
    if not fr_match:
        return []

    fr_body = fr_match.group(1)

    # Split on subsection headings: ### 3.N [FEAT-XXX] Title
    parts = re.split(r"^(### [\d.]+ \[FEAT-\d+\].*)", fr_body, flags=re.MULTILINE)
    entries = []

    i = 0
    while i < len(parts):
        heading = parts[i].strip()
        if not re.match(r"### [\d.]+ \[FEAT-\d+\]", heading):
            i += 1
            continue
        body = parts[i + 1] if i + 1 < len(parts) else ""
        i += 2

        m = re.search(r"\[(FEAT-\d+)\]\s*(.+)", heading)
        if not m:
            continue
        feat_id = m.group(1).strip()
        title   = m.group(2).strip()

        def extract_field(label: str) -> str:
            p = rf"\*\*{label}:\*\*\s*(.*?)(?=\n\*\*|\Z)"
            m2 = re.search(p, body, re.DOTALL)
            return m2.group(1).strip() if m2 else ""

        scope = extract_field("Scope")
        date  = extract_field("Date")

        # ACs: lines starting with - [x]
        acs = re.findall(r"-\s*\[x\]\s*(.+)", body, re.IGNORECASE)

        # Files
        files_raw = extract_field("Files")
        files = [
            line.lstrip("-* ").strip()
            for line in files_raw.splitlines()
            if line.strip()
        ]

        entries.append({
            "feat_id":       feat_id,
            "title":         title,
            "scope":         scope,
            "acs":           acs,
            "api_contracts": [],   # not extracted from old format
            "data_models":   [],
            "env_vars":      [],
            "security_summary": "migrated",
            "files":         files,
            "updated":       date,
        })

    return entries


def main() -> None:
    if not SRS_MD.exists():
        print("SRS.md not found — nothing to migrate.")
        sys.exit(0)

    text = SRS_MD.read_text(encoding="utf-8")
    entries = parse_functional_requirements(text)

    if not entries:
        print("No [FEAT-XXX] subsections found in SRS.md. Writing empty srs.jsonl.")

    with SRS_JSONL.open("w", encoding="utf-8") as f:
        for entry in entries:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")

    print(f"Migrated {len(entries)} feature(s) → {SRS_JSONL}")

    # Backup original
    backup = SRS_MD.with_suffix(".md.bak")
    backup.write_text(text, encoding="utf-8")
    print(f"Original backed up → {backup}")


if __name__ == "__main__":
    main()
