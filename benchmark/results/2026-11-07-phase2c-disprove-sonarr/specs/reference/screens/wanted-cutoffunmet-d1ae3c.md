# Wanted: Cutoff Unmet

`page:/wanted/cutoffunmet` · screen · tsconfig.json

From the code: /wanted/cutoffunmet · CutoffUnmet

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Cutoff Unmet" with the scope "Wanted" in the heading. [1]
- **S2** A loading indicator shows while the list is fetching and loading. [1]
- **S3** When loading fails and the list is not fetching, an error alert shows the message for CutoffUnmetLoadError. [1]
- **S4** When there is no error and no records, a message for CutoffUnmetNoItems shows. [1]
- **S5** When records exist, they show in a selectable table with a pager showing page, total pages and total records. [1]
- **S6** Each row can show these columns when visible: series title, episode number, episode title, air date, last search time, languages, quality, status, and an actions/search cell. [2]
- **S7** The file quality shows as "Unknown" when the episode file has no quality. [3]
- **S8** The toolbar shows Search, Monitor/Unmonitor, Options and Filter items. [1]
- **S9** The search button reads "Search Selected" when any row is selected, otherwise "Search All". [1]
- **S10** The monitor button reads "Unmonitor Selected" when the current filter shows monitored items, otherwise "Monitor Selected". [1][4]

## Actions

- **A1** Search Selected runs an episode search for the selected episode ids, then refetches the list. [1]
- **A2** Search All opens a confirmation dialog showing the total record count and a mass-search cancel warning; confirming runs a cutoff-unmet episode search using the active filters, then refetches. [1][5]
- **A3** Monitor/Unmonitor Selected toggles the monitored state of the selected episodes. [1][6] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/Episode/monitor`](../backend/sonarr.api.v3-c45bd0/put-api-v3-episode-monitor-9edd0a.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/Episode/monitor`](../backend/sonarr.api.v5-c79e7b/put-api-v5-episode-monitor-50a3ef.md)
- **A4** The list is loaded from the wanted/cutoff endpoint with page, page size, sort and filters. [7][8] → [`endpoint:Sonarr.Api.V3|GET /api/v3/wanted/cutoff`](../backend/sonarr.api.v3-c45bd0/get-api-v3-wanted-cutoff-5d3e3d.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/wanted/cutoff`](../backend/sonarr.api.v5-c79e7b/get-api-v5-wanted-cutoff-21aa08.md)
- **A5** Users can select all or unselect all rows with the header checkbox, and select individual rows. [1][2]
- **A6** Users can choose a filter or a custom filter, sort by a column, and open table options to change columns and page size. [1][9]
- **A7** Episode file details are loaded for the listed episode file ids. [10] → [`endpoint:Sonarr.Api.V3|GET /api/v3/EpisodeFile`](../backend/sonarr.api.v3-c45bd0/get-api-v3-episodefile-233ba7.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/EpisodeFile`](../backend/sonarr.api.v5-c79e7b/get-api-v5-episodefile-4ec726.md)

## Rules

- **R1** The Search button is disabled and spinning while a cutoff-unmet or episode search command is queued or started. [1][11]
- **R2** The monitor toggle button is disabled unless at least one row is selected. [1]
- **R3** Changing the page size returns the list to page 1. [1]
- **R4** Total pages is the total records divided by page size, rounded up, with a minimum of 1. [8]
- **R5** A row is not shown if its series is unknown or it has no episode file id. [2]
- **R6** The same command run again within 5 seconds is ignored. [12]
- **R7** Search All filters sent to the command are limited to seriesIds, qualityProfileIds, seriesType, seriesTags and quality. [5]
- **R8** The page number resets to 1 on browser back/forward navigation. [13]
- **R9** Episode files are fetched only when episode file ids are present. [10]

## Side effects

- **E1** Search actions send a command to the command endpoint, which is a POST to /command. [12]
- **E2** Toggling monitored sends PUT /episode/monitor and updates the cached episodes on success. [6]
- **E3** The list refetches when seriesUpdated, episodeFileUpdated or episodeFileDeleted events occur. [1][14]
- **E4** The page's query key is stored as the cutoff-unmet episode query key. [7][15]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/Wanted/CutoffUnmet/CutoffUnmet.tsx#CutoffUnmetContent`
2. `ts|frontend/src/Wanted/CutoffUnmet/CutoffUnmetRow.tsx#CutoffUnmetRow`
3. `ts|frontend/src/EpisodeFile/EpisodeFileQuality.tsx#EpisodeFileQuality`
4. `ts|frontend/src/Wanted/CutoffUnmet/CutoffUnmet.tsx#getMonitoredValue`
5. `ts|frontend/src/Wanted/toWantedSearchCommandBody.ts#toWantedSearchCommandBody`
6. `ts|frontend/src/Episode/useEpisode.ts#useToggleEpisodesMonitored`
7. `ts|frontend/src/Wanted/CutoffUnmet/useCutoffUnmet.tsx#useCutoffUnmet`
8. `ts|frontend/src/Helpers/Hooks/usePagedApiQuery.ts#usePagedApiQuery`
9. `ts|frontend/src/Wanted/CutoffUnmet/CutoffUnmetFilterModal.tsx#CutoffUnmetFilterModal`
10. `ts|frontend/src/EpisodeFile/useEpisodeFiles.ts#useEpisodeFiles`
11. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
12. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
13. `ts|frontend/src/Helpers/Hooks/usePage.ts#usePage`
14. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`
15. `ts|frontend/src/Episode/useEpisode.ts#setEpisodeQueryKey`

<sub>commit 0bffefe · inputs 6f80996 · slice of 351 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
