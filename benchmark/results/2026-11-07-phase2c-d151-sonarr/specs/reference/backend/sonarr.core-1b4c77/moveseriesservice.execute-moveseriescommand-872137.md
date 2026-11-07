# Move Series (backend command)

`Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.Execute(NzbDrone.Core.Tv.Commands.MoveSeriesCommand)` · backend · Sonarr.Core

From the code: MoveSeriesService.Execute(MoveSeriesCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** Executing a move-series command loads the series by the command's series id and moves it from the command's source path to its destination path. [1][2][3]

## Rules

- **R1** If the source path is not a valid path for the current OS, the series is not moved, a warning is logged, and the series path is set to the destination path. [3][4]
- **R2** If the source folder does not exist, no files are moved and the series path is updated to the destination path, whether or not the destination folder exists. A debug message is logged if it exists, a warning if not. [3][5]
- **R3** If the source and destination paths are equal, nothing is moved and the series path is set to the destination path. [3][6]
- **R4** If the move fails with an IO error, the error is logged and the series path is reset to the source path. Any other exception also resets the path to the source and is rethrown. [3][7]
- **R5** Each path update sets the series Path to the given path and clears its PendingPath. [7]

## Side effects

- **E1** A real move creates the parent folder of the destination, then moves the source folder to the destination using move transfer mode. [3][8][9]
- **E2** After a successful move, the series is saved with the new path and a SeriesMovedEvent with the series, source and destination paths is published. [3][10][11][12]
- **E3** Progress messages are logged when a move starts and when it succeeds. [3][13]

## Couldn't see

- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.Execute(NzbDrone.Core.Tv.Commands.MoveSeriesCommand)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Int32)`
3. `Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.MoveSingleSeries(NzbDrone.Core.Tv.Series,System.String,System.String,System.Nullable{System.Int32},System.Nullable{System.Int32})`
4. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.IsPathValid(System.String,NzbDrone.Common.Disk.PathValidationType)`
5. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.PathEquals(System.String,System.String,System.Nullable{System.StringComparison})`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.UpdatePath(System.Int32,System.String)`
8. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.CreateFolder(System.String)`
9. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskTransferService.TransferFolder(System.String,System.String,NzbDrone.Common.Disk.TransferMode)`
10. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
11. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesMovedEvent.#ctor(NzbDrone.Core.Tv.Series,System.String,System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.UpdateSeries(NzbDrone.Core.Tv.Series,System.Boolean,System.Boolean)`
13. `Sonarr.Common|M:NzbDrone.Common.Instrumentation.Extensions.LoggerExtensions.ProgressInfo(NLog.Logger,System.String,System.Object[])`

<sub>commit 0bffefe · inputs 492d03d · slice of 57 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
