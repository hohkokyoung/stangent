# Episode Details Modal

`ts|frontend/src/Episode/EpisodeDetailsModalContent.tsx#EpisodeDetailsModalContent` · component · sonarr

From the code: EpisodeDetailsModalContent

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The header shows the series title and the season/episode number, followed by the episode title next to a monitor toggle button. [1]
- **S2** The body has three tabs: Details, History and Search. [1]
- **S3** The footer has a Close button, and an Open Series button when showOpenSeriesButton is set. [1]
- **S4** The Details tab shows when the episode airs with its network, the series quality profile name (Unknown if not found), and the episode overview, or 'NoEpisodeOverview' text when empty. [2][3]
- **S5** When the episode has a file with a path, a table shows its path, size, languages, quality, custom formats, custom format score and actions. [2][4]
- **S6** The History tab shows a loading indicator while fetching, an error alert on failure, an info alert when there is no history, otherwise a table of history rows. [5]
- **S7** Each history row shows event type icon, source title, languages, quality, custom formats, score, relative date, and an info popover with event details. [6]
- **S8** The Search tab shows Quick Search and Interactive Search buttons, or the interactive search results once opened. [7]
- **S9** Interactive search shows a release filter text box, a filter menu, a loading indicator, error and empty-result messages, and a sortable table of releases. [8]

## Actions

- **A1** Toggling the monitor button sets the episode's monitored state to the opposite of its current value. [1][9][10] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/Episode/monitor`](../backend/sonarr.api.v3-c45bd0/put-api-v3-episode-monitor-9edd0a.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/Episode/monitor`](../backend/sonarr.api.v5-c79e7b/put-api-v5-episode-monitor-50a3ef.md)
- **A2** Selecting a tab calls onTabChange with whether it is the Search tab. [1]
- **A3** Quick Search runs an episode search command for the episode and closes the modal. [7][11]
- **A4** Interactive Search opens the interactive release search in place. [7]
- **A5** Deleting the episode file asks for confirmation, then deletes the file. [4][12] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/EpisodeFile/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-episodefile-bulk-01e923.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/EpisodeFile/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-episodefile-bulk-c0723f.md)
- **A6** On a grabbed history row, Mark as Failed asks for confirmation, then marks the history item as failed. [6][13] → [`endpoint:Sonarr.Api.V3|POST /api/v3/History/failed/{id}`](../backend/sonarr.api.v3-c45bd0/post-api-v3-history-failed-id-5718bb.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/History/failed/{id:int}`](../backend/sonarr.api.v5-c79e7b/post-api-v5-history-failed-id-int-0ceb1a.md)
- **A7** In interactive search, a release can be grabbed with the download button, or with a manual override via the override dialog. [14][15] → [`endpoint:Sonarr.Api.V3|POST /api/v3/Release`](../backend/sonarr.api.v3-c45bd0/post-api-v3-release-a4ed0b.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/Release`](../backend/sonarr.api.v5-c79e7b/post-api-v5-release-18fa5b.md)
- **A8** Typing in the release filter box narrows the release list to titles containing the text, case-insensitively. [16][8]

## Rules

- **R1** The monitor toggle is disabled when the series is not monitored. [1]
- **R2** The monitor toggle shows a spinner while saving. [1][10]
- **R3** The modal opens on the 'details' tab by default. [1]
- **R4** The Mark as Failed button appears only for history rows whose event type is grabbed. [6]
- **R5** The same command run within 5 seconds is skipped. [11]
- **R6** Interactive search opens directly if startInteractiveSearch is set or releases were already fetched. [7]
- **R7** Releases are sorted by releaseWeight ascending by default; results are cached for 30 minutes with no retry. [17][16]
- **R8** Seeder counts in the peers label are colored by thresholds: over 50, over 10, over 0, otherwise danger. [18]
- **R9** A manual grab requires a series, at least one episode, a quality and at least one language, otherwise an error is shown. [19]
- **R10** A grab that is not downloadAllowed asks for confirmation first. [14]

## Side effects

- **E1** Toggling monitored updates the cached episode list with the new monitored value on success. [10]
- **E2** Deleting an episode file refreshes the cached episode file and episode data. [12]
- **E3** Marking as failed refreshes the cached history on success. [13]
- **E4** Closing the modal removes the cached release search results for the episode. [20][1]
- **E5** Executing a command posts it to the command endpoint and adds it to the cached command list. [11]
- **E6** Loading releases adds any new rejection reasons to the saved rejection filter tags. [16]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- the depth limit left out 80 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Episode/EpisodeDetailsModalContent.tsx#EpisodeDetailsModalContent`
2. `ts|frontend/src/Episode/Summary/EpisodeSummary.tsx#EpisodeSummary`
3. `ts|frontend/src/Settings/Profiles/Quality/QualityProfileName.tsx#QualityProfileName`
4. `ts|frontend/src/Episode/Summary/EpisodeFileRow.tsx#EpisodeFileRow`
5. `ts|frontend/src/Episode/History/EpisodeHistory.tsx#EpisodeHistory`
6. `ts|frontend/src/Episode/History/EpisodeHistoryRow.tsx#EpisodeHistoryRow`
7. `ts|frontend/src/Episode/Search/EpisodeSearch.tsx#EpisodeSearch`
8. `ts|frontend/src/InteractiveSearch/InteractiveSearch.tsx#InteractiveSearch`
9. `ts|frontend/src/Components/MonitorToggleButton.tsx#MonitorToggleButton`
10. `ts|frontend/src/Episode/useEpisode.ts#useToggleEpisodesMonitored`
11. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
12. `ts|frontend/src/EpisodeFile/useEpisodeFiles.ts#useDeleteEpisodeFile`
13. `ts|frontend/src/Activity/History/useHistory.ts#useMarkAsFailed`
14. `ts|frontend/src/InteractiveSearch/InteractiveSearchRow.tsx#InteractiveSearchRow`
15. `ts|frontend/src/InteractiveSearch/useReleases.ts#useGrabRelease`
16. `ts|frontend/src/InteractiveSearch/useReleases.ts#useReleases`
17. `ts|frontend/src/InteractiveSearch/useReleases.ts#releaseStore`
18. `ts|frontend/src/InteractiveSearch/Peers.tsx#getKind`
19. `ts|frontend/src/InteractiveSearch/OverrideMatch/OverrideMatchModalContent.tsx#OverrideMatchModalContent`
20. `ts|frontend/src/InteractiveSearch/useReleases.ts#useClearReleasesOnUnmount`

<sub>commit 0bffefe · inputs c6704d3 · slice of 793 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
