# Delete series (bulk editor)

`endpoint:Sonarr.Api.V5|DELETE /api/v5/series/editor` · backend · Sonarr.Api.V5

From the code: DELETE /api/v5/series/editor

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an empty 204 No Content response after the series are deleted. [1]

## Actions

- **A1** Deletes the series listed in the JSON request body's series IDs, passing along the delete-files and add-import-list-exclusion flags. [1][2] → `endpoint:Sonarr.Api.V5|DELETE /api/v5/series/editor`

## Rules

- **R1** The request must be sent as application/json. [1]

## Side effects

- **E1** The series records for the given IDs are loaded and then deleted from the series repository in one bulk delete. [2][3][4]
- **E2** After the delete, a series-deleted event is published carrying the deleted series, the delete-files flag and the add-import-list-exclusion flag. [2][5][6]
- **E3** Handlers for the series-deleted event include media file deletion, media file records, episodes, extra files, history, download history, pending releases, tracked downloads, blocklist, import list exclusions, media covers, notifications and series translations. [7][8][9][10][11][12][13][14][15][16][17][18][19]
- **E4** The V3 and V5 series controllers also have handlers for the series-deleted event. [20][21]

## Couldn't see

- the depth limit left out 8 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesEditorController.DeleteSeries(Sonarr.Api.V5.Series.SeriesEditorResource)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.DeleteSeries(System.Collections.Generic.List{System.Int32},System.Boolean,System.Boolean)`
3. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.DeleteMany(System.Collections.Generic.IEnumerable{System.Int32})`
4. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Get(System.Collections.Generic.IEnumerable{System.Int32})`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesDeletedEvent.#ctor(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean,System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
7. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileDeletionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafiledeletionservice.handleasync-seriesdeletedevent-99b0bf.md)
8. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafileservice.handleasync-seriesdeletedevent-335c67.md)
9. [`Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/episodeservice.handleasync-seriesdeletedevent-8a9817.md)
10. [`Sonarr.Core|M:NzbDrone.Core.Extras.Files.ExtraFileService`1.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/extrafileservice.handleasync-seriesdeletedevent-965873.md)
11. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-seriesdeletedevent-50a2ab.md)
12. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-seriesdeletedevent-7c1aff.md)
13. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-seriesdeletedevent-8d4742.md)
14. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-seriesdeletedevent-a8c1e9.md)
15. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handleasync-seriesdeletedevent-9ca82f.md)
16. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.Exclusions.ImportListExclusionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/importlistexclusionservice.handleasync-seriesdeletedevent-7baedd.md)
17. [`Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediacoverservice.handleasync-seriesdeletedevent-bceb58.md)
18. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-seriesdeletedevent-234108.md)
19. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesTranslationService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/seriestranslationservice.handleasync-seriesdeletedevent-389cb7.md)
20. [`Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.api.v3-c45bd0/seriescontroller.handle-seriesdeletedevent-d5ea84.md)
21. [`Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.api.v5-c79e7b/seriescontroller.handle-seriesdeletedevent-2117ea.md)

<sub>commit 0bffefe · inputs 239dda8 · slice of 30 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
