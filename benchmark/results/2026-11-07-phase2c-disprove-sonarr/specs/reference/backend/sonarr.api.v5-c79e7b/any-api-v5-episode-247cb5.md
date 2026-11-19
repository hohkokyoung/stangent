# Episode API: set monitored / get episode

`endpoint:Sonarr.Api.V5|ANY /api/v5/Episode` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Episode

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The update returns the saved episode as JSON with fields such as id, seriesId, tvdbId, episodeFileId, season and episode numbers, title, air dates, runtime, finale type, overview, last search time, hasFile and monitored. [1]
- **S2** The returned episode also includes absolute episode number and scene absolute, episode and season numbers, and the unverified scene numbering flag. [1]
- **S3** After a monitored update the response does not include series, episode file or images, because they are not requested. [2][3]
- **S4** When series, episode file or images are requested, the resource can also carry the series details, the episode file details (path, size, quality, media info, custom formats and score, cutoff-not-met flag) and the episode images. [3][4][5]
- **S5** Getting an episode by id returns 200 with the resource, or 404 Not Found when the model is not found. [6]

## Actions

- **A1** A PUT to the episode by id with a JSON body sets that episode's monitored flag to the body's Monitored value and returns the updated episode. [2] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Episode`
- **A2** A GET by id returns the episode resource, or Not Found. [6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Episode`

## Rules

- **R1** The monitored update accepts only application/json bodies. [2]
- **R2** On a PUT, if the body resource's Id is 0 and the route has an id, the route id is copied into the resource before validation. [7]
- **R3** POST and PUT body resources are validated, and the route id is validated for actions that need id validation, unless validation is skipped for the action. [7]
- **R4** Calls to endpoints marked deprecated are logged as a warning and get the response header Deprecation: true. [7]
- **R5** An episode file's path is the series path combined with the file's relative path, and its custom format score is the sum of the quality profile scores of the matching formats (0 if there is no profile). [4][8]

## Side effects

- **E1** Setting monitored loads the episode by id and updates its monitored flag through the episode repository. [9][10]
- **E2** A debug log entry records that the episode's monitored flag was set to the new value. [9]

## Couldn't see

- the depth limit left out 59 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeController.SetEpisodeMonitored(System.Int32,Sonarr.Api.V5.Episodes.EpisodeResource)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeControllerWithSignalR.MapToResource(NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.EpisodeFiles.EpisodeFileResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.Tv.Series,NzbDrone.Core.DecisionEngine.Specifications.IUpgradableSpecification,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
7. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
8. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
9. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.SetEpisodeMonitored(System.Int32,System.Boolean)`
10. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeRepository.SetMonitoredFlat(NzbDrone.Core.Tv.Episode,System.Boolean)`

<sub>commit 0bffefe · inputs 3fae66c · slice of 123 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
