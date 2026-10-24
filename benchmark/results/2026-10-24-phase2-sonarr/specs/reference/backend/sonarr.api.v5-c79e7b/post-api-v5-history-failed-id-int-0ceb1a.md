# Mark history item as failed (API)

`endpoint:Sonarr.Api.V5|POST /api/v5/History/failed/{id:int}` · backend · Sonarr.Api.V5

From the code: POST /api/v5/History/failed/{id:int}

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an empty 204 No Content response on success. [1]

## Actions

- **A1** Marks the history item with the given id as a failed download, using the default message and no source, and with redownload allowed. [1][2] → `endpoint:Sonarr.Api.V5|POST /api/v5/History/failed/{id:int}`

## Rules

- **R1** If no message is given, the failure message is "Manually marked as failed". [2]
- **R2** If the history item has no download ID and is a Grabbed item, a failure is published for just that item's episode. [2][3]
- **R3** If the history item has no download ID and is not Grabbed, the call fails with "Unable to mark download as failed, history item was not grabbed and has no download ID". [2]
- **R4** If there is a download ID, the Grabbed history items with that ID are gathered (the given item first if Grabbed, then others newest first), de-duplicated by id, and the first one is used for the failure event with all their distinct episode ids. [2][4][5]
- **R5** If there is a download ID but no Grabbed history is found, the call fails with "Unable to mark download as failed, no grabbed history available". [2]

## Side effects

- **E1** Publishes a DownloadFailedEvent carrying the series, episodes, quality, source title, download client, download ID, message, languages, release source and history data. [6]
- **E2** The event is handled by the blocklist service, the download event hub, the download history service, the redownload-failed-download service and the history service. [7][8][9][10][11]

## Couldn't see

- the depth limit left out 32 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryController.MarkAsFailed(System.Int32)`
2. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.MarkAsFailed(System.Int32,System.String,System.String,System.Boolean)`
3. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNullOrWhiteSpace(System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.GetGrabbedHistory(System.String)`
5. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.GetEpisodeIds(System.Collections.Generic.List{NzbDrone.Core.History.EpisodeHistory})`
6. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.PublishDownloadFailedEvent(NzbDrone.Core.History.EpisodeHistory,System.Collections.Generic.List{System.Int32},System.String,System.String,NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.Boolean)`
7. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handle-downloadfailedevent-ad8e80.md)
8. [`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/downloadeventhub.handle-downloadfailedevent-338121.md)
9. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadfailedevent-d3fd1e.md)
10. [`Sonarr.Core|M:NzbDrone.Core.Download.RedownloadFailedDownloadService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/redownloadfaileddownloadservice.handle-downloadfailedevent-e106a5.md)
11. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-downloadfailedevent-b3cd3e.md)

<sub>commit 0bffefe · inputs 734c6da · slice of 22 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
