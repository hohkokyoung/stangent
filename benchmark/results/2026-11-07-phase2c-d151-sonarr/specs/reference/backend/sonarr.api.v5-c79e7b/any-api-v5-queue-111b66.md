# Remove queue item (DELETE /api/v5/Queue/{id})

`endpoint:Sonarr.Api.V5|ANY /api/v5/Queue` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Queue

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns 204 No Content when the queue item is removed, or 404 Not Found when no matching queue item or tracked download exists. [1]

## Actions

- **A1** Removes a queue item by id, with query options removalMethod, blocklistMethod and an optional message. A pending release is removed and blocklisted if requested. [1][2] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Queue`
- **A2** If the id is not a pending release, the matching tracked download is removed according to removalMethod and blocklistMethod, then it stops being tracked. [1][3] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Queue`

## Rules

- **R1** removalMethod and blocklistMethod are both required; a missing one gives a bad request ('removalMethod must be provided' or 'blocklistMethod must be provided'). [4]
- **R2** The pending releases are checked first; only if none matches the id is the queue and tracked downloads looked up. [1]
- **R3** A not-found error is raised if the queue item or its tracked download is not found. [5]
- **R4** A pending release is blocklisted unless blocklistMethod is DoNotBlocklist. The default message is 'Pending release manually blocklisted'. [2]
- **R5** With removalMethod RemoveFromClient, the item is removed from the download client with its data deleted. With ChangeCategory, it is marked as imported in the client. A missing download client gives a bad request. [3]
- **R6** Marking an item as imported throws 'does not support marking items as imported' unless the client overrides it. [6]
- **R7** Marking as failed requires grabbed history for the download; otherwise it fails with 'Unable to mark download as failed, no grabbed history available'. The default message is 'Manually marked as failed'. [7]
- **R8** A tracked download is ignored only when removalMethod is Ignore and blocklisting is not requested. If the series is unknown, ignoring logs a warning and does nothing; the request still stops tracking and returns 204. [3][8][1]
- **R9** Skip-redownload is set on failure marking only when blocklistMethod is BlocklistOnly. [3]
- **R10** The request source recorded is the X-Sonarr-Client header if present, otherwise it comes from the User-Agent, or 'Other'. [9][10]

## Side effects

- **E1** Blocklisting a pending release inserts a blocklist record with series, episodes, release details, message and source. [11]
- **E2** Removing a pending release deletes all pending releases of that series with the same season and episode numbers. [12]
- **E3** Ignoring a download publishes a DownloadIgnoredEvent with message 'Manually ignored', handled by the download history and history services. [8][13][14]
- **E4** Stopping tracking removes the download from the tracked-download cache and publishes a TrackedDownloadsRemovedEvent, handled by the download monitoring service. [15][16]

## Couldn't see

- the depth limit left out 135 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R8 (unsupported): When the series is unknown, IgnoreDownload only logs a warning and returns false. Remove then returns null. Nothing throws or fails. RemoveAction goes on to stop tracking and return 204. The word 'fails' is not supported. **fixed**: Reworded: unknown series only logs a warning and returns false; the request continues to stop tracking and return 204. Removed 'fails'.

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Queue.QueueController.RemoveAction(System.Int32,System.Nullable{Sonarr.Api.V5.Queue.QueueRemovalMethod},System.Nullable{Sonarr.Api.V5.Queue.QueueBlocklistMethod},System.String)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Queue.QueueController.Remove(NzbDrone.Core.Queue.Queue,System.String,Sonarr.Api.V5.Queue.QueueBlocklistMethod)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Queue.QueueController.Remove(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.String,Sonarr.Api.V5.Queue.QueueRemovalMethod,Sonarr.Api.V5.Queue.QueueBlocklistMethod)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Queue.QueueController.ValidateRemovalOptions(System.Nullable{Sonarr.Api.V5.Queue.QueueRemovalMethod},System.Nullable{Sonarr.Api.V5.Queue.QueueBlocklistMethod})`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Queue.QueueController.GetTrackedDownload(System.Int32)`
6. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadClientBase`1.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
7. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.MarkAsFailed(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.String,System.String,System.Boolean)`
8. `Sonarr.Core|M:NzbDrone.Core.Download.IgnoredDownloadService.IgnoreDownload(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload)`
9. `Sonarr.Http|M:Sonarr.Http.Extensions.RequestExtensions.GetSource(Microsoft.AspNetCore.Http.HttpRequest)`
10. `Sonarr.Common|M:NzbDrone.Common.Http.UserAgentParser.ParseSource(System.String)`
11. `Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Block(NzbDrone.Core.Parser.Model.RemoteEpisode,System.String,System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.RemovePendingQueueItems(System.Int32)`
13. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadIgnoredEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadignoredevent-779745.md)
14. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Download.DownloadIgnoredEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-downloadignoredevent-16800f.md)
15. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.StopTracking(System.String)`
16. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadsRemovedEvent)`](../../backend/sonarr.core-1b4c77/downloadmonitoringservice.handle-trackeddownloadsremovedevent-3cf177.md)

<sub>commit 0bffefe · inputs 01a9903 · slice of 105 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
