# Set episodes monitored (PUT /api/v3/Episode/monitor)

`endpoint:Sonarr.Api.V3|PUT /api/v3/Episode/monitor` · backend · Sonarr.Api.V3

From the code: PUT /api/v3/Episode/monitor

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the updated episodes as a list of episode resources, with HTTP status Accepted. [1]
- **S2** Each returned episode includes id, series id, TVDB id, episode file id, season and episode numbers, title, air dates, runtime, finale type, overview, last search time, has-file, monitored, absolute and scene numbering and unverified scene numbering. [2]
- **S3** The returned episodes never include series or episode file details. Images are included only when includeImages is true. [1][3]

## Actions

- **A1** Sets the monitored flag of the given episode ids to the requested value. A single id is handled as a single-episode update; several ids are updated together. [1][4][5] → `endpoint:Sonarr.Api.V3|PUT /api/v3/Episode/monitor`

## Rules

- **R1** The request body is JSON with a list of episode ids and a monitored value. An optional includeImages query parameter defaults to false. [1]
- **R2** When exactly one episode id is supplied, the single-episode path is used. Otherwise the bulk path is used. [1]

## Side effects

- **E1** For a single episode, the episode is loaded and its monitored flag is saved through the episode repository. A debug log line records the new value. [4]
- **E2** For several episodes, the monitored flag is updated through the episode repository for all the ids. [5]

## Couldn't see

- the depth limit left out 106 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeController.SetEpisodesMonitored(Sonarr.Api.V3.Episodes.EpisodesMonitoredResource,System.Boolean)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeControllerWithSignalR.MapToResource(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.SetEpisodeMonitored(System.Int32,System.Boolean)`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.SetMonitored(System.Collections.Generic.IEnumerable{System.Int32},System.Boolean)`

<sub>commit 0bffefe · inputs ff58071 · slice of 92 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
