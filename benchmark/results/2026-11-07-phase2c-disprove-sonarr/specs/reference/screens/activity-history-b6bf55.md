# Activity History

`page:/activity/history` · screen · tsconfig.json

From the code: /activity/history · History

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled History, with the heading scope Activity and title History. [1]
- **S2** A loading indicator shows while history or episodes are loading and the page is not yet fully populated. [1]
- **S3** If history or episodes fail to load (and nothing is fetching), a danger alert shows the HistoryLoadError message. [1]
- **S4** When history has loaded without error and has no records, the page shows the NoHistoryFound message. [1]
- **S5** When populated, without error and with records, a table of history rows is shown with a pager (page, total pages, total records). [1]
- **S6** The toolbar has Refresh and Options buttons and a filter menu with built-in and custom history filters; the Refresh icon spins while fetching. [1][2]
- **S7** Each row can show columns: event type icon (with tooltip), series, episode number, episode title, languages, quality, custom formats, date, download client, indexer, custom format score, release group, source title, and a details button. Only visible columns are rendered. [3]
- **S8** A row is not rendered until both its series and episode are available. [3]
- **S9** Event type icons: grabbed and downloadFailed use a downloading icon, seriesFolderImported a drive, downloadFolderImported a downloaded icon, episodeFileDeleted a missing-file icon when the reason is MissingFromDisk otherwise delete, renamed an organize icon, ignored an ignore icon, others unknown. downloadFailed is shown in danger color. [4][5][6]
- **S10** The event type cell tooltip text depends on the event type; grabbed mentions the indexer and download client. [7]
- **S11** The custom format score cell shows +N, -N, '+0' (when there are custom formats and score is zero), or blank, with a tooltip listing the custom formats. [8][3]
- **S12** The download client column shows the download client name, falling back to the download client, else blank. Indexer and release group show blank when absent from the event data. [3]
- **S13** The details modal title depends on event type: Grabbed, DownloadFailed, EpisodeImported, EpisodeFileDeleted, EpisodeFileRenamed, DownloadIgnored, otherwise Unknown. [9]
- **S14** For grabbed events the details show Name, Indexer, Release Group, Custom Format Score (if non-zero), Series Match Type, Release Source, Info URL link, Download Client, Grab Id, Age When Grabbed, Published Date and Size, each only when present. [10]
- **S15** For downloadFailed events the details show Name, Grab Id, Indexer, Message and Source; for downloadIgnored they show Name, Grab Id and Message. [10]
- **S16** For imported events the details show Name, Source (dropped path), Imported To, Custom Format Score (if non-zero) and File Size. [10]
- **S17** For deleted file events the details show Name, Reason (Manual, missing from disk, or upgrade), Custom Format Score (if non-zero) and File Size. [10]
- **S18** For renamed file events the details show source path, source relative path, destination path and destination relative path; other event types show only Name. [10]

## Actions

- **A1** Refresh resets to page 1 and refetches history. [1]
- **A2** Options opens a table options modal to change columns and page size. [1]
- **A3** Selecting a filter in the filter menu sets the selected history filter; a filter modal supports custom filters of type history. [1][11]
- **A4** Clicking a column header sorts the table. [1]
- **A5** The pager moves between pages. [1][12]
- **A6** The Details button on a row opens a modal with event details and a Close button. [3][13]
- **A7** For grabbed events the details modal has a Mark as failed button that posts to the history failed endpoint. [13][14] → [`endpoint:Sonarr.Api.V5|POST /api/v5/History/failed/{id:int}`](../backend/sonarr.api.v5-c79e7b/post-api-v5-history-failed-id-int-0ceb1a.md)

## Rules

- **R1** History is requested from /history with page, page size, sort key, sort direction and the selected filters; total pages is at least 1. [15][16]
- **R2** Episodes for the listed records are fetched by their unique episode ids, and only when there is at least one id. [17][18]
- **R3** The current page defaults to 1 and resets to 1 on back/forward navigation; changing page size returns to page 1. [19][12][1]
- **R4** If the selected filter key is empty, no filters are applied; an unmatched key logs an error and applies none. [20]
- **R5** Mark as failed appears only for grabbed events. [13]
- **R6** If marking as failed fails, the error 'Error marking history item as failed' is set. [14]

## Side effects

- **E1** Marking a history item as failed sends POST /history/failed/{id} and, on success, invalidates the cached history queries. [14]
- **E2** The page registers a populator that refetches history, and unregisters it when leaving. [1][21][22]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime
- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Activity/History/History.tsx#History`
2. `ts|frontend/src/Filters/useCustomFilters.ts#useCustomFiltersList`
3. `ts|frontend/src/Activity/History/HistoryRow.tsx#HistoryRow`
4. `ts|frontend/src/Activity/History/HistoryEventTypeCell.tsx#getIconName`
5. `ts|frontend/src/Activity/History/HistoryEventTypeCell.tsx#getIconKind`
6. `ts|frontend/src/Activity/History/HistoryEventTypeCell.tsx#HistoryEventTypeCell`
7. `ts|frontend/src/Activity/History/HistoryEventTypeCell.tsx#getTooltip`
8. `ts|frontend/src/Utilities/Number/formatCustomFormatScore.ts#formatCustomFormatScore`
9. `ts|frontend/src/Activity/History/Details/HistoryDetailsModal.tsx#getHeaderTitle`
10. `ts|frontend/src/Activity/History/Details/HistoryDetails.tsx#HistoryDetails`
11. `ts|frontend/src/Activity/History/HistoryFilterModal.tsx#HistoryFilterModal`
12. `ts|frontend/src/Helpers/Hooks/usePage.ts#usePage`
13. `ts|frontend/src/Activity/History/Details/HistoryDetailsModal.tsx#HistoryDetailsModal`
14. `ts|frontend/src/Activity/History/useHistory.ts#useMarkAsFailed`
15. `ts|frontend/src/Activity/History/useHistory.ts#useHistory`
16. `ts|frontend/src/Helpers/Hooks/usePagedApiQuery.ts#usePagedApiQuery`
17. `ts|frontend/src/Episode/useEpisodes.ts#useEpisodes`
18. `ts|frontend/src/Utilities/Object/selectUniqueIds.ts#selectUniqueIds`
19. `ts|frontend/src/Helpers/Hooks/usePage.ts#pageStore`
20. `ts|frontend/src/Utilities/Filter/findSelectedFilters.ts#findSelectedFilters`
21. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`
22. `ts|frontend/src/Utilities/pagePopulator.ts#unregisterPagePopulator`

<sub>commit 0bffefe · inputs 051c491 · slice of 396 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
