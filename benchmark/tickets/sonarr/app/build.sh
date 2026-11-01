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
# The app's folder is per target framework (net8.0 on older commits, net10.0 later); run.sh clears _output between
# commits, so exactly one is here. Two would mean a stale build: fail rather than serve another commit's app.
tfm=(); for f in _output/net*/Sonarr.dll; do [ -e "$f" ] && [[ "$f" != *-windows/* ]] && tfm+=("$(dirname "$f")"); done
[ ${#tfm[@]} -eq 1 ] || { echo "build step failed: expected one _output/net*/Sonarr.dll, found ${#tfm[@]}" >&2; exit 1; }
rm -rf "${tfm[0]}/UI" && cp -R _output/UI "${tfm[0]}/UI"
