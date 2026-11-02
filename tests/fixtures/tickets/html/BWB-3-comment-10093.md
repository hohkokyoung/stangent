_syntegris-sec wrote:_

We experience the same problem. We have a payed account.
We get an HTTP 500 on the billing/subscription page.
This occured after an updateself follow by update.

**Our instance:**

- self hosted on Debian 12.10
- Docker version 27.5.1
- MS SQL database

**Docker images in use:**
ghcr.io/bitwarden/nginx:2025.3.3
ghcr.io/bitwarden/admin:2025.3.3
ghcr.io/bitwarden/attachments:2025.3.3
ghcr.io/bitwarden/identity:2025.3.3
ghcr.io/bitwarden/icons:2025.3.3
ghcr.io/bitwarden/notifications:2025.3.3
ghcr.io/bitwarden/api:2025.3.3
ghcr.io/bitwarden/mssql:2025.3.3
ghcr.io/bitwarden/sso:2025.3.3
ghcr.io/bitwarden/events:2025.3.3
ghcr.io/bitwarden/web:2025.3.1

**Error details (chrome devtools):**

api.service.ts:1571
 GET https://bw.syntegris.de/api/organizations/1bd12345-9e2a-41e7-813f-ae9100a12345/subscription 500 (Internal Server Error)
nativeFetch	@	api.service.ts:1571
(anonymous)	@	api.service.ts:1567
o	@	main.7751503….js:3
onInvoke	@	ng_zone.ts:470
onInvokeTask	@	ng_zone.ts:447

A fix would be highly appreciated.
