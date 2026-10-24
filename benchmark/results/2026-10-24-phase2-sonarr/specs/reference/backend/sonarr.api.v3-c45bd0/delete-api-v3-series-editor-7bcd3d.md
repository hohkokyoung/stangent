# Bulk delete series (series editor)

`endpoint:Sonarr.Api.V3|DELETE /api/v3/series/editor` · backend · Sonarr.Api.V3

From the code: DELETE /api/v3/series/editor

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an empty JSON object after the deletion call completes. [1]

## Actions

- **A1** Deletes the series listed in the request body's series ids, passing along the delete-files and add-import-list-exclusion flags. [1][2] → `endpoint:Sonarr.Api.V3|DELETE /api/v3/series/editor`

## Rules

- **R1** The series records are loaded by id first, then all of the given ids are deleted in one repository call. [2][3][4]

## Side effects

- **E1** Removes the series records from the datastore. [2][4]
- **E2** Publishes a SeriesDeletedEvent carrying the deleted series, the delete-files flag and the add-import-list-exclusion flag. [2][5][6]
- **E3** Handlers of the deletion event include media file deletion, media files, extra files, episodes, translations, history, blocklist, import list exclusions, media covers, notifications, download history, pending releases and tracked downloads. [7][8][9][10][11][12][13][14][15][16][17][18][19]

## Couldn't see

- the depth limit left out 8 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesEditorController.DeleteSeries(Sonarr.Api.V3.Series.SeriesEditorResource)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.DeleteSeries(System.Collections.Generic.List{System.Int32},System.Boolean,System.Boolean)`
3. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Get(System.Collections.Generic.IEnumerable{System.Int32})`
4. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.DeleteMany(System.Collections.Generic.IEnumerable{System.Int32})`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesDeletedEvent.#ctor(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean,System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
7. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileDeletionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafiledeletionservice.handleasync-seriesdeletedevent-99b0bf.md)
8. [`Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediafileservice.handleasync-seriesdeletedevent-335c67.md)
9. [`Sonarr.Core|M:NzbDrone.Core.Extras.Files.ExtraFileService`1.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/extrafileservice.handleasync-seriesdeletedevent-965873.md)
10. [`Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/episodeservice.handleasync-seriesdeletedevent-8a9817.md)
11. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesTranslationService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/seriestranslationservice.handleasync-seriesdeletedevent-389cb7.md)
12. [`Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/historyservice.handle-seriesdeletedevent-50a2ab.md)
13. [`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/blocklistservice.handleasync-seriesdeletedevent-9ca82f.md)
14. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.Exclusions.ImportListExclusionService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/importlistexclusionservice.handleasync-seriesdeletedevent-7baedd.md)
15. [`Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/mediacoverservice.handleasync-seriesdeletedevent-bceb58.md)
16. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-seriesdeletedevent-234108.md)
17. [`Sonarr.Core|M:NzbDrone.Core.Download.History.DownloadHistoryService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/downloadhistoryservice.handle-seriesdeletedevent-7c1aff.md)
18. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-seriesdeletedevent-8d4742.md)
19. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesDeletedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-seriesdeletedevent-a8c1e9.md)

<sub>commit 0bffefe · inputs ddf6ec8 · slice of 30 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
