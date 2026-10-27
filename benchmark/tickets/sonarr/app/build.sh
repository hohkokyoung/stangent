#!/usr/bin/env bash
# Builds Sonarr at the checked-out commit for Playwright reproductions (D76): backend, frontend, UI beside the binary.
# NuGetAudit off, as in tickets.dotnetArgs: old commits pin packages with advisories published since, and Sonarr turns
# warnings into errors. On a failure the end of the step's output goes to stderr, so stangent can report it.
set -euo pipefail
log="$(mktemp -t stangent-app-build)"
trap 'rm -f "$log"' EXIT
step() { if ! "$@" >"$log" 2>&1; then echo "build step failed: $*" >&2; tail -n 30 "$log" >&2; exit 1; fi; }
step dotnet build src/Sonarr.sln -p:Platform=Posix -p:NuGetAudit=false -v q -nologo
step npx --yes yarn@1.22.22 install --frozen-lockfile --ignore-scripts --non-interactive
step npx --yes yarn@1.22.22 build
rm -rf _output/net10.0/UI && cp -R _output/UI _output/net10.0/UI
