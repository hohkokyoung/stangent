# Episode File API (update quality, delete)

`endpoint:Sonarr.Api.V5|ANY /api/v5/EpisodeFile` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/EpisodeFile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** A successful quality update returns 202 Accepted with the episode file resource and a link to the GET-by-id action. [1][2]
- **S2** A successful delete returns 204 No Content. [3]
- **S3** Getting an episode file by id returns 200 OK with the resource, or 404 Not Found if the model is not found. [4]

## Actions

- **A1** PUT updates an episode file's quality. Scene name and release group are also updated when supplied, then the file is saved. [2][5] → `endpoint:Sonarr.Api.V5|ANY /api/v5/EpisodeFile`
- **A2** DELETE by id deletes an episode file: it looks up the file and its series, then deletes the file from disk and database. [3][6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/EpisodeFile`
- **A3** GET by id returns a single episode file resource. [4] → `endpoint:Sonarr.Api.V5|ANY /api/v5/EpisodeFile`

## Rules

- **R1** The quality update body must be JSON. [2]
- **R2** The scene name is applied only if it is not null and is a valid scene title. [2]
- **R3** A title is a scene title only if it contains a '.', contains no spaces, and parses with a release group, a known quality and a non-blank series title. [7]
- **R4** The release group is applied only when it is not null. [2]
- **R5** Deleting a missing episode file fails with a 404 NzbDroneClientException "Episode file not found". [3][8]
- **R6** Delete fails with 409 Conflict if the series' root folder doesn't exist or has no subdirectories. [6]
- **R7** Delete fails with 500 "Unable to delete episode file" if moving the file to the recycle bin throws. [6]
- **R8** The file is removed from disk only if both the series folder and the file exist. The database record is deleted regardless. [6]
- **R9** On PUT or POST, the resource is validated. For PUT, a zero body Id is filled from the route id. [9]
- **R10** Calls to endpoints marked deprecated are logged and the response gets a "Deprecation: true" header. [9]

## Side effects

- **E1** Updating quality saves the changed episode file record. [5][2]
- **E2** Deleting sends the file to the recycle bin, using a subfolder relative to the series' parent folder. [6][10]
- **E3** Deleting removes the episode file record with reason Manual. [6][11]
- **E4** After a delete, a DeleteCompletedEvent is published, which the notification service handles. [6][12]

## Couldn't see

- the depth limit left out 22 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.EpisodeFiles.EpisodeFileController.SetQuality(Sonarr.Api.V5.EpisodeFiles.EpisodeFileResource)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.EpisodeFiles.EpisodeFileController.DeleteEpisodeFile(System.Int32)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
5. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IMediaFileService.Update(NzbDrone.Core.MediaFiles.EpisodeFile)`
6. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileDeletionService.DeleteEpisodeFile(NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)`
7. `Sonarr.Core|M:NzbDrone.Core.Parser.SceneChecker.IsSceneTitle(System.String)`
8. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
9. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
10. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IRecycleBinProvider.DeleteFile(System.String,System.String)`
11. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.IMediaFileService.Delete(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.MediaFiles.DeleteMediaFileReason)`
12. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.HandleAsync(NzbDrone.Core.MediaFiles.Events.DeleteCompletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handleasync-deletecompletedevent-7ce7c0.md)

<sub>commit 0bffefe · inputs 442a453 · slice of 103 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
