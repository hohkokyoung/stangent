#!/usr/bin/env bash
# Measures line coverage (D28: a number to watch, no floor, no gate). Runs each test project with Microsoft's coverage
# extension, then prints line coverage per Stangent.* assembly and a total, test assemblies left out. A line counts as
# covered if any test project ran it. A product no test loads shows as "not loaded", not as a silent gap.
#
# usage: scripts/coverage.sh
# out:   the table on stdout; the Cobertura reports stay in artifacts/coverage/ (git-ignored)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/coverage"
rm -rf "$out"
mkdir -p "$out"
for project in "$root"/tests/*.Tests/; do
  name="$(basename "$project")"
  dotnet test --project "$project" --results-directory "$out/$name" \
    --coverage --coverage-output-format cobertura --coverage-output "$name.cobertura.xml" >"$out/$name.log" 2>&1 \
    || { echo "tests failed in $name: see $out/$name.log" >&2; exit 1; }
done

python3 - "$root" "$out" <<'PY'
import glob, os, sys
import xml.etree.ElementTree as ET

root, out = sys.argv[1:]
lines = {}  # assembly -> (file, line) -> ran
for report in glob.glob(os.path.join(out, "*", "*.cobertura.xml")):
    for package in ET.parse(report).getroot().iter("package"):
        name = package.get("name")
        if not name.startswith("Stangent.") or name.endswith(".Tests"):
            continue
        seen = lines.setdefault(name, {})
        for cls in package.iter("class"):
            for line in cls.iter("line"):
                key = (cls.get("filename"), line.get("number"))
                seen[key] = seen.get(key, False) or int(line.get("hits")) > 0

products = {d for d in os.listdir(os.path.join(root, "src")) if d.startswith("Stangent.")}
total_ran = total = 0
for name in sorted(products | set(lines)):
    if name not in lines:
        print(f"{name:24} not loaded by any test")
        continue
    ran, count = sum(lines[name].values()), len(lines[name])
    total_ran += ran
    total += count
    print(f"{name:24} {100 * ran / count:5.1f}%  ({ran}/{count} lines)")
print(f"{'Total':24} {100 * total_ran / total:5.1f}%  ({total_ran}/{total} lines)")
PY
