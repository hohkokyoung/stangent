#!/usr/bin/env bash
# Installs the Playwright app setting into a Sonarr clone (D76): untracked files, so checking out other commits keeps them.
#   .stangent-app/   build.sh, start.sh, seed.sql (tickets.app.build / start)
#   e2e/             package.json, playwright.config.ts, tests/ (two seeded specs, D86), stangent/ (where reproductions go),
#                    Playwright + Chromium
# usage: setup.sh <clone>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
clone="$(cd "$1" && pwd)"
mkdir -p "$clone/.stangent-app" "$clone/e2e/stangent"
cp "$here/build.sh" "$here/start.sh" "$here/seed.sql" "$clone/.stangent-app/"
cp "$here/e2e/package.json" "$here/e2e/playwright.config.ts" "$clone/e2e/"
mkdir -p "$clone/e2e/tests" && cp "$here/e2e/tests/"*.spec.ts "$clone/e2e/tests/"   # the two seeded specs (D86)
for p in .stangent-app/ e2e/; do grep -qxF "$p" "$clone/.git/info/exclude" || echo "$p" >> "$clone/.git/info/exclude"; done
(cd "$clone/e2e" && npm install --no-audit --no-fund >/dev/null && npx playwright install chromium >/dev/null)
echo "installed in $clone"
