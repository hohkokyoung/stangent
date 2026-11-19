# Bulk move series to a new root folder

`Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.Execute(NzbDrone.Core.Tv.Commands.BulkMoveSeriesCommand)` · backend · Sonarr.Core

From the code: MoveSeriesService.Execute(BulkMoveSeriesCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Progress messages are logged: "Moving N series to '<root folder>'" at the start, per-series "Moving <title> from '<src>' to '<dest>' (i/total)", and "Finished moving N series to '<root folder>'" at the end. [1][2]
- **S2** A success message "<title> moved successfully to <destination>" is logged after a series folder is moved; an error is logged if an IO error prevents the move. [2]

## Actions

- **A1** Executing the bulk move command moves each listed series, in order, from its source path to a folder under the destination root folder. [1]

## Rules

- **R1** The destination path of each series is the destination root folder combined with the series folder name built from the series by the file name builder. [1][3]
- **R2** If the source path is not valid for the current OS, nothing is moved; a warning is logged and the series path is set to the destination path. [2][4]
- **R3** A path is invalid if empty/whitespace, has invalid characters or path traversal, or (on Windows) has leading/trailing spaces in it or any parent folder name. [4]
- **R4** If the source folder does not exist, no move happens and the series path is updated to the destination path anyway; it is logged as a completed move if the destination exists, otherwise as a warning. [2][5]
- **R5** If the source and destination paths are equal, no move happens; the series path is set to the destination. [2][6]
- **R6** If the move fails with an IO error, the series path is set back to the source path and the error is logged. Any other exception also resets the path to the source and is rethrown. [2]
- **R7** Updating a series path sets its Path to the new path and clears its PendingPath. [7]

## Side effects

- **E1** The destination's parent folder is created if missing, then the series folder is moved (transfer mode Move) from the source to the destination. [2][8][9]
- **E2** After a successful move, the series path is saved and a SeriesMovedEvent with the series, source and destination paths is published. [2][10][11]
- **E3** The series record is reloaded and saved with its new path through the series service. [7][12][13]

## Couldn't see

- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.Execute(NzbDrone.Core.Tv.Commands.BulkMoveSeriesCommand)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.MoveSingleSeries(NzbDrone.Core.Tv.Series,System.String,System.String,System.Nullable{System.Int32},System.Nullable{System.Int32})`
3. `Sonarr.Core|M:NzbDrone.Core.Organizer.IBuildFileNames.GetSeriesFolder(NzbDrone.Core.Tv.Series,NzbDrone.Core.Organizer.NamingConfig)`
4. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.IsPathValid(System.String,NzbDrone.Common.Disk.PathValidationType)`
5. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.PathEquals(System.String,System.String,System.Nullable{System.StringComparison})`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.UpdatePath(System.Int32,System.String)`
8. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.CreateFolder(System.String)`
9. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskTransferService.TransferFolder(System.String,System.String,NzbDrone.Common.Disk.TransferMode)`
10. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
11. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesMovedEvent.#ctor(NzbDrone.Core.Tv.Series,System.String,System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Int32)`
13. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.UpdateSeries(NzbDrone.Core.Tv.Series,System.Boolean,System.Boolean)`

<sub>commit 0bffefe · inputs f28a26f · slice of 61 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
