# Remove queue item

`endpoint:Sonarr.Api.V3|ANY /api/v3/Queue` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Queue

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns no body on success; a missing queue item or tracked download gives a 404 Not Found error. [1][2]
- **S2** Returns 400 Bad Request when the download client for the item cannot be found while removing from the client or changing category. [3][4]

## Actions

- **A1** Removes a queue item by id. Options: removeFromClient, blocklist, skipRedownload, changeCategory. [5] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Queue`
- **A2** If the id matches a pending release, it is removed (and optionally blocklisted) and nothing else happens. [5][6]

## Rules

- **R1** Defaults: removeFromClient true, blocklist false, skipRedownload false, changeCategory false. [5]
- **R2** Pending releases are found by matching the id among the pending queue items. [7]
- **R3** Otherwise the queue item is looked up by id, then its tracked download by download id; either missing gives Not Found. [1]
- **R4** If removeFromClient is true, the item is removed from the download client with data deleted. Otherwise, if changeCategory is true, the item is marked as imported in the client. [3]
- **R5** Marking as imported throws a not-supported error for clients that do not support it. [8]
- **R6** Marking as failed throws an error if no grabbed history exists for the download. [9]
- **R7** The item is ignored only when removeFromClient, blocklist and changeCategory are all false; ignoring fails (returns false) when the series is unknown. [3][10]

## Side effects

- **E1** With blocklist on a pending release, a blocklist entry is saved with message "Pending release manually blocklisted". [6][11]
- **E2** Removing a pending release deletes the stored pending releases of the same series with the same season and episode numbers. [12]
- **E3** With blocklist on a tracked download, the download is marked as failed (message defaults to "Manually marked as failed"), passing skipRedownload. [3][9]
- **E4** Ignoring publishes a DownloadIgnoredEvent with message "Manually ignored", handled by history services. [10][13][14]
- **E5** After removing a tracked download, tracking stops: it is removed from the cache and a TrackedDownloadsRemovedEvent is published. [15][16]

## Couldn't see

- the depth limit left out 133 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueController.GetTrackedDownload(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.NotFoundException.#ctor(System.Object)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueController.Remove(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.Boolean,System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueController.RemoveAction(System.Int32,System.Boolean,System.Boolean,System.Boolean,System.Boolean)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueController.Remove(NzbDrone.Core.Queue.Queue,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.FindPendingQueueItemObsolete(System.Int32)`
8. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadClientBase`1.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
9. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.MarkAsFailed(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.String,System.String,System.Boolean)`
10. `Sonarr.Core|M:NzbDrone.Core.Download.IgnoredDownloadService.IgnoreDownload(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload)`
11. `Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Block(NzbDrone.Core.Parser.Model.RemoteEpisode,System.String,System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.RemovePendingQueueItemsObsolete(System.Int32)`
13. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Download.DownloadIgnoredEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-downloadignoredevent-16800f.md)
14. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadIgnoredEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadignoredevent-779745.md)
15. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.StopTracking(System.String)`
16. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadsRemovedEvent)`](../../backend/sonarr.core-1b4c77/downloadmonitoringservice.handle-trackeddownloadsremovedevent-3cf177.md)

<sub>commit 0bffefe · inputs ddcab84 · slice of 99 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
