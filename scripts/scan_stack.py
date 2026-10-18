#!/usr/bin/env python3
"""List the NuGet and npm packages a repo uses, grouped by the link detector they need.

A throwaway helper for choosing stangent's first link detectors (decisions.md, D17).
Standard library only; reads project files, never runs or restores anything.

    python3 scripts/scan_stack.py /path/to/repo
"""

import json
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

SKIP_DIRS = {"node_modules", "bin", "obj", ".git", "dist", "wwwroot", "lib"}

# (detector category, package-name prefix). First match wins; prefixes are case-insensitive.
CATEGORIES = [
    ("Web/API: gRPC", "Grpc."),
    ("Web/API: SignalR", "Microsoft.AspNetCore.SignalR"),
    ("Web/API: ASP.NET Core", "Microsoft.AspNetCore."),
    ("Wiring: Autofac", "Autofac"),
    ("Wiring: Scrutor", "Scrutor"),
    ("In-process messaging: MediatR", "MediatR"),
    ("Between services: MassTransit", "MassTransit"),
    ("Between services: NServiceBus", "NServiceBus"),
    ("Between services: Azure Service Bus", "Azure.Messaging.ServiceBus"),
    ("Between services: RabbitMQ", "RabbitMQ.Client"),
    ("Between services: Kafka", "Confluent.Kafka"),
    ("Between services: Refit", "Refit"),
    ("Between services: HttpClient factory", "Microsoft.Extensions.Http"),
    ("Database: EF Core", "Microsoft.EntityFrameworkCore"),
    ("Database: Dapper", "Dapper"),
    ("Database: linq2db", "linq2db"),
    ("Database: FluentMigrator", "FluentMigrator"),
    ("API clients: NSwag", "NSwag"),
    ("Frontend: Angular", "@angular/"),
    ("Frontend: React", "react"),
    ("Frontend: React Query", "@tanstack/react-query"),
    ("Frontend: axios", "axios"),
    ("API clients: OpenAPI generator", "@openapitools/"),
    ("Frontend: jQuery", "jquery"),
]


def walk(root, name_pattern):
    for path in root.rglob(name_pattern):
        if not SKIP_DIRS.intersection(path.relative_to(root).parts[:-1]):
            yield path


def nuget_packages(root):
    """Yield (package, project) from .csproj PackageReference/FrameworkReference and Web SDKs."""
    for proj in walk(root, "*.csproj"):
        try:
            tree = ET.parse(proj)
        except ET.ParseError:
            print(f"warning: could not parse {proj}", file=sys.stderr)
            continue
        rel = str(proj.relative_to(root))
        sdk = tree.getroot().get("Sdk", "")
        if sdk.startswith("Microsoft.NET.Sdk.Web"):
            yield "Microsoft.AspNetCore.App (Web SDK)", rel
        for el in tree.iter():
            tag = el.tag.split("}")[-1]
            if tag in ("PackageReference", "FrameworkReference") and el.get("Include"):
                yield el.get("Include"), rel


def npm_packages(root):
    for manifest in walk(root, "package.json"):
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            print(f"warning: could not parse {manifest}", file=sys.stderr)
            continue
        rel = str(manifest.relative_to(root))
        for section in ("dependencies", "devDependencies"):
            for name in data.get(section, {}):
                yield name, rel


def category(package):
    lower = package.lower()
    for name, prefix in CATEGORIES:
        p = prefix.lower()
        if p.endswith((".", "/")):
            hit = lower.startswith(p)
        else:  # whole name or a sub-package: "react", "react-dom", "dapper.contrib" — not "reactive"
            hit = lower == p or lower.startswith((p + ".", p + "-"))
        if hit:
            return name
    return None


def main():
    if len(sys.argv) != 2 or not Path(sys.argv[1]).is_dir():
        sys.exit("usage: scan_stack.py /path/to/repo")
    root = Path(sys.argv[1]).resolve()

    found = defaultdict(lambda: defaultdict(set))  # category -> package -> projects
    other = defaultdict(set)
    counts = {"nuget": 0, "npm": 0}
    for kind, source in (("nuget", nuget_packages), ("npm", npm_packages)):
        for package, project in source(root):
            counts[kind] += 1
            cat = category(package)
            (found[cat][package] if cat else other[package]).add(project)

    projects = len(list(walk(root, "*.csproj")))
    manifests = len(list(walk(root, "package.json")))
    print(f"# Stack scan: {root.name}\n")
    print(f"{projects} .csproj, {manifests} package.json, "
          f"{counts['nuget']} NuGet and {counts['npm']} npm references\n")
    print("## Packages that need a link detector\n")
    if not found:
        print("(none recognised)\n")
    for cat in sorted(found):
        print(f"- **{cat}**")
        for package, used_in in sorted(found[cat].items()):
            print(f"  - `{package}` in {len(used_in)} project(s)")
    print("\n## Everything else (check for in-house frameworks)\n")
    for package, used_in in sorted(other.items(), key=lambda kv: (-len(kv[1]), kv[0].lower())):
        print(f"- `{package}` ({len(used_in)})")


if __name__ == "__main__":
    main()
