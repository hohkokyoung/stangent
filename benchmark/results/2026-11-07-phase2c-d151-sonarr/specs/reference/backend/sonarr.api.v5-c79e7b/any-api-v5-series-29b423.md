# Series API (v5)

`endpoint:Sonarr.Api.V5|ANY /api/v5/Series` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Series

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Getting a series by id returns the series with its details, including seasons, translations, alternate titles, statistics, local cover URLs and root folder path. [1][2]
- **S2** Series statistics are looked up by series id and quality profile id and attached to the returned series. [3]
- **S3** Alternate titles come from scene mappings found by TVDB id; if none are found they are left unset. [4]
- **S4** The returned series includes its root folder path, taken as the best root folder match for the series path. [5]
- **S5** Season images are included only when the SeasonImages subresource is requested. [6]
- **S6** Adding a series responds Created with the stored series; updating responds Accepted with the series; deleting responds No Content. [7][8][9]
- **S7** Getting a series that is missing returns Not Found. [6]

## Actions

- **A1** Get a series by id, optionally including subresources. [6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Series`
- **A2** Add a series from a JSON body; it is looked up in Skyhook by TVDB id, validated and saved. [7][10] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Series`
- **A3** Update a series from a JSON body, with an optional moveFiles flag (default false) to move the files to the new path. [8] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Series`
- **A4** Delete a series by id, with optional deleteFiles and addImportListExclusion flags (both default false). [9] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Series`

## Rules

- **R1** If the TVDB id is not found when adding, a validation error on TvdbId says a series with this ID was not found. [11]
- **R2** When adding, seasons passed in are used if there are any; otherwise the seasons from Skyhook are used. [11]
- **R3** When adding without a path, the path is the root folder path combined with the generated series folder name. [12]
- **R4** When adding, clean title, sort title and the added date (UTC now) are set. [12]
- **R5** When adding with a Monitor option of None, the series is set to not monitored. [12]
- **R6** A series that fails the add validator is rejected with a validation error. [12]
- **R7** A new series must not be null. [10][13]
- **R8** On update, if a move is already queued or running for the series, the path update is skipped and the source path is kept. [14][15]
- **R9** On update with moveFiles, the path stays at the source and the destination is stored as pending path until the move completes; without moveFiles the path is set to the destination and pending path is cleared. [14]
- **R10** On update, AddOptions is never changed and keeps the stored value. [16]
- **R11** On update, when a season's monitored flag differs from the stored one, the episodes of that season are set to the same monitored value. [16]
- **R12** On POST or PUT, the body is validated; on PUT, an id of 0 in the body is taken from the route id. [17]
- **R13** Calls to deprecated endpoints are logged as a warning and get a Deprecation: true response header. [17]

## Side effects

- **E1** Adding a series saves the new series. [10]
- **E2** Updating a series saves it and publishes a SeriesEditedEvent, which the slice lists handlers for in pending releases, tracked downloads and series edited handling. [16][18][19][20]
- **E3** Updating with moveFiles queues a MoveSeriesCommand with the series id, source path and destination path, triggered manually. [14][21]
- **E4** Deleting a series removes it from the repository and publishes a SeriesDeletedEvent carrying the deleteFiles and addImportListExclusion flags. [22][23]
- **E5** The SeriesDeletedEvent is handled by blocklist, download history, pending releases, tracked downloads, extra files, history, import list exclusions, media covers, media file deletion, media files, notifications, episodes and series translations. [24][25][26][27][28][29][30][31][32][33][34][35][36]

## Couldn't see

- the depth limit left out 89 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.GetSeriesResource(NzbDrone.Core.Tv.Series,System.Boolean)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.GetSeriesResourceById(System.Int32,System.Boolean)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.FetchAndLinkSeriesStatistics(Sonarr.Api.V5.Series.SeriesResource)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.PopulateAlternateTitles(Sonarr.Api.V5.Series.SeriesResource)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.LinkRootFolderPath(Sonarr.Api.V5.Series.SeriesResource)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.GetResourceByIdWithErrorHandler(System.Int32,Sonarr.Api.V5.Series.SeriesSubresource[])`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.AddSeries(Sonarr.Api.V5.Series.SeriesResource)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.UpdateSeries(Sonarr.Api.V5.Series.SeriesResource,System.Boolean)`
9. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.DeleteSeries(System.Int32,System.Boolean,System.Boolean)`
10. `Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.AddSeries(NzbDrone.Core.Tv.Series)`
11. `Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.AddSkyhookData(NzbDrone.Core.Tv.Series)`
12. `Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.SetPropertiesAndValidate(NzbDrone.Core.Tv.Series)`
13. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.EnsureObjectExtensions.IsNotNull``1(NzbDrone.Common.EnsureThat.Param{``0})`
14. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.UpdateSeries(NzbDrone.Core.Tv.Series,System.String,System.String,System.Boolean)`
15. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.GetSeriesIdsWithPendingMove`
16. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.UpdateSeries(NzbDrone.Core.Tv.Series,System.Boolean,System.Boolean)`
17. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
18. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesEditedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-serieseditedevent-9c427a.md)
19. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesEditedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-serieseditedevent-ef59b6.md)
20. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesEditedService.Handle(NzbDrone.Core.Tv.Events.SeriesEditedEvent)`](../../backend/sonarr.core-1b4c77/serieseditedservice.handle-serieseditedevent-df708d.md)
21. [`Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.Execute(NzbDrone.Core.Tv.Commands.MoveSeriesCommand)`](../../backend/sonarr.core-1b4c77/moveseriesservice.execute-moveseriescommand-872137.md)
22. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.DeleteSeries(System.Collections.Generic.List{System.Int32},System.Boolean,System.Boolean)`
23. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesDeletedEvent.#ctor(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean,System.Boolean)`
24. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handleasync-seriesdeletedevent-9ca82f.md)
25. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-seriesdeletedevent-7c1aff.md)
26. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-seriesdeletedevent-8d4742.md)
27. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-seriesdeletedevent-a8c1e9.md)
28. [`Sonarr.Core|M:NzbDrone.Core.Extras.Files.ExtraFileService`1.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/extrafileservice.handleasync-seriesdeletedevent-965873.md)
29. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-seriesdeletedevent-50a2ab.md)
30. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.Exclusions.ImportListExclusionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/importlistexclusionservice.handleasync-seriesdeletedevent-7baedd.md)
31. [`Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediacoverservice.handleasync-seriesdeletedevent-bceb58.md)
32. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileDeletionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafiledeletionservice.handleasync-seriesdeletedevent-99b0bf.md)
33. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafileservice.handleasync-seriesdeletedevent-335c67.md)
34. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-seriesdeletedevent-234108.md)
35. [`Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/episodeservice.handleasync-seriesdeletedevent-8a9817.md)
36. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesTranslationService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/seriestranslationservice.handleasync-seriesdeletedevent-389cb7.md)

<sub>commit 0bffefe · inputs 0ce04f9 · slice of 196 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
