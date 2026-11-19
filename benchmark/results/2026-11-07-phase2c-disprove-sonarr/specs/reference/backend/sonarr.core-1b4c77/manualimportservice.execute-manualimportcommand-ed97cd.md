# Manual import of episode files

`Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.Execute(NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportCommand)` · backend · Sonarr.Core

From the code: ManualImportService.Execute(ManualImportCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** Runs a manual import of the files in the command, one at a time, using the command's import mode, and logs progress such as "Processing file X of Y" and "Manually imported N files". [1][2]

## Rules

- **R1** Each file is imported with the series and episodes chosen by the user (series id and episode ids). The release group, quality, indexer flags and release type chosen by the user override values detected by the aggregation step. [1][3][4]
- **R2** The user-chosen languages replace the detected languages only when at least one language is given and all of them are non-null and valid. [1][5]
- **R3** A file counts as an existing file when its path is inside the series path. Otherwise it is treated as a new download. [1][6]
- **R4** File and folder names are parsed for episode info. If the path can't be parsed, an empty parsed info is used. [1][7][8]
- **R5** When a folder name is given, it is parsed as the folder episode info. The file is marked as a scene source when it is not an existing file. [1][9]
- **R6** When a download id is given, the tracked download is looked up and its parsed info and download item are attached to the file. [1][10]
- **R7** Custom formats are recalculated for the file before import. [1][11]
- **R8** A file without a tracked download is imported as a new download only if it is not an existing file. A file with a tracked download is always imported as a new download. [1][12]
- **R9** A tracked download's output folder is deleted only if it exists, it is judged safe to delete, and the download item can move files. [1][13][14]
- **R10** A folder is judged safe to delete only if all its video files are samples and no RAR file larger than 10 MB is present. Unparseable files, non-sample files, RAR files over 10 MB, a missing folder or any error all mean it is kept. [15]
- **R11** A tracked download is marked Imported only when the number of imported episodes is at least the number of episodes in its remote episode (minimum 1). [1]

## Side effects

- **E1** Each file is imported through the import step, which imports the file for the chosen series and episodes. [1][12]
- **E2** For successfully imported files without a tracked download, one UntrackedDownloadCompletedEvent is published per series and season. The event carries the files' longest common source path. Notification handling listens for it. [1][16][17][18]
- **E3** When all episodes of a tracked download are imported, its state is set to Imported and a DownloadCompletedEvent is published. The download event hub, download history and notifications handle it. [1][19][20][21][22]
- **E4** The output folder of a tracked download is deleted recursively from disk when it passes the deletion checks. [1][23]

## Couldn't see

- the depth limit left out 42 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.Execute(NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportCommand)`
2. `Sonarr.Common|M:NzbDrone.Common.Instrumentation.Extensions.LoggerExtensions.ProgressTrace(NLog.Logger,System.String,System.Object[])`
3. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Int32)`
4. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodes(System.Collections.Generic.IEnumerable{System.Int32})`
5. `Sonarr.Core|M:NzbDrone.Core.Languages.Language.IsValid(System.Boolean)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.IsParentPath(System.String,System.String)`
7. `Sonarr.Core|M:NzbDrone.Core.Parser.Parser.ParsePath(System.String)`
8. `Sonarr.Core|M:NzbDrone.Core.Parser.Model.ParsedEpisodeInfo.#ctor`
9. `Sonarr.Core|M:NzbDrone.Core.Parser.Parser.ParseTitle(System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.ITrackedDownloadService.Find(System.String)`
11. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.ILocalEpisodeCustomFormatCalculationService.UpdateEpisodeCustomFormats(NzbDrone.Core.Parser.Model.LocalEpisode)`
12. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.IImportApprovedEpisodes.Import(System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeImport.ImportDecision},System.Boolean,NzbDrone.Core.Download.DownloadClientItem,NzbDrone.Core.MediaFiles.EpisodeImport.ImportMode)`
13. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
14. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IDownloadedEpisodesImportService.ShouldDeleteFolder(System.IO.DirectoryInfo,NzbDrone.Core.Tv.Series)`
15. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.DownloadedEpisodesImportService.ShouldDeleteFolder(System.IO.DirectoryInfo,NzbDrone.Core.Tv.Series)`
16. `Sonarr.Core|M:NzbDrone.Core.Download.UntrackedDownloadCompletedEvent.#ctor(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile},NzbDrone.Core.Parser.Model.ParsedEpisodeInfo,System.String)`
17. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.GetLongestCommonPath(System.Collections.Generic.List{System.String})`
18. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.UntrackedDownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-untrackeddownloadcompletedevent-063048.md)
19. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadCompletedEvent.#ctor(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile},NzbDrone.Core.Parser.Model.GrabbedReleaseInfo)`
20. [`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/downloadeventhub.handle-downloadcompletedevent-42a2f6.md)
21. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadcompletedevent-4ec245.md)
22. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-downloadcompletedevent-dc9c2f.md)
23. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.DeleteFolder(System.String,System.Boolean)`

<sub>commit 0bffefe · inputs 6797b94 · slice of 300 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
