### Is there an existing issue for this?

- [x] I have searched the existing open and closed issues

### Current Behavior

Setting up an Import List for "Trakt - Recommended by Year" results in "Recommended by Weekly" behavior instead of yearly.

### Expected Behavior

Setting up an Import List for "Trakt - Recommended by Year" results in recommendations (most favorited Shows) from Trakt over the course of the past year.

### Steps To Reproduce

1. Go to "Import Lists" settings in Sonarr Web UI via Settings > Import Lists.
2. Add new Import List by clicking the + button under "Import Lists" heading.
3. Select "Trakt Popular List" under the "Trakt List" section.
4. Under List Type, select "Recommended Shows By Year".
5. Save the Import List.
6. Wait for the next Import and compare the import results against the Trakt website for "shows/recommended/weekly" and "shows/recommended/yearly" (the latter of which doesn't actually work)

### Environment

```markdown
- OS: Ubuntu 24.04.1 LTS
- Sonarr: 4.0.14.2939
- Docker Install: Yes
- Using Reverse Proxy: No
- Browser: Chrome 134.0.6998.118
- Database: Sqlite 3.48.0
```

### What branch are you running?

Main

### Trace Logs?

2025-03-24 14:51:16.9|Info|ImportListSyncService|Starting Import List Sync
2025-03-24 14:51:16.9|Debug|FetchAndParseImportListService|Available import lists 3
2025-03-24 14:51:16.9|Debug|TraktPopularImport|Downloading Feed https://api.trakt.tv/shows/recommended/yearly?years=&genres=&ratings=&limit=5
2025-03-24 14:51:16.9|Debug|TraktUserImport|Downloading Feed https://api.trakt.tv/users/(removed)/watchlist/shows/rank?limit=100
2025-03-24 14:51:17.3|Debug|FetchAndParseImportListService|Found 80 reports from Trakt User (Trakt - halfskye Watchlist)
2025-03-24 14:51:21.0|Debug|FetchAndParseImportListService|Found 5 reports from Trakt Popular List (Trakt - Recommended by Year)
2025-03-24 14:51:21.0|Debug|FetchAndParseImportListService|Found 85 total reports from 3 lists
2025-03-24 14:51:21.0|Info|ImportListSyncService|Processing 85 list items

### Anything else?

This seems to actually be an issue with Trakt since even when the API request for "shows/recommended/yearly" is used with a URL in a web browser, the results indicate "the most favorited shows for **the last 7 days.**":

![Image](https://github.com/user-attachments/assets/16b419fa-9298-4cf3-a5de-a67fb69d0495)

Using the Movies equivalent "movies/recommended/yearly" results in the same weekly behavior.

It seems if the trailing period label isn't recognized (e.g., "yearly", "year", "foo"), the API defaults to "weekly".

If Trakt no longer supports the Yearly period, maybe this Import List option should be removed?
