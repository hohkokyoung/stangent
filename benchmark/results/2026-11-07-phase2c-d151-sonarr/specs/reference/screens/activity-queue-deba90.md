# Activity Queue

`page:/activity/queue` · screen · tsconfig.json

From the code: /activity/queue · Queue

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is headed Activity / Queue and lists queue items in a table with a pager showing page, total pages and total records. [1]
- **S2** While loading and not yet populated, a loading indicator is shown. [1]
- **S3** When loading is finished and the queue or episodes request failed, an error alert shows the 'QueueLoadError' message. [1]
- **S4** When there are no records, a message shows: 'QueueFilterHasNoItems' if a filter other than 'all' is selected and the queue status count is above 0, otherwise 'QueueIsEmpty'. [1]
- **S5** The toolbar has Refresh, Grab Selected, Remove Selected, Import Selected, Options buttons and a filter menu with custom filters of type queue. [1]
- **S6** Each row has a selection checkbox and shows only the visible columns: status, series, episode, episode title, air date, languages, quality, custom formats, custom format score, protocol, indexer, download client, title, size, output path, time left, progress, added, actions. [2]
- **S7** The series cell links to the series if it is known; otherwise it shows the release title. [2]
- **S8** The episode cell shows '-' when there are no episodes, the season number for a full season, a single episode number, or a first-to-last episode range. [3]
- **S9** The episode title cell shows '-' without episodes or series, a link for one episode, or a 'Multiple Episodes' popover listing titles for several. [4]
- **S10** The time-left cell shows an info tooltip for delayed items (delaying until) and for download-client-unavailable items (retrying on); '-' if no time left or status is completed or failed; otherwise the formatted time left with remaining/total size as hover title. [5]
- **S11** The progress cell shows a progress bar with percentage (one decimal) when progress is non-zero. [2]
- **S12** The remove dialog title and message vary by whether it is for one named item, one selected item, or several selected items. [6]

## Actions

- **A1** Refresh runs the RefreshMonitoredDownloads command. [1][7]
- **A2** Grab Selected sends the selected ids to the bulk grab call. [1][8] → [`endpoint:Sonarr.Api.V3|POST /api/v3/queue/grab/bulk`](../backend/sonarr.api.v3-c45bd0/post-api-v3-queue-grab-bulk-c19316.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/queue/grab/bulk`](../backend/sonarr.api.v5-c79e7b/post-api-v5-queue-grab-bulk-5ab7cd.md)
- **A3** Remove Selected opens a confirmation dialog; confirming sends the selected ids to the bulk delete call and closes the dialog. [1][9] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/Queue/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-queue-bulk-6deed0.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/Queue/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-queue-bulk-b22407.md)
- **A4** Import Selected opens interactive import for the download ids of the selected completed items. [1]
- **A5** A row's Grab button grabs that single queue item. [2][10] → [`endpoint:Sonarr.Api.V3|POST /api/v3/queue/grab/{id:int}`](../backend/sonarr.api.v3-c45bd0/post-api-v3-queue-grab-id-int-99d425.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/queue/grab/{id:int}`](../backend/sonarr.api.v5-c79e7b/post-api-v5-queue-grab-id-int-410f1f.md)
- **A6** A row's remove button (Remove from queue) opens the remove dialog; confirming removes that item. [2][11]
- **A7** A row's interactive import button opens interactive import for that item's download. [2]
- **A8** In the remove dialog the user picks a removal method and a blocklist method, then clicks Remove or Close. [6]
- **A9** The user can select a filter, sort by a column, change table options and page size, and change page. [1]
- **A10** The select-all checkbox selects or unselects all rows. [1]

## Rules

- **R1** Grab Selected is disabled unless something is selected and at least one selected item has status 'delay'. [1]
- **R2** Remove Selected is disabled when nothing is selected; Import Selected is disabled when nothing is selected or no selected item is completed. [1]
- **R3** A row's Grab button shows only for items with status 'delay' or 'downloadClientUnavailable'; the interactive import button shows only for completed items with tracked download status 'warning'. [2]
- **R4** The maximum page size in table options is 200; changing page size returns to page 1. [1]
- **R5** The queue is fetched from /queue with page, page size, sort key, sort direction and the selected filters; total pages is at least 1. [12][13]
- **R6** Episodes are loaded for the unique episode ids of the queue records; the table shows once episodes are fetched, or when there are no records or none have episodes. [1][14]
- **R7** The visible page is not re-rendered while a remove or import dialog is open. [1]
- **R8** In the remove dialog, Change category is disabled unless the download client has a post-import category, and Ignore is disabled unless the series and episodes are known. [6][1]
- **R9** For pending items the removal method choice is hidden and Blocklist and search is disabled. [6]
- **R10** The Refresh command is ignored if the same command was run within the last 5 seconds. [7]
- **R11** Page number resets to 1 when navigating back (POP). [15]
- **R12** Queue status count is the total count when unknown-series items are included, otherwise the count. [16]

## Side effects

- **E1** Successful bulk or single remove and grab calls invalidate the cached queue so it reloads. [9][11][8][10]
- **E2** Remove calls send the chosen removal method and blocklist method as query string, using DELETE. [9][17]
- **E3** Choosing removal or blocklist options in the dialog saves them as queue options. [6]
- **E4** Refresh posts a command to /command and stores the returned command. [7]
- **E5** The page registers a page populator that refetches the queue, removed on leaving. [1][18]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime
- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Activity/Queue/Queue.tsx#QueueContent`
2. `ts|frontend/src/Activity/Queue/QueueRow.tsx#QueueRow`
3. `ts|frontend/src/Activity/Queue/EpisodeCellContent.tsx#EpisodeCellContent`
4. `ts|frontend/src/Activity/Queue/EpisodeTitleCellContent.tsx#EpisodeTitleCellContent`
5. `ts|frontend/src/Activity/Queue/TimeLeftCell.tsx#TimeLeftCell`
6. `ts|frontend/src/Activity/Queue/RemoveQueueItemModal.tsx#RemoveQueueItemModal`
7. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
8. `ts|frontend/src/Activity/Queue/useQueue.ts#useGrabQueueItems`
9. `ts|frontend/src/Activity/Queue/useQueue.ts#useRemoveQueueItems`
10. `ts|frontend/src/Activity/Queue/useQueue.ts#useGrabQueueItem`
11. `ts|frontend/src/Activity/Queue/useQueue.ts#useRemoveQueueItem`
12. `ts|frontend/src/Activity/Queue/useQueue.ts#useQueue`
13. `ts|frontend/src/Helpers/Hooks/usePagedApiQuery.ts#usePagedApiQuery`
14. `ts|frontend/src/Utilities/Object/selectUniqueIds.ts#selectUniqueIds`
15. `ts|frontend/src/Helpers/Hooks/usePage.ts#usePage`
16. `ts|frontend/src/Activity/Queue/Status/useQueueStatus.ts#useQueueStatus`
17. `ts|frontend/src/Activity/Queue/useQueue.ts#useRemovalOptions`
18. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`

<sub>commit 0bffefe · inputs d5df142 · slice of 546 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
