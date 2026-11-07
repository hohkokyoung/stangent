# Rescan Series (disk scan)

`Sonarr.Core|M:NzbDrone.Core.MediaFiles.DiskScanService.Execute(NzbDrone.Core.MediaFiles.Commands.RescanSeriesCommand)` · backend · Sonarr.Core

From the code: DiskScanService.Execute(RescanSeriesCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** Rescans a single series when the command carries a series id. [1][2]
- **A2** Rescans every series, one after another, when the command has no series id. [1][3]

## Rules

- **R1** If the series folder is missing and its root folder does not exist, the scan is skipped with reason RootFolderDoesNotExist and a warning is logged. [4]
- **R2** If the series folder is missing and the root folder is empty, the scan is skipped with reason RootFolderIsEmpty and a warning is logged. [4]
- **R3** A missing series folder is created only when the create-empty-series-folders setting is on and the delete-empty-folders setting is off. [4]
- **R4** When the series folder is missing (and not skipped), media files are cleaned against an empty file list and scanning is completed with no extra files. [4]
- **R5** Only video files not already mapped to the series are sent for import decisions. [4]
- **R6** Existing episode files whose size on disk differs from the stored size get the new size; media info is updated, and files where that update does not occur are saved. [4]

## Side effects

- **E1** Publishes a SeriesScanSkippedEvent when a scan is skipped; a handler in the series scanned handler reacts to it. [4][5]
- **E2** Approved import decisions are imported as episode files. [4]
- **E3** May create the missing series folder and set its permissions. [4]
- **E4** Updates stored size and media info of episode files whose size changed. [4]
- **E5** Removes the series folder if empty, then collects non-video files as possible extra files when the folder still exists. [4]

## Couldn't see

- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.DiskScanService.Execute(NzbDrone.Core.MediaFiles.Commands.RescanSeriesCommand)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Int32)`
3. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetAllSeries`
4. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.DiskScanService.Scan(NzbDrone.Core.Tv.Series)`
5. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesScannedHandler.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScanSkippedEvent)`](../../backend/sonarr.core-1b4c77/seriesscannedhandler.handle-seriesscanskippedevent-254546.md)

<sub>commit 0bffefe · inputs 1c2ed64 · slice of 33 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
