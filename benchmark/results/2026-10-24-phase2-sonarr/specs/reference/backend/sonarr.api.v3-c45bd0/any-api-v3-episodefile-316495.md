# Episode File API

`endpoint:Sonarr.Api.V3|ANY /api/v3/EpisodeFile` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/EpisodeFile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** A GET by id returns the episode file resource with 200 OK, or 404 Not Found when the file is not found. [1]
- **S2** After a quality update, the response is 202 Accepted pointing at the updated episode file resource. [2][3]

## Actions

- **A1** Update an episode file by id (PUT, JSON body): sets its quality, and optionally its scene name and release group, then saves it. [3][4] → `endpoint:Sonarr.Api.V3|ANY /api/v3/EpisodeFile`
- **A2** Delete an episode file by id: looks up the file and its series, then deletes the file through the deletion service. [5][6][7] → `endpoint:Sonarr.Api.V3|ANY /api/v3/EpisodeFile`

## Rules

- **R1** When updating, the scene name is applied only if it is provided and is a valid scene title. [3]
- **R2** A scene title must contain a dot, contain no spaces, and parse to a release group, a known quality and a non-blank series title. [8]
- **R3** The release group is applied on update only when provided. [3]
- **R4** Deleting a file that is not found returns 404 with "Episode file not found". [5][9]
- **R5** Deletion fails with 409 Conflict if the series' root folder does not exist or has no subdirectories. [10]
- **R6** If the disk file cannot be moved to the recycle bin, deletion fails with 500 "Unable to delete episode file". [10]
- **R7** On PUT, an id of 0 in the body is filled from the route id; on POST/PUT resources are validated, and the id is validated for id-based actions unless validation is skipped. [11]

## Side effects

- **E1** Updating saves the changed episode file record. [4][3]
- **E2** Deleting moves the file on disk, if present, to the recycle bin. [10][12]
- **E3** Deleting removes the episode file record from the database with reason Manual, even if the disk file was already gone. [10][13]
- **E4** After deletion a DeleteCompletedEvent is published, which the notification service handles. [10][14][15]
- **E5** Calls to deprecated endpoints are logged as a warning and get a "Deprecation: true" response header. [11]

## Couldn't see

- the depth limit left out 22 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.EpisodeFiles.EpisodeFileController.SetQuality(Sonarr.Api.V3.EpisodeFiles.EpisodeFileResource)`
4. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IMediaFileService.Update(NzbDrone.Core.MediaFiles.EpisodeFile)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.EpisodeFiles.EpisodeFileController.DeleteEpisodeFile(System.Int32)`
6. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Int32)`
7. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IDeleteMediaFiles.DeleteEpisodeFile(NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)`
8. `Sonarr.Core|M:NzbDrone.Core.Parser.SceneChecker.IsSceneTitle(System.String)`
9. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileDeletionService.DeleteEpisodeFile(NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)`
11. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
12. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IRecycleBinProvider.DeleteFile(System.String,System.String)`
13. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IMediaFileService.Delete(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.MediaFiles.DeleteMediaFileReason)`
14. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
15. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.HandleAsync(NzbDrone.Core.MediaFiles.Events.DeleteCompletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handleasync-deletecompletedevent-7ce7c0.md)

<sub>commit 0bffefe · inputs 3178a14 · slice of 103 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
