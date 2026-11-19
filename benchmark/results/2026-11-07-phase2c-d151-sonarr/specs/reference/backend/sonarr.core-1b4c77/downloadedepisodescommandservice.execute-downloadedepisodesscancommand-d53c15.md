# Downloaded episodes scan command

`Sonarr.Core|M:NzbDrone.Core.MediaFiles.DownloadedEpisodesCommandService.Execute(NzbDrone.Core.MediaFiles.Commands.DownloadedEpisodesScanCommand)` · backend · Sonarr.Core

From the code: DownloadedEpisodesCommandService.Execute(DownloadedEpisodesScanCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** Runs a downloaded-episodes scan for a given path: imports episodes found at that folder or file. [1][2]
- **A2** When a download client id is supplied and a matching tracked download is known, imports the path for that download's series and download item, then verifies the import against the tracked download. [2][3][4]

## Rules

- **R1** A path is required; if it is null or whitespace the command throws an ArgumentException "A path must be provided". [1]
- **R2** If the path is neither an existing folder nor an existing file, a warning is logged and nothing is imported. [2]
- **R3** If a download client id is given but no tracked download is found, a warning is logged and a normal import of the path is attempted using the command's import mode. [2]
- **R4** If there are no import results or none has result Imported, the command logs "Failed to import" and reports the result as Unsuccessful, but still completes without error. [1]
- **R5** A download counts as fully imported when imported episodes number at least the larger of 1 and the release's episode count, or when history shows all its episodes already imported. [4]
- **R6** The unsuccessful result is only recorded when a command is in the current progress context and the reentrancy lock can be taken. [5][6]

## Side effects

- **E1** Reporting Unsuccessful sets the result on the running command in the command queue. [5][7]
- **E2** When all episodes are imported, the tracked download state is set to Imported and a DownloadCompletedEvent is published, with grabbed release info when grabbed history exists. [4][8]
- **E3** DownloadCompletedEvent is handled by the download event hub, download history service and notification service. [9][10][11]
- **E4** If no episode was just imported but history shows all were imported, a debug event with a Sentry warning "DownloadHistoryIncomplete" is written. [4][12]

## Couldn't see

- the depth limit left out 40 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.DownloadedEpisodesCommandService.Execute(NzbDrone.Core.MediaFiles.Commands.DownloadedEpisodesScanCommand)`
2. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.DownloadedEpisodesCommandService.ProcessPath(NzbDrone.Core.MediaFiles.Commands.DownloadedEpisodesScanCommand)`
3. `Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Find(System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.CompletedDownloadService.VerifyImport(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeImport.ImportResult})`
5. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandResultReporter.Report(NzbDrone.Core.Messaging.Commands.CommandResult)`
6. `Sonarr.Core|M:NzbDrone.Core.ProgressMessaging.ProgressMessageContext.LockReentrancy`
7. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandQueueManager.SetResult(NzbDrone.Core.Messaging.Commands.CommandModel,NzbDrone.Core.Messaging.Commands.CommandResult)`
8. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadCompletedEvent.#ctor(NzbDrone.Core.Download.TrackedDownloads.TrackedDownload,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile},NzbDrone.Core.Parser.Model.GrabbedReleaseInfo)`
9. [`Sonarr.Core|M:NzbDrone.Core.Download.DownloadEventHub.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/downloadeventhub.handle-downloadcompletedevent-42a2f6.md)
10. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-downloadcompletedevent-4ec245.md)
11. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-downloadcompletedevent-dc9c2f.md)
12. `Sonarr.Common|M:NzbDrone.Common.Instrumentation.Extensions.SentryLoggerExtensions.WriteSentryWarn(NLog.LogEventBuilder,System.String[])`

<sub>commit 0bffefe · inputs 2074efe · slice of 138 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
