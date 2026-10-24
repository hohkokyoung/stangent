# Series details page

`page:/series/:titleSlug` · screen · tsconfig.json

From the code: /series/:titleSlug · SeriesDetailsPage

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** If no series matches the URL title slug, the page shows a Page Not Found screen with the message 'SeriesCannotBeFound' translation. [1][2]
- **S2** The page title is the series title. A toolbar offers Refresh & Scan, Search Monitored, Preview Rename, Manage Episodes, History, Episode Monitoring, Edit, Delete and Expand All/Collapse All. [3]
- **S3** The header shows the series poster, a fanart backdrop when a fanart image exists, the title, and an alternate titles popover when there are alternate titles. [3][4][5]
- **S4** Previous and next series arrow buttons link to the neighbouring series in sortTitle order, wrapping around at the ends. [3][6]
- **S5** The header shows runtime, a heart rating with vote count tooltip, genres, and running years when present. Years show as 'year-' or 'year-endYear' when the series has ended with a last aired date. Nothing shows when year is 0. [3][7][8][9]
- **S6** Chips show Monitored/Unmonitored, a Links tooltip, a Tags tooltip (only when the series has tags), episode progress, size on disk, quality profile, season type, language, status, and original language, original country and network when present. [3][10][11]
- **S7** The Links tooltip lists The TVDB, TV Maze, IMDB, Trakt, MDBList and TMDB links depending on which ids exist, with a copy-id button for entries that have an external id. [12]
- **S8** Progress shows 'files / episodes', or 'files + newDownloads / episodes' when downloads are queued. Its colour and icon depend on state: downloading, complete, unmonitored, or missing. [13][14][15]
- **S9** The series path is shown with a pending-move icon when a pending path exists. A metadata attribution link to the metadata source settings is also shown. [16][17]
- **S10** The overview text is clamped, with a More/Less toggle shown when it is clamped or expanded. [3]
- **S11** A loading indicator shows until episodes and episode files are loaded. Separate error alerts show if either fails to load. A warning shows when the series has no seasons. [3]
- **S12** Seasons are listed newest first. Each shows a monitor toggle, 'Specials' for season 0 or a season number, an optional title, a progress label with a season information popover, and size on disk when non-zero. [3][18][19]
- **S13** An expanded season shows an episodes table, or 'NoEpisodesInThisSeason' message when it has none. Episode rows show configurable columns such as monitored, number, title, air date, runtime, formats, languages, media info, size, release group, indexer flags, status and search actions. [18][20]
- **S14** On small screens season actions appear in a menu; otherwise they appear as icon buttons. [18]

## Actions

- **A1** Refresh & Scan runs the refresh series command for this series. [3][21] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Command`](../backend/sonarr.api.v3-c45bd0/any-api-v3-command-7feb4b.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Command`](../backend/sonarr.api.v5-c79e7b/any-api-v5-command-bee921.md)
- **A2** Search Monitored runs the series search command for this series. [3][21] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Command`](../backend/sonarr.api.v3-c45bd0/any-api-v3-command-7feb4b.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Command`](../backend/sonarr.api.v5-c79e7b/any-api-v5-command-bee921.md)
- **A3** Clicking the Monitored/Unmonitored chip toggles the series monitored flag and saves the series. [3][22] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Series`](../backend/sonarr.api.v3-c45bd0/any-api-v3-series-8b55c2.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Series`](../backend/sonarr.api.v5-c79e7b/any-api-v5-series-29b423.md)
- **A4** Preview Rename opens a modal listing existing and new file paths. Organize runs the rename files command for the selected files, with all rows selected by default. [23][24][3] → [`endpoint:Sonarr.Api.V3|GET /api/v3/rename`](../backend/sonarr.api.v3-c45bd0/get-api-v3-rename-fb7fad.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/rename`](../backend/sonarr.api.v5-c79e7b/get-api-v5-rename-594c18.md), [`endpoint:Sonarr.Api.V3|ANY /api/v3/Command`](../backend/sonarr.api.v3-c45bd0/any-api-v3-command-7feb4b.md)
- **A5** Manage Episodes opens an interactive import modal for the series folder. [3]
- **A6** History opens a modal table of series history events, with a mark-as-failed action on grabbed rows. [25][26] → [`endpoint:Sonarr.Api.V3|GET /api/v3/History/series`](../backend/sonarr.api.v3-c45bd0/get-api-v3-history-series-0b0b90.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/History/series`](../backend/sonarr.api.v5-c79e7b/get-api-v5-history-series-4b8f53.md)
- **A7** Episode Monitoring opens a modal to choose a monitoring option and Save it. Save does nothing while 'no change' is selected. [27] → [`endpoint:Sonarr.Api.V3|POST /api/v3/SeasonPass`](../backend/sonarr.api.v3-c45bd0/post-api-v3-seasonpass-d38909.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/SeasonPass`](../backend/sonarr.api.v5-c79e7b/post-api-v5-seasonpass-71b638.md)
- **A8** Edit opens a form for monitored, new seasons monitoring, season folder, quality profile, series type, language, season type, path and tags. Save updates the series. [28][29] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Series`](../backend/sonarr.api.v3-c45bd0/any-api-v3-series-8b55c2.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Series`](../backend/sonarr.api.v5-c79e7b/any-api-v5-series-29b423.md)
- **A9** Delete opens a modal with options to add a list exclusion and delete files. Delete removes the series. [30][31] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/series/editor`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-series-editor-7bcd3d.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/series/editor`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-series-editor-a840b3.md)
- **A10** Expand All/Collapse All toggles all seasons, and each season has its own expand/collapse button. [3][18]
- **A11** A season's monitor toggle sets the season monitored state. [18][32] → [`endpoint:Sonarr.Api.V5|PUT /api/v5/Series/{id:int}/season`](../backend/sonarr.api.v5-c79e7b/put-api-v5-series-id-int-season-0b0f0b.md)
- **A12** An episode's monitor toggle sets that episode's monitored state. Shift-click also sets the range back to the last toggled episode. [18][33][34] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/Episode/monitor`](../backend/sonarr.api.v3-c45bd0/put-api-v3-episode-monitor-9edd0a.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/Episode/monitor`](../backend/sonarr.api.v5-c79e7b/put-api-v5-episode-monitor-50a3ef.md)
- **A13** A season's search button runs the season search command. Season buttons also open interactive search, preview rename, manage episodes and history for that season. [18][35] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Command`](../backend/sonarr.api.v3-c45bd0/any-api-v3-command-7feb4b.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Command`](../backend/sonarr.api.v5-c79e7b/any-api-v5-command-bee921.md)

## Rules

- **R1** Search Monitored is disabled when the series is unmonitored, has no monitored episodes, or has no episodes. [3]
- **R2** Preview Rename is disabled without episode files. History is disabled without episodes. [3]
- **R3** If the series disappears and an item with its previous id exists, the page redirects to the new slug; otherwise it redirects to '/'. [1]
- **R4** Season and episode monitor toggles are disabled when the series is unmonitored. [18][20]
- **R5** A season's search is disabled while searching, with no monitored episodes, or when the series is unmonitored. Interactive search and history need episodes; rename and manage need episode files. [18]
- **R6** A season counts an episode when it has a file or is monitored and aired in the past. [36]
- **R7** A season starts expanded if it is season 1 or later and an episode airs in the future or within the last 30 days, or no episode has an air date. Otherwise it starts collapsed. [18]
- **R8** Running the same command within 5 seconds is ignored. [21]
- **R9** The Delete files option defaults to unchecked; the delete modal warns with the path and file count when it is checked. [30]
- **R10** The season type field is disabled when there are no season types or episode files exist. Saving a changed path first asks to confirm moving files. [28]
- **R11** The Edit modal closes after a successful save. [28]

## Side effects

- **E1** Series, episodes, episode files and commands are fetched from the API. Commands are refreshed on an interval. [37][38][39][40]
- **E2** Episodes and files are refetched when a refresh or rename command finishes, and when the page populator reports 'seriesUpdated'. [3][41]
- **E3** Deleting a series removes it from the cached series list. [31]
- **E4** Saving monitoring options refetches the series' episodes. [42][27]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- the depth limit left out 196 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Series/Details/SeriesDetailsPage.tsx#SeriesDetailsPage`
2. `ts|frontend/src/Components/NotFound.tsx#NotFound`
3. `ts|frontend/src/Series/Details/SeriesDetails.tsx#SeriesDetails`
4. `ts|frontend/src/Series/Details/SeriesDetails.tsx#getFanartUrl`
5. `ts|frontend/src/Series/Details/SeriesAlternateTitles.tsx#SeriesAlternateTitles`
6. `ts|frontend/src/Utilities/Array/sortByProp.ts#sortByProp`
7. `ts|frontend/src/Series/Details/SeriesDetails.tsx#getRunningYears`
8. `ts|frontend/src/Components/HeartRating.tsx#HeartRating`
9. `ts|frontend/src/Series/SeriesGenres.tsx#SeriesGenres`
10. `ts|frontend/src/Settings/Profiles/Quality/QualityProfileName.tsx#QualityProfileName`
11. `ts|frontend/src/Series/SeriesStatus.ts#getSeriesStatusDetails`
12. `ts|frontend/src/Series/Details/SeriesDetailsLinks.tsx#SeriesDetailsLinks`
13. `ts|frontend/src/Series/Details/SeriesProgressLabel.tsx#SeriesProgressLabel`
14. `ts|frontend/src/Series/Details/SeriesProgressLabel.tsx#getEpisodeCountKind`
15. `ts|frontend/src/Series/Details/SeriesProgressLabel.tsx#getEpisodeCountIcon`
16. `ts|frontend/src/Series/Details/SeriesDetailsPath.tsx#SeriesDetailsPath`
17. `ts|frontend/src/Components/MetadataAttribution.tsx#MetadataAttribution`
18. `ts|frontend/src/Series/Details/SeriesDetailsSeason.tsx#SeriesDetailsSeason`
19. `ts|frontend/src/Series/Details/SeasonInfo.tsx#SeasonInfo`
20. `ts|frontend/src/Series/Details/EpisodeRow.tsx#EpisodeRow`
21. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
22. `ts|frontend/src/Series/useSeries.ts#useToggleSeriesMonitored`
23. `ts|frontend/src/Organize/OrganizePreviewModalContent.tsx#OrganizePreviewModalContentInner`
24. `ts|frontend/src/Organize/OrganizePreviewRow.tsx#OrganizePreviewRow`
25. `ts|frontend/src/Series/History/SeriesHistoryModalContent.tsx#SeriesHistoryModalContent`
26. `ts|frontend/src/Series/History/SeriesHistoryRow.tsx#SeriesHistoryRow`
27. `ts|frontend/src/Series/MonitoringOptions/MonitoringOptionsModalContent.tsx#MonitoringOptionsModalContent`
28. `ts|frontend/src/Series/Edit/EditSeriesModalContent.tsx#EditSeriesModalContent`
29. `ts|frontend/src/Series/useSeries.ts#useSaveSeries`
30. `ts|frontend/src/Series/Delete/DeleteSeriesModalContent.tsx#DeleteSeriesModalContent`
31. `ts|frontend/src/Series/useSeries.ts#useDeleteSeries`
32. `ts|frontend/src/Series/useSeries.ts#useToggleSeasonMonitored`
33. `ts|frontend/src/Episode/useEpisode.ts#useToggleEpisodesMonitored`
34. `ts|frontend/src/Utilities/Table/getToggledRange.ts#getToggledRange`
35. `ts|frontend/src/Series/Search/SeasonInteractiveSearchModalContent.tsx#SeasonInteractiveSearchModalContent`
36. `ts|frontend/src/Series/Details/SeriesDetailsSeason.tsx#getSeasonStatistics`
37. `ts|frontend/src/Series/useSeries.ts#useSeries`
38. `ts|frontend/src/Episode/useEpisodes.ts#useEpisodes`
39. `ts|frontend/src/EpisodeFile/useEpisodeFiles.ts#useEpisodeFiles`
40. `ts|frontend/src/Commands/useCommands.ts#useCommands`
41. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`
42. `ts|frontend/src/Series/useSeries.ts#useUpdateSeriesMonitor`

<sub>commit 0bffefe · inputs 031c2d0 · slice of 1062 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
