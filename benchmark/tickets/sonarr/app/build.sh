#!/usr/bin/env bash
# Builds Sonarr at the checked-out commit for Playwright reproductions (D76): backend, frontend, UI beside the binary.
set -euo pipefail
dotnet build src/Sonarr.sln -p:Platform=Posix -v q -nologo >/dev/null
npx --yes yarn@1.22.22 install --frozen-lockfile --ignore-scripts --non-interactive >/dev/null 2>&1
npx --yes yarn@1.22.22 build >/dev/null
rm -rf _output/net10.0/UI && cp -R _output/UI _output/net10.0/UI
