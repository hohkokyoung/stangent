# Episode API (get / set monitored)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Episode` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Episode

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Updating an episode returns the updated episode as a resource with id, series id, tvdb id, episode file id, season and episode numbers, title, air date (local and UTC), runtime, finale type, overview, last search time, has-file and monitored flags, and absolute/scene numbering. [1][2]
- **S2** The response after updating does not include the series, episode file or images. [2][3]
- **S3** Fetching an episode by id returns the episode, or a Not Found result when the episode does not exist. [4]

## Actions

- **A1** PUT by id with a JSON body sets the episode's monitored flag to the body's Monitored value, then returns the episode with status Accepted. [2] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Episode`
- **A2** GET by id returns the episode resource as JSON. [4] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Episode`

## Rules

- **R1** The update endpoint only accepts application/json requests. [2]
- **R2** On PUT, if the body resource Id is 0 and the route has an id, the route id is used as the body Id. [5]
- **R3** POST and PUT resources are validated before the action runs, and the id is validated when the action requires it and validation is not skipped. [5]
- **R4** Calls to endpoints marked deprecated get a Deprecation: true response header. [5]

## Side effects

- **E1** Setting monitored loads the episode and saves the new monitored flag via the episode repository. [6][7]
- **E2** A debug log entry records the episode id and the new monitored value. [6]
- **E3** A warning is logged with the caller's user agent when a deprecated endpoint is called. [5]

## Couldn't see

- the depth limit left out 68 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeController.SetEpisodeMonitored(System.Int32,Sonarr.Api.V3.Episodes.EpisodeResource)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeControllerWithSignalR.MapToResource(NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
6. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.SetEpisodeMonitored(System.Int32,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeRepository.SetMonitoredFlat(NzbDrone.Core.Tv.Episode,System.Boolean)`

<sub>commit 0bffefe · inputs 78d9aeb · slice of 123 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
