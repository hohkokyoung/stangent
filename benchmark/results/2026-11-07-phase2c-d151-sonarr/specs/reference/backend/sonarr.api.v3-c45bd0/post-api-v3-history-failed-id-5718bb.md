# Mark history item as failed (API)

`endpoint:Sonarr.Api.V3|POST /api/v3/History/failed/{id}` · backend · Sonarr.Api.V3

From the code: POST /api/v3/History/failed/{id}

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an empty object on success. [1]

## Actions

- **A1** POST /api/v3/History/failed/{id} marks the history item with the given id as a failed download. [1][2] → `endpoint:Sonarr.Api.V3|POST /api/v3/History/failed/{id}`

## Rules

- **R1** The failure message defaults to "Manually marked as failed" when none is given. [2]
- **R2** If the history item has no download ID and is a Grabbed event, a failure is published for that item's episode only. [2][3]
- **R3** If the history item has no download ID and is not Grabbed, it fails with "Unable to mark download as failed, history item was not grabbed and has no download ID". [2]
- **R4** If the item has a download ID, all Grabbed history for that download ID (newest first) plus the item itself if grabbed are collected, de-duplicated by id, and the first is used for the failure. [2][4]
- **R5** The failure covers the distinct episode IDs of all collected grabbed history items. [5]
- **R6** If no grabbed history is found for the download ID, it fails with "Unable to mark download as failed, no grabbed history available". [2]

## Side effects

- **E1** Publishes a DownloadFailedEvent carrying series, episodes, quality, source title, download client, download ID, message, languages and release source from the history item. [6]
- **E2** The blocklist service handles the failed-download event. [7]
- **E3** The history service handles the failed-download event. [8]
- **E4** The download history service handles the failed-download event. [9]
- **E5** The download event hub handles the failed-download event. [10]
- **E6** The redownload service handles the failed-download event, running last. [11]

## Couldn't see

- the depth limit left out 32 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.MarkAsFailed(System.Int32)`
2. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.MarkAsFailed(System.Int32,System.String,System.String,System.Boolean)`
3. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNullOrWhiteSpace(System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.GetGrabbedHistory(System.String)`
5. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.GetEpisodeIds(System.Collections.Generic.List{NzbDrone.Core.History.EpisodeHistory})`
6. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.PublishDownloadFailedEvent(NzbDrone.Core.History.EpisodeHistory,System.Collections.Generic.List{System.Int32},System.String,System.String,NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.Boolean)`
7. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handle-downloadfailedevent-ad8e80.md)
8. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-downloadfailedevent-b3cd3e.md)
9. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadfailedevent-d3fd1e.md)
10. [`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/downloadeventhub.handle-downloadfailedevent-338121.md)
11. [`Sonarr.Core|M:NzbDrone.Core.Download.RedownloadFailedDownloadService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/redownloadfaileddownloadservice.handle-downloadfailedevent-e106a5.md)

<sub>commit 0bffefe · inputs 64cf96a · slice of 22 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
