# Update episode file dates after series scan

`Sonarr.Core|M:NzbDrone.Core.MediaFiles.UpdateEpisodeFileService.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScannedEvent)` · backend · Sonarr.Core

From the code: UpdateEpisodeFileService.Handle(SeriesScannedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a series scan finishes, loads the series' episodes that have files, groups them by episode file, and tries to change each file's last-write date to the air date of its first episode. [1][2][3]

## Rules

- **R1** Nothing is done when the file date setting is None. [1]
- **R2** A file is not changed when its first episode has no air date. [4]
- **R3** With the local air date setting the air date is converted to local time. With the UTC air date setting the UTC air date is used as if it were local time. [4]
- **R4** The file date is only set when it differs from the current last-write time by at least a second; sub-second ticks of the old time are kept. [5][6][7]
- **R5** On non-Windows systems, an air date before 1970-01-01 is replaced with 1970-01-01. [5]
- **R6** A failure to set the file date is logged as a warning and the file is counted as not changed. [5]

## Side effects

- **E1** Changes the last-write time of episode files on disk. [5][8]
- **E2** Logs a progress message with the number of files changed out of the total for the series, or that no file dates changed. [1][9]

## Couldn't see

- the depth limit left out 21 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.UpdateEpisodeFileService.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScannedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.EpisodesWithFiles(System.Int32)`
3. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeRepository.EpisodesWithFiles(System.Int32)`
4. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.UpdateEpisodeFileService.ChangeFileDate(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode})`
5. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.UpdateEpisodeFileService.ChangeFileDateToLocalDate(System.String,System.DateTime)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.DateTimeExtensions.WithTicksFrom(System.DateTime,System.DateTime)`
7. `Sonarr.Common|M:NzbDrone.Common.Extensions.DateTimeExtensions.WithoutTicks(System.DateTime)`
8. `Sonarr.Common|M:NzbDrone.Common.Disk.DiskProviderBase.FileSetLastWriteTime(System.String,System.DateTime)`
9. `Sonarr.Common|M:NzbDrone.Common.Instrumentation.Extensions.LoggerExtensions.ProgressDebug(NLog.Logger,System.String,System.Object[])`

<sub>commit 0bffefe · inputs d4c9068 · slice of 79 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
