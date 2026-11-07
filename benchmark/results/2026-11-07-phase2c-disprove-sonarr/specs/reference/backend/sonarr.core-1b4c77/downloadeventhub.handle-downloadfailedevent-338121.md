# Remove failed download from download client

`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadFailedEvent)` · backend · Sonarr.Core

From the code: DownloadEventHub.Handle(DownloadFailedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a download-failed event arrives, the handler may remove the failed download from its download client and stop tracking it. [1][2]

## Rules

- **R1** Nothing happens if the event has no tracked download or the download item cannot be removed. [1]
- **R2** Nothing happens unless the download client definition has RemoveFailedDownloads enabled. [1]
- **R3** The download is removed from the client with deleteData set to true. [2]
- **R4** If the client does not support removal, a warning is logged and nothing else happens. Any other error is logged as an error and not rethrown. [2]
- **R5** Pneumatic does not support removal and throws NotSupportedException. [3]
- **R6** The Torrent and Usenet Blackhole clients refuse removal without deleting data, and otherwise delete the item data. [4][5]
- **R7** Sabnzbd removes the item from the queue if it is still queued, otherwise from history. [6]

## Side effects

- **E1** The item is removed in the download client, with the client-specific behaviour of its RemoveItem implementation. [7][8]
- **E2** Stopping tracking removes the download from the tracked-downloads cache. [9]
- **E3** A TrackedDownloadsRemovedEvent is published containing the removed tracked download. [9][10]
- **E4** DownloadMonitoringService handles the TrackedDownloadsRemovedEvent. [11]

## Couldn't see

- the depth limit left out 77 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.RemoveFromDownloadClient(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,NzbDrone.Core.Download.IDownloadClient)`
3. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Pneumatic.Pneumatic.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Blackhole.TorrentBlackhole.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
5. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Blackhole.UsenetBlackhole.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Sabnzbd.Sabnzbd.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Download.IDownloadClient.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
8. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadClientBase`1.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
9. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.StopTracking(System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadsRemovedEvent.#ctor(System.Collections.Generic.List{NzbDrone.Core.Download.TrackedDownloads.TrackedDownload})`
11. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadsRemovedEvent)`](../../backend/sonarr.core-1b4c77/downloadmonitoringservice.handle-trackeddownloadsremovedevent-3cf177.md)

<sub>commit 0bffefe · inputs b2de40b · slice of 194 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
