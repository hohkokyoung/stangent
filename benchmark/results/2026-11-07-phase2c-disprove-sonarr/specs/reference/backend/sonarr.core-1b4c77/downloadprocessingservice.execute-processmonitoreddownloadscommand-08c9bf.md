# Process Monitored Downloads

`Sonarr.Core|M:NzbDrone.Core.Download.DownloadProcessingService.Execute(NzbDrone.Core.Download.ProcessMonitoredDownloadsCommand)` · backend · Sonarr.Core

From the code: DownloadProcessingService.Execute(ProcessMonitoredDownloadsCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** On a ProcessMonitoredDownloadsCommand, the service takes all tracked downloads that are trackable and processes each one in turn. [1][2]
- **A2** A download in ImportPending state is imported when completed download handling is enabled in the configuration. [1][3]
- **A3** A download in FailedPending state is passed to failed download processing. [1][4]

## Rules

- **R1** Only tracked downloads flagged as trackable are processed. [1]
- **R2** Completed items are processed before failed ones, so a failed import can change state and be handled in the same run. [1]
- **R3** An error while processing one download is logged at debug level and does not stop the other downloads. [1]
- **R4** Import marks the download as blocked with a warning if the download cannot be parsed (no remote episode), and sets state Importing before running the import. [3]
- **R5** If no files are eligible for import, the download gets the warning "No files found are eligible for import in {path}". [3]
- **R6** If some results were not imported, a warning "One or more episodes expected in this release were not imported or missing from the release" is added with per-file errors, and the import is blocked. [3]
- **R7** Failed processing only acts on FailedPending downloads that have grabbed history for their download id; otherwise it does nothing. [4][5]
- **R8** The failure message is "Encrypted download detected" if the item is encrypted, the client's message if it failed with a non-blank message, otherwise "Failed download detected". [4]
- **R9** Only imported downloads whose client item can be removed are announced as removable. [6]

## Side effects

- **E1** After processing, a DownloadCanBeRemovedEvent is published for each imported download that can be removed. [6][7]
- **E2** A DownloadsProcessedEvent is published at the end of every run; download monitoring, extras and notifications handle it. [1][8][9][10]
- **E3** Failed processing sets the download state to Failed and publishes a DownloadFailedEvent with series, episodes, quality, message and source from the grabbed history. [4][11]
- **E4** DownloadFailedEvent is handled by blocklisting, download history, history, redownload and the download event hub. [12][13][14][15][16]

## Couldn't see

- the depth limit left out 14 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadProcessingService.Execute(NzbDrone.Core.Download.ProcessMonitoredDownloadsCommand)`
2. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.GetTrackedDownloads`
3. `Sonarr.Core|M:NzbDrone.Core.Download.CompletedDownloadService.Import(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.ProcessFailed(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload)`
5. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.GetGrabbedHistory(System.String)`
6. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadProcessingService.RemoveCompletedDownloads`
7. [`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadCanBeRemovedEvent)`](../../backend/sonarr.core-1b4c77/downloadeventhub.handle-downloadcanberemovedevent-8c8bce.md)
8. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.Download.DownloadsProcessedEvent)`](../../backend/sonarr.core-1b4c77/downloadmonitoringservice.handle-downloadsprocessedevent-fba69c.md)
9. [`Sonarr.Core|M:NzbDrone.Core.Extras.ExtraService.Handle(NzbDrone.Core.Download.DownloadsProcessedEvent)`](../../backend/sonarr.core-1b4c77/extraservice.handle-downloadsprocessedevent-c06f5e.md)
10. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.HandleAsync(NzbDrone.Core.Download.DownloadsProcessedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handleasync-downloadsprocessedevent-04f52b.md)
11. `Sonarr.Core|M:NzbDrone.Core.Download.FailedDownloadService.PublishDownloadFailedEvent(NzbDrone.Core.History.EpisodeHistory,System.Collections.Generic.List{System.Int32},System.String,System.String,NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.Boolean)`
12. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handle-downloadfailedevent-ad8e80.md)
13. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadfailedevent-d3fd1e.md)
14. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-downloadfailedevent-b3cd3e.md)
15. [`Sonarr.Core|M:NzbDrone.Core.Download.RedownloadFailedDownloadService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/redownloadfaileddownloadservice.handle-downloadfailedevent-e106a5.md)
16. [`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`](../../backend/sonarr.core-1b4c77/downloadeventhub.handle-downloadfailedevent-338121.md)

<sub>commit 0bffefe · inputs e0e05a2 · slice of 119 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
