# Series API (/api/v3/Series)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Series` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Series

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Get by id returns a series resource with title, overview, status, network, images, seasons, season types, path, quality profile, monitored flags, external ids, genres, tags, ratings and language. [1]
- **S2** The returned series also has cover image URLs mapped to local URLs, series statistics, alternate titles from scene mappings, translations and a root folder path. [2][3][4]
- **S3** Get by id returns NotFound when the series does not exist. [5]
- **S4** Add returns a Created response pointing to the get-by-id action with the new series; update returns an Accepted response the same way. [6][7]

## Actions

- **A1** Get a series by id, optionally with includeSeasonImages (default false). [5][8]
- **A2** Add a series by posting a series resource (JSON); it is passed to the add-series service. [9]
- **A3** Update a series by putting a series resource (JSON), with optional moveFiles flag (default false). [10]
- **A4** Delete a series by id, with optional deleteFiles and addImportListExclusion flags (both default false). [11]

## Rules

- **R1** On add, the series is looked up by TVDB id; if not found a validation error 'A series with this ID was not found' is raised on TvdbId. [12]
- **R2** On add, if seasons are supplied they are used, otherwise the seasons from the lookup are used. [12]
- **R3** On add, an empty path becomes the root folder path plus a generated series folder name. [13]
- **R4** On add, if add options monitor is None the series is set to not monitored. [13]
- **R5** On add, the series is validated and a validation exception with the errors is thrown if invalid; a null series is rejected. [13][14]
- **R6** When mapping a resource to a series, season type defaults to Official and language defaults to English. [15]
- **R7** On update, the season type of an existing series is never changed, and language falls back to the stored language if none is supplied. [10]
- **R8** On update, if a move is already queued or started for the series, the path stays the source path and the path change is skipped with a warning; the rest of the update still runs. [16][17]
- **R9** On update with moveFiles, the path stays the source and the destination is stored as the pending path until files are moved; without moveFiles the path is set to the destination and pending path cleared. [16]
- **R10** On update, add options are never changed, and episodes are set monitored to match any season whose monitored flag changed. [18]
- **R11** For POST and PUT, the body resource is validated, and for PUT a zero id in the body is filled from the route id. [19]

## Side effects

- **E1** Adding a series saves it through the series service. [14]
- **E2** Updating a series with moveFiles queues a manual MoveSeriesCommand with series id, source and destination paths. [16][20]
- **E3** Updating a series saves it and publishes a SeriesEditedEvent, which other services handle. [18][21][22][23]
- **E4** Deleting a series removes it from the repository and publishes a SeriesDeletedEvent carrying the deleteFiles and addImportListExclusion flags. [24][25]
- **E5** SeriesDeletedEvent is handled by services for blocklist, download history, pending releases, tracked downloads, extra files, history, import list exclusions, media covers, media file deletion, media files, notifications, episodes and translations. [26][27][28][29][30][31][32][33][34][35][36][37][38]

## Couldn't see

- the depth limit left out 104 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R8 (contradicted): Only the path update is skipped. The rest of the update still runs, because UpdateSeries(series, true, true) is called afterwards. **fixed**: Reworded: only the path change is skipped; the rest of the update still runs.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.GetSeriesResource(NzbDrone.Core.Tv.Series,System.Boolean)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.PopulateAlternateTitles(Sonarr.Api.V3.Series.SeriesResource)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.LinkRootFolderPath(Sonarr.Api.V3.Series.SeriesResource)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.GetResourceByIdWithErrorHandler(System.Int32,System.Boolean)`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
7. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
8. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.GetSeriesResourceById(System.Int32,System.Boolean)`
9. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.AddSeries(Sonarr.Api.V3.Series.SeriesResource)`
10. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.UpdateSeries(Sonarr.Api.V3.Series.SeriesResource,System.Boolean)`
11. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.DeleteSeries(System.Int32,System.Boolean,System.Boolean)`
12. `Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.AddSkyhookData(NzbDrone.Core.Tv.Series)`
13. `Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.SetPropertiesAndValidate(NzbDrone.Core.Tv.Series)`
14. `Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.AddSeries(NzbDrone.Core.Tv.Series)`
15. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToModel(Sonarr.Api.V3.Series.SeriesResource)`
16. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.UpdateSeries(NzbDrone.Core.Tv.Series,System.String,System.String,System.Boolean)`
17. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.GetSeriesIdsWithPendingMove`
18. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.UpdateSeries(NzbDrone.Core.Tv.Series,System.Boolean,System.Boolean)`
19. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
20. [`Sonarr.Core|M:NzbDrone.Core.Tv.MoveSeriesService.Execute(NzbDrone.Core.Tv.Commands.MoveSeriesCommand)`](../../backend/sonarr.core-1b4c77/moveseriesservice.execute-moveseriescommand-872137.md)
21. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesEditedService.Handle(NzbDrone.Core.Tv.Events.SeriesEditedEvent)`](../../backend/sonarr.core-1b4c77/serieseditedservice.handle-serieseditedevent-df708d.md)
22. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesEditedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-serieseditedevent-9c427a.md)
23. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesEditedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-serieseditedevent-ef59b6.md)
24. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.DeleteSeries(System.Collections.Generic.List{System.Int32},System.Boolean,System.Boolean)`
25. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesDeletedEvent.#ctor(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean,System.Boolean)`
26. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handleasync-seriesdeletedevent-9ca82f.md)
27. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-seriesdeletedevent-7c1aff.md)
28. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-seriesdeletedevent-8d4742.md)
29. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-seriesdeletedevent-a8c1e9.md)
30. [`Sonarr.Core|M:NzbDrone.Core.Extras.Files.ExtraFileService`1.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/extrafileservice.handleasync-seriesdeletedevent-965873.md)
31. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-seriesdeletedevent-50a2ab.md)
32. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.Exclusions.ImportListExclusionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/importlistexclusionservice.handleasync-seriesdeletedevent-7baedd.md)
33. [`Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediacoverservice.handleasync-seriesdeletedevent-bceb58.md)
34. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileDeletionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafiledeletionservice.handleasync-seriesdeletedevent-99b0bf.md)
35. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafileservice.handleasync-seriesdeletedevent-335c67.md)
36. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-seriesdeletedevent-234108.md)
37. [`Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/episodeservice.handleasync-seriesdeletedevent-8a9817.md)
38. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesTranslationService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/seriestranslationservice.handleasync-seriesdeletedevent-389cb7.md)

<sub>commit 0bffefe · inputs ec18bfc · slice of 194 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
