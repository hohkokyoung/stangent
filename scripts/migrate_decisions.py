#!/usr/bin/env python3
"""
Migrate .stangent/decisions.md → .stangent/decisions.json

Parses each ## ADR-NNN section and outputs a compact JSON array.
Run once from the project root: python .stangent/scripts/migrate_decisions.py
"""
import json
import re
import sys
from pathlib import Path

DECISIONS_MD  = Path(".stangent/decisions.md")
DECISIONS_JSON = Path(".stangent/decisions.json")


def parse_decisions(text: str) -> list[dict]:
    # Split on ## ADR- headings
    parts = re.split(r"^(## ADR-\d+.*)", text, flags=re.MULTILINE)
    entries = []

    i = 0
    while i < len(parts):
        heading = parts[i].strip()
        if not heading.startswith("## ADR-"):
            i += 1
            continue
        body = parts[i + 1] if i + 1 < len(parts) else ""
        i += 2

        # Parse heading: ## ADR-NNN — Title
        m = re.match(r"## (ADR-\d+)\s*[—\-]+\s*(.+)", heading)
        if not m:
            continue
        adr_id, title = m.group(1).strip(), m.group(2).strip()

        def extract(label: str) -> str:
            pattern = rf"\*\*{label}:\*\*\s*(.*?)(?=\n\*\*|\Z)"
            m2 = re.search(pattern, body, re.DOTALL)
            return m2.group(1).strip() if m2 else ""

        status_raw = extract("Status") or extract("status")
        decision   = extract("Decision") or extract("decision")
        rationale  = extract("Context") or extract("Rationale") or extract("rationale")
        consequences_raw = extract("Consequences") or extract("consequences")

        # Parse consequences as list (lines starting with - or *)
        consequences = [
            line.lstrip("-* ").strip()
            for line in consequences_raw.splitlines()
            if line.strip().startswith(("-", "*"))
        ] or ([consequences_raw] if consequences_raw else [])

        # Infer applies_to from title/body keywords
        applies_to = []
        body_lower = body.lower() + title.lower()
        if any(k in body_lower for k in ["python", "fastapi", "sqlalchemy", ".py", "pydantic"]):
            applies_to.append("*.py")
        if any(k in body_lower for k in ["flutter", "dart", ".dart"]):
            applies_to.append("*.dart")
        if not applies_to:
            applies_to = ["*"]

        # Extract date
        date_m = re.search(r"\*\*Date:\*\*\s*(\S+)", body)
        created = date_m.group(1) if date_m else ""

        entries.append({
            "id":           adr_id,
            "title":        title,
            "status":       status_raw.lower() if status_raw else "accepted",
            "decision":     decision,
            "rationale":    rationale,
            "consequences": consequences,
            "applies_to":   applies_to,
            "created":      created,
        })

    return entries


def main() -> None:
    if not DECISIONS_MD.exists():
        print("decisions.md not found — nothing to migrate.")
        sys.exit(0)

    text = DECISIONS_MD.read_text(encoding="utf-8")
    entries = parse_decisions(text)

    if not entries:
        print("No ADR sections found in decisions.md. Writing empty array.")

    DECISIONS_JSON.write_text(
        json.dumps(entries, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"Migrated {len(entries)} ADR(s) → {DECISIONS_JSON}")

    # Backup original
    backup = DECISIONS_MD.with_suffix(".md.bak")
    backup.write_text(text, encoding="utf-8")
    print(f"Original backed up → {backup}")


if __name__ == "__main__":
    main()
