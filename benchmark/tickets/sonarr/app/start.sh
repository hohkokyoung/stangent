#!/usr/bin/env bash
# Starts Sonarr offline with a fresh data folder and seeded series (D76): no SkyHook metadata calls, no sign-in.
# 1. Start once so Sonarr's migrations create the database, then stop it. 2. Seed it. 3. Start for real (exec: stangent
# stops the whole process tree when the run ends). The first start listens on another port (8990), so nothing waiting for
# 8989 mistakes it for the seeded app (D86).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
data="${SONARR_DATA:-$here/data}"
rm -rf "$data"; mkdir -p "$data/tv/The Test Show" "$data/tv/Second Show"
cat > "$data/config.xml" <<XML
<Config>
  <BindAddress>127.0.0.1</BindAddress>
  <Port>8990</Port>
  <AuthenticationMethod>External</AuthenticationMethod>
  <AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>
  <ApiKey>0123456789abcdef0123456789abcdef</ApiKey>
  <AnalyticsEnabled>False</AnalyticsEnabled>
  <UpdateAutomatically>False</UpdateAutomatically>
  <LaunchBrowser>False</LaunchBrowser>
</Config>
XML
bin="_output/net10.0/Sonarr.dll"
dotnet "$bin" -nobrowser -data="$data" >/dev/null 2>&1 &
first=$!
for _ in $(seq 1 120); do
  curl -sf -o /dev/null -H "X-Api-Key: 0123456789abcdef0123456789abcdef" http://127.0.0.1:8990/api/v3/system/status && break
  sleep 1
done
kill "$first"; wait "$first" 2>/dev/null || true
sed "s|@DATA@|$data|g" "$here/seed.sql" | sqlite3 "$data/sonarr.db"
sed -i.bak 's|<Port>8990</Port>|<Port>8989</Port>|' "$data/config.xml" && rm -f "$data/config.xml.bak"
exec dotnet "$bin" -nobrowser -data="$data"
