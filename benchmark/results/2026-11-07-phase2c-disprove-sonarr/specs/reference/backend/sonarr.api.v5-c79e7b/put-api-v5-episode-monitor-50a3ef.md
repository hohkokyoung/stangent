# Set episodes monitored (PUT Episode/monitor)

`endpoint:Sonarr.Api.V5|PUT /api/v5/Episode/monitor` · backend · Sonarr.Api.V5

From the code: PUT /api/v5/Episode/monitor

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the list of episodes whose ids were sent, after the monitored change, each with fields such as id, series id, season and episode number, title, air date, runtime, overview, has-file flag and monitored flag. [1][2]
- **S2** The returned episodes include their images only when the Images subresource is requested in the query. [1][3]
- **S3** The returned episodes never include the series or the episode file. [1]

## Actions

- **A1** Sets the monitored flag to the requested value for the episode ids in the JSON body, then returns the updated episodes. [1] → `endpoint:Sonarr.Api.V5|PUT /api/v5/Episode/monitor`

## Rules

- **R1** The request body must be JSON. [1]
- **R2** When exactly one episode id is sent, that single episode is loaded and updated individually. With any other number of ids, all ids are updated in one bulk operation. [1][4][5]

## Side effects

- **E1** The monitored flag of the selected episodes is saved through the episode repository. [4][5][6][7]
- **E2** For a single-episode update, a debug log line records the new monitored value for that episode. [4]

## Couldn't see

- the depth limit left out 97 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeController.SetEpisodesMonitored(Sonarr.Api.V5.Episodes.EpisodesMonitoredResource,Sonarr.Api.V5.Episodes.EpisodeSubresource[])`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeControllerWithSignalR.MapToResource(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.SetEpisodeMonitored(System.Int32,System.Boolean)`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.SetMonitored(System.Collections.Generic.IEnumerable{System.Int32},System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeRepository.SetMonitoredFlat(NzbDrone.Core.Tv.Episode,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeRepository.SetMonitored(System.Collections.Generic.IEnumerable{System.Int32},System.Boolean)`

<sub>commit 0bffefe · inputs 280d51b · slice of 93 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
