# Download monitoring: refresh on episode imported

`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.MediaFiles.Events.EpisodeImportedEvent)` · backend · Sonarr.Core

From the code: DownloadMonitoringService.Handle(EpisodeImportedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When an episode-imported event is received, the service requests a debounced refresh of download monitoring. [1]

## Rules

- **R1** Each trigger marks the debouncer as triggered. [2]
- **R2** The debounce timer is started only when the debouncer is not paused (paused count is 0). [2]

## Side effects

- **E1** The event handler does not refresh directly; it calls the debouncer's Execute, which starts the timer when not paused. [1][2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.DownloadMonitoringService.Handle(NzbDrone.Core.MediaFiles.Events.EpisodeImportedEvent)`
2. `Sonarr.Common|M:NzbDrone.Common.TPL.Debouncer.Execute`

<sub>commit 0bffefe · inputs 48a699c · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
