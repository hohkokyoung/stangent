# Wanted: Missing episodes

`page:/wanted/missing` · screen · tsconfig.json

From the code: /wanted/missing · Missing

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Missing" with the heading scope "Wanted". [1]
- **S2** Missing episodes are listed in a paged table, with a row per episode. Rows for episodes whose series is not found are not shown. [1][2]
- **S3** Each row has a select checkbox and the visible columns: series title link, episode number, episode title link, air date, last search time, status and a search/actions cell. [2]
- **S4** A loading indicator shows while the list is fetching and loading. [1]
- **S5** When loading fails and it is not fetching, an error alert shows the message for "MissingLoadError". [1]
- **S6** When there is no error and no records, a "MissingNoItems" message is shown instead of the table. [1]
- **S7** A pager below the table shows the current page, total pages and total records. [1]
- **S8** The toolbar has Search (label "Search Selected" when any row is selected, otherwise "Search All"), Monitor/Unmonitor Selected, Manual Import, Options and a filter menu. [1]

## Actions

- **A1** Pressing Search with rows selected runs an episode search command for the selected episode ids, then refetches the list. [1][3]
- **A2** Pressing Search with nothing selected opens a confirmation modal "Search for all missing episodes" showing the total record count and a mass search cancel warning. [1]
- **A3** Confirming the modal runs a missing-episode search command built from the active filters, refetches the list and closes the modal. [1][4]
- **A4** Monitor/Unmonitor Selected toggles the monitored state of the selected episodes through a PUT to the episode monitor endpoint. [1][5] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/Episode/monitor`](../backend/sonarr.api.v3-c45bd0/put-api-v3-episode-monitor-9edd0a.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/Episode/monitor`](../backend/sonarr.api.v5-c79e7b/put-api-v5-episode-monitor-50a3ef.md)
- **A5** Manual Import opens the interactive import modal. [1]
- **A6** The list is loaded from the wanted/missing endpoint with page, page size, sort and filters. [6][7] → [`endpoint:Sonarr.Api.V3|GET /api/v3/wanted/missing`](../backend/sonarr.api.v3-c45bd0/get-api-v3-wanted-missing-361610.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/wanted/missing`](../backend/sonarr.api.v5-c79e7b/get-api-v5-wanted-missing-8f262a.md)
- **A7** Users can pick a filter, sort by a column, change table options (columns, page size) and select all or individual rows. Changing page size returns to page 1. [1][2]

## Rules

- **R1** The Search button is disabled and spinning while a missing-episode or episode search command is queued or started. [1][8]
- **R2** Monitor/Unmonitor Selected is disabled when nothing is selected. It reads "Unmonitor Selected" when the selected filter shows monitored episodes, and sets monitored to the opposite of that. [1][9]
- **R3** The same command is not sent again if it was run within the last 5 seconds. [3]
- **R4** Only filters seriesIds, qualityProfileIds, seriesType, seriesTags and quality are passed into the search-all command body. [4]
- **R5** The page number starts at 1 and resets to 1 on back/forward navigation. Total pages is at least 1 when records are loaded. [10][7]
- **R6** Filters come from the selected built-in filter or the saved custom filter of type wanted.missing; the previous page data is kept while a new page loads. [6][11]

## Side effects

- **E1** The page refetches the list when seriesUpdated, episodeFileUpdated or episodeFileDeleted updates occur. [1][12]
- **E2** After a successful monitor toggle, the cached episodes are updated with the new monitored value. [5]
- **E3** Running a search posts a command to the command API. [3]
- **E4** The query key of the loaded missing list is stored for the wanted.missing episode entity. [6][13]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/Wanted/Missing/Missing.tsx#MissingContent`
2. `ts|frontend/src/Wanted/Missing/MissingRow.tsx#MissingRow`
3. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
4. `ts|frontend/src/Wanted/toWantedSearchCommandBody.ts#toWantedSearchCommandBody`
5. `ts|frontend/src/Episode/useEpisode.ts#useToggleEpisodesMonitored`
6. `ts|frontend/src/Wanted/Missing/useMissing.tsx#useMissing`
7. `ts|frontend/src/Helpers/Hooks/usePagedApiQuery.ts#usePagedApiQuery`
8. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
9. `ts|frontend/src/Wanted/Missing/Missing.tsx#getMonitoredValue`
10. `ts|frontend/src/Helpers/Hooks/usePage.ts#usePage`
11. `ts|frontend/src/Utilities/Filter/findSelectedFilters.ts#findSelectedFilters`
12. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`
13. `ts|frontend/src/Episode/useEpisode.ts#setEpisodeQueryKey`

<sub>commit 0bffefe · inputs cda94ed · slice of 317 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
