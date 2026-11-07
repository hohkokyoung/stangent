# Download completed handler (mark imported and remove from client)

`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)` · backend · Sonarr.Core

From the code: DownloadEventHub.Handle(DownloadCompletedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** On a download-completed event, gets the download client for the tracked download and marks the item as imported in that client. [1][2][3]
- **A2** After marking, removes the item from the download client (with data deletion requested) and stops tracking the download, when the removal conditions are met. [1][4]

## Rules

- **R1** The item is removed only if it can be removed, its status is not Downloading, and the client definition has RemoveCompletedDownloads enabled. [1]
- **R2** Marking as imported happens before the removal checks, regardless of them. [1]
- **R3** By default a client does not support marking as imported and throws a not-supported error, which is logged at debug level only. [5][2]
- **R4** Other errors while marking as imported are logged as errors and do not stop the handler. [2]
- **R5** If the client does not support removal, a warning is logged; any other removal error is logged as an error. Neither is rethrown. [4]
- **R6** Deluge, qBittorrent and rTorrent set the post-import label only when an imported category is set and differs from the TV category. [6][7][8]
- **R7** Transmission marks imported only if it supports labels; otherwise it throws not-supported. It replaces the TV category label with the imported category. [9]
- **R8** uTorrent sets the imported label and removes the old TV category label if one is set. [10]
- **R9** Flood adds the configured post-import tags to the torrent's existing tags when any are configured. [11]
- **R10** Pneumatic does not support removal. Blackhole (torrent and usenet) supports removal only with data deletion. [12][13][14]

## Side effects

- **E1** Marking as imported changes the item's label or tags in the download client, for clients that support it. [7][11]
- **E2** Removal deletes the item from the download client, and data too where the client supports it. [4][15]
- **E3** Stopping tracking removes the download from the tracked-download cache and publishes a TrackedDownloadsRemovedEvent. [16][17]
- **E4** The TrackedDownloadsRemovedEvent is handled by the download monitoring service. [18]

## Couldn't see

- the depth limit left out 105 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.MarkItemAsImported(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,NzbDrone.Core.Download.IDownloadClient)`
3. `Sonarr.Core|M:NzbDrone.Core.Download.IProvideDownloadClient.Get(System.Int32)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.RemoveFromDownloadClient(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,NzbDrone.Core.Download.IDownloadClient)`
5. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadClientBase`1.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
6. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Deluge.Deluge.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
7. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.QBittorrent.QBittorrent.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
8. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.RTorrent.RTorrent.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
9. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Transmission.Transmission.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
10. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.UTorrent.UTorrent.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
11. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Flood.Flood.MarkItemAsImported(NzbDrone.Core.Download.DownloadClientItem)`
12. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Pneumatic.Pneumatic.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
13. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Blackhole.TorrentBlackhole.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
14. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Blackhole.UsenetBlackhole.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
15. `Sonarr.Core|M:NzbDrone.Core.Download.Clients.Deluge.Deluge.RemoveItem(NzbDrone.Core.Download.DownloadClientItem,System.Boolean)`
16. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.StopTracking(System.String)`
17. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadsRemovedEvent.#ctor(System.Collections.Generic.List{NzbDrone.Core.Download.TrackedDownloads.TrackedDownload})`
18. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadsRemovedEvent)`](../../backend/sonarr.core-1b4c77/downloadmonitoringservice.handle-trackeddownloadsremovedevent-3cf177.md)

<sub>commit 0bffefe · inputs 4f46320 · slice of 204 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
