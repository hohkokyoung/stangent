# Download history: record ignored download

`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadIgnoredEvent)` · backend · Sonarr.Core

From the code: DownloadHistoryService.Handle(DownloadIgnoredEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** On a download-ignored event, creates a download history entry of type DownloadIgnored and saves it. [1]

## Rules

- **R1** The entry copies series id, download id and source title from the event, and takes protocol and download client id from the event's download client info. [1]
- **R2** The entry date is the current UTC time. [1]
- **R3** The entry's data holds two values: DownloadClient (the client type) and DownloadClientName (the client name). [1][2]

## Side effects

- **E1** Inserts a new download history record into the repository for each ignored download. [1][3]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadIgnoredEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistory.#ctor`
3. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`

<sub>commit 0bffefe · inputs d3ce17a · slice of 27 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
