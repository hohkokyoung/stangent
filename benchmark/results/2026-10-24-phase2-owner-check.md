# Phase 2 — your two checks (Sonarr, 2026-10-24)

Two short jobs. Reply in chat with your answers; I fill in the files and re-score. Nothing here was shown to the AI.

## Part A — 20 unplanted findings: real drift or false alarm?

These are findings that match none of the 30 planted items, picked by code (seed 42) from the 62 unplanted. For each,
say **real** (the FSD and the code genuinely differ, or the FSD really leaves out something a BA would write down) or
**false** (no real difference: the judge or the second check was too strict, or misread). The D57 bar: at most 30% of
all findings are false alarms. With 62 of 89 unplanted, it passes if about 12 or more of these 20 are real.

The FSD text is in `2026-10-24-phase2-sonarr/specs/fsd/sonarr-fsd/document.md` (links below are relative to `benchmark/results/`).

### A1. gap · section 1.1
- **FSD says:** | Series Type | Standard, daily or anime; this changes how searches are made. |
- **Page:** `page:/add/new` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/add-new-e02b06.md))
- **Why it was raised:** Disputed by the second check: S16 says the popover describes Anime, Daily and Standard, but not that it changes how searches are made.
- id `L75fb026b16` · your verdict: real / false

### A2. undocumented · section Settings / Profiles / Quality profiles
- **Page:** `page:/settings/profiles` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/settings-profiles-d5c636.md))
- **Page says:** If the cutoff is unset or no longer allowed, it is set to the first allowed quality.
- **Why it was raised:** Cutoff defaulting rule is not in the FSD.
- id `Lfefaa07baa` · your verdict: real / false

### A3. gap · section Settings / Profiles / Delay profiles
- **FSD says:** Usenet Delay and Torrent Delay: in minutes; neither can be negative.
- **Page:** `page:/settings/profiles` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/settings-profiles-d5c636.md))
- **Why it was raised:** Disputed by the second check: Page R7 says delays are in minutes, but nothing shows that negative values are rejected.
- id `L9a99a0d94b` · your verdict: real / false

### A4. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/system/status` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/system-status-050695.md))
- **Why it was raised:** no FSD section links to this screen
- id `L2e3de4a2e0` · your verdict: real / false

### A5. gap · section Wanted / Missing episodes
- **FSD says:** Episodes whose file exists but whose quality is below the cutoff are not listed here.
- **Page:** `page:/wanted/missing` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/wanted-missing-45a580.md))
- **Why it was raised:** No statement on the missing page excludes cutoff-unmet episodes (those are on a separate cutoff unmet page).
- id `L62a755f545` · your verdict: real / false

### A6. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/settings/connect` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/settings-connect-63edb1.md))
- **Why it was raised:** no FSD section links to this screen
- id `L4e5e31ff42` · your verdict: real / false

### A7. conflict · section 1.1
- **FSD says:** - Start search for missing episodes: when ticked, the new series' missing monitored episodes are searched for straight after adding.
- **Page:** `page:/add/new` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/add-new-e02b06.md))
- **Page says:** plus Search checkboxes for Missing and Cutoff Unmet.
- **Why it was raised:** FSD mentions only the missing search option; page also has a Cutoff Unmet search checkbox.
- id `Ld8b49577e7` · your verdict: real / false

### A8. gap · section 1.1
- **FSD says:** | Tags | Tags link the series to delay profiles, release profiles and indexers. |
- **Page:** `page:/add/new` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/add-new-e02b06.md))
- **Why it was raised:** Disputed by the second check: S15 only lists a Tags field; the linkage to delay profiles, release profiles and indexers isn't shown.
- id `Lf02bf20efd` · your verdict: real / false

### A9. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/system/events` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/system-events-4a03db.md))
- **Why it was raised:** no FSD section links to this screen
- id `La1cb5f357f` · your verdict: real / false

### A10. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/seasonpass` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/seasonpass-33b52d.md))
- **Why it was raised:** no FSD section links to this screen
- id `L3b4e14ba3e` · your verdict: real / false

### A11. gap · section 2
- **FSD says:** Monitored and unmonitored episodes can be shown or hidden
- **Page:** `page:/calendar` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/calendar-a662cd.md))
- **Why it was raised:** Disputed by the second check: S14 only lists event statuses (downloaded, unmonitored, etc.); it does not show that monitored/unmonitored episodes can be shown or hidden. That is in other statements (R5 filter).
- id `Lcd2be0442b` · your verdict: real / false

### A12. gap · section System / Backups
- **FSD says:** The user can take a backup now, restore from a backup in the list, or restore from a backup file they upload.
- **Page:** `page:/system/backups` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/system-backups-bc2863.md))
- **Why it was raised:** Disputed by the second check: A1 only covers the Backup Now button. The FSD sentence also covers restoring from a listed backup and from an uploaded file (page A2, A4, A5), so A1 alone doesn't show the whole sentence.
- id `L04d4ddb984` · your verdict: real / false

### A13. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/add/import/:rootFolderId` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/add-import-rootfolderid-a118a0.md))
- **Why it was raised:** no FSD section links to this screen
- id `L035853bf41` · your verdict: real / false

### A14. gap · section 1.2
- **FSD says:** The series details screen shows one series: its poster, overview, network, status, path, size on disk and its seasons, with each season's episodes listed underneath.
- **Page:** `page:/series/:titleSlug` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/series-titleslug-19f6a1.md))
- **Why it was raised:** Disputed by the second check: S3 covers only the header (poster, fanart, title, alternate titles). It doesn't cover overview, network, status, path, size on disk, seasons or episodes. Those appear in other statements, so this link is partial.
- id `Ld7dcac8eec` · your verdict: real / false

### A15. conflict · section Settings / Quality definitions
- **FSD says:** The quality definitions screen lists every quality with its title and its minimum, preferred and maximum size in megabytes per minute.
- **Page:** `page:/settings/quality` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/settings-quality-85d066.md))
- **Page says:** The list has column headers 'Quality' and 'Title'. Each row shows the quality name and an editable text input holding the title.
- **Why it was raised:** FSD: screen lists min, preferred and max size; page screen shows only quality and editable title columns.
- id `L55fc880fb6` · your verdict: real / false

### A16. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/root-be96de.md))
- **Why it was raised:** no FSD section links to this screen
- id `L90d600ed7e` · your verdict: real / false

### A17. undocumented · section Settings / Tags
- **Page:** `endpoint:Sonarr.Api.V3|ANY /api/v3/Tag` ([page](2026-10-24-phase2-sonarr/specs/reference/backend/sonarr.api.v3-c45bd0/any-api-v3-tag-a2227b.md))
- **Page says:** Adding a tag whose label already exists returns the existing tag and does not insert a new one.
- **Why it was raised:** Duplicate label handling not in FSD.
- id `L384481b321` · your verdict: real / false

### A18. undocumented · section (none: no FSD section links this screen)
- **Page:** `page:/settings/importlists` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/settings-importlists-48fe6f.md))
- **Why it was raised:** no FSD section links to this screen
- id `L0c6d91c33a` · your verdict: real / false

### A19. gap · section 3.1
- **FSD says:** The user can remove a release from the queue. Removing an item from the queue always adds the release to the blocklist.
- **Page:** `page:/activity/queue` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/activity-queue-deba90.md))
- **Why it was raised:** Disputed by the second check: A6 confirms the user can remove an item. The FSD also says removal always adds the release to the blocklist. The page lets the user pick a blocklist method (A8) and a DoNotBlocklist option exists, so removal does not always blocklist.
- id `L191027d8b5` · your verdict: real / false

### A20. undocumented · section Settings / Profiles / Quality profiles
- **Page:** `page:/settings/profiles` ([page](2026-10-24-phase2-sonarr/specs/reference/screens/settings-profiles-d5c636.md))
- **Page says:** The Upgrade Until field shows only when Upgrades Allowed is checked.
- **Why it was raised:** The FSD does not say that Upgrade Until is conditional on Upgrades Allowed.
- id `L60bd6c7b0e` · your verdict: real / false

## Part B — hand-read 5 pages

Picked by code: 2 screens, 1 component, 1 endpoint, 1 handler, `random.Random(42)` over the sorted recorded page ids of each
kind. For each page, count the statements that are **wrong** (the code doesn't do that) and, if you like, name them
by id (S3, R2…). Each statement cites the code it comes from; the page's Markdown links the symbols. No bar: it tells us
whether Sonnet is good enough as the writer or Opus is needed (D66). I'll compare your count with what the verifier caught.

1. **Calendar** (screen, 40 statements) — [2026-10-24-phase2-sonarr/specs/reference/screens/calendar-a662cd.md](2026-10-24-phase2-sonarr/specs/reference/screens/calendar-a662cd.md)  · wrong: __
2. **Blocklist** (screen, 24 statements) — [2026-10-24-phase2-sonarr/specs/reference/screens/activity-blocklist-20ed5f.md](2026-10-24-phase2-sonarr/specs/reference/screens/activity-blocklist-20ed5f.md)  · wrong: __
3. **Queue Status Icon** (component, 15 statements) — [2026-10-24-phase2-sonarr/specs/reference/components/queuestatus-794914.md](2026-10-24-phase2-sonarr/specs/reference/components/queuestatus-794914.md)  · wrong: __
4. **Bulk delete blocklist entries** (endpoint, 4 statements) — [2026-10-24-phase2-sonarr/specs/reference/backend/sonarr.api.v3-c45bd0/delete-api-v3-blocklist-bulk-c74ce8.md](2026-10-24-phase2-sonarr/specs/reference/backend/sonarr.api.v3-c45bd0/delete-api-v3-blocklist-bulk-c74ce8.md)  · wrong: __
5. **Provider added event handler (live update broadcast)** (handler, 5 statements) — [2026-10-24-phase2-sonarr/specs/reference/backend/sonarr.api.v5-c79e7b/providercontrollerbase.handle-provideraddedevent-2-287367.md](2026-10-24-phase2-sonarr/specs/reference/backend/sonarr.api.v5-c79e7b/providercontrollerbase.handle-provideraddedevent-2-287367.md)  · wrong: __

## How to answer

A short reply is enough, e.g. `A: 1 real, 2 false, 3 real … B: 1→0, 2→1 (R4), 3→0, 4→2 (S2, A1), 5→0`.
