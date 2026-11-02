_CirnoT wrote:_

```
2025-05-18 00:30:00.432 +00:00 [Information] Validating licenses for 1 organizations.
2025-05-18 00:30:00.515 +00:00 [Information] Organization a049ecd1-7035-41b6-ad65-b20e00d23e80 ("i32.pl") has an invalid license and is being disabled. Reason: "Invalid signature."
2025-05-18 00:30:07.749 +00:00 [Information] Finished job "ValidateOrganizationsJob" at 05/18/2025 00:30:07.
```

The likely cause is https://github.com/bitwarden/server/pull/5569 which introduces new property `UseAdminSponsoredFamilies` without updating license version hence making all legacy property-based licenses invalid. @Neonwarden as I understand it, the agreement on introducing claim-based licenses was that legacy licenses still continue to function?

Pinging @cyprain-okeke as author and @cturnbull-bitwarden as reviewer of PR in question. Also pinging @BTreston because their recent PR https://github.com/bitwarden/server/pull/5834 bumps version.

---

For reference, the above log comes from instance running license that was generated on `2024-10-19T12:27:45.6010386Z` (version 14).
