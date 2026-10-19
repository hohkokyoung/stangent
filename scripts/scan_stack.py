#!/usr/bin/env python3
"""List the NuGet and npm packages a repo uses, grouped by the link detector they need, and how
its frontends call the backend (generated API clients vs hand-written calls).

A throwaway helper for choosing stangent's first link detectors (decisions.md, D17).
Standard library only; reads project files, never runs or restores anything.

    python3 scripts/scan_stack.py /path/to/repo
"""

import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

SKIP_DIRS = {"node_modules", "bin", "obj", ".git", "dist", "wwwroot", "lib"}

# Config files that mean "API clients are generated from the backend's API description".
GENERATOR_FILES = [
    ("NSwag", re.compile(r"(^nswag\.json$|\.nswag$)", re.I)),
    ("OpenAPI Generator", re.compile(r"^openapitools\.json$", re.I)),
    ("orval", re.compile(r"^orval\.config\.(js|cjs|mjs|ts)$", re.I)),
    ("ng-openapi-gen", re.compile(r"^ng-openapi-gen\.json$", re.I)),
    ("@hey-api/openapi-ts", re.compile(r"^openapi-ts\.config\.(js|cjs|mjs|ts)$", re.I)),
    ("Kiota", re.compile(r"^kiota-lock\.json$", re.I)),
    ("AutoRest", re.compile(r"^(readme\.md|autorest\.md)$", re.I)),  # only counted with autorest in package.json
]
GENERATOR_PACKAGES = {
    "nswag": "NSwag", "@openapitools/openapi-generator-cli": "OpenAPI Generator", "orval": "orval",
    "ng-openapi-gen": "ng-openapi-gen", "@hey-api/openapi-ts": "@hey-api/openapi-ts", "openapi-typescript": "openapi-typescript",
    "swagger-typescript-api": "swagger-typescript-api", "autorest": "AutoRest", "@microsoft/kiota-bundle": "Kiota",
}

# Hand-written call styles, counted per source file.
CALL_STYLES = [
    ("Angular HttpClient", re.compile(r"from\s+['\"]@angular/common/http['\"]")),
    ("fetch()", re.compile(r"(?<![\w.])fetch\s*\(")),
    ("axios", re.compile(r"from\s+['\"]axios['\"]|require\(['\"]axios['\"]\)")),
    ("React Query / TanStack Query", re.compile(r"from\s+['\"]@tanstack/react-query['\"]|from\s+['\"]react-query['\"]")),
    ("jQuery $.ajax / $.get / $.post", re.compile(r"\$\.(ajax|get|post|getJSON)\s*\(")),
    ("generated client in use (new XxxClient())", re.compile(r"new\s+[A-Z]\w*Client\s*\(")),
    ("generated client imported", re.compile(r"from\s+['\"][^'\"]*(web-api-client|api-client|generated|openapi)[^'\"]*['\"]", re.I)),
]
SOURCE_EXT = (".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue", ".cshtml")
SKIP_SOURCE = SKIP_DIRS | {"dist", "build", "coverage", ".angular", "out"}

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


def frontend_calls(root, npm_names):
    """Generator configs and packages found, and how many source files use each call style."""
    generators = defaultdict(set)
    styles = defaultdict(set)
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_SOURCE]
        rel_dir = os.path.relpath(dirpath, root)
        for name in filenames:
            rel = os.path.normpath(os.path.join(rel_dir, name))
            for gen, pattern in GENERATOR_FILES:
                if pattern.search(name) and (gen != "AutoRest" or "AutoRest" in npm_names.values()):
                    generators[gen].add(rel)
            if not name.endswith(SOURCE_EXT) or name.endswith((".min.js", ".d.ts")):
                continue
            try:
                text = Path(dirpath, name).read_text(encoding="utf-8", errors="ignore")
            except OSError:
                continue
            for style, pattern in CALL_STYLES:
                if pattern.search(text):
                    styles[style].add(rel)
    for package, gen in npm_names.items():
        generators[gen].add(f"package.json: {package}")
    return generators, styles


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
    npm_generators = {}
    for package, _ in npm_packages(root):
        if package.lower() in GENERATOR_PACKAGES:
            npm_generators[package] = GENERATOR_PACKAGES[package.lower()]
    generators, styles = frontend_calls(root, npm_generators)
    print("\n## How frontends call the backend\n")
    if generators:
        print("**Generated API clients:**")
        for gen in sorted(generators):
            print(f"- **{gen}**: " + ", ".join(f"`{f}`" for f in sorted(generators[gen])[:5]))
    else:
        print("**Generated API clients:** none found")
    print("\n**Hand-written call styles** (files using each):")
    if not styles:
        print("- none found")
    for style in [s for s, _ in CALL_STYLES if s in styles]:
        files = sorted(styles[style])
        print(f"- {style}: {len(files)} file(s), e.g. " + ", ".join(f"`{f}`" for f in files[:3]))

    print("\n## Everything else (check for in-house frameworks)\n")
    for package, used_in in sorted(other.items(), key=lambda kv: (-len(kv[1]), kv[0].lower())):
        print(f"- `{package}` ({len(used_in)})")


if __name__ == "__main__":
    main()
