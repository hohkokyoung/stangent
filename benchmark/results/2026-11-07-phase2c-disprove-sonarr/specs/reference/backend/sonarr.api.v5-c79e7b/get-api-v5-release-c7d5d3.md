# List releases (interactive search / RSS)

`endpoint:Sonarr.Api.V5|GET /api/v5/Release` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Release

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of releases, each mapped with a release weight, a quality weight and, when matched, a history entry with Grabbed date and optional Failed date. [1][2]
- **S2** The release weight is the release's position in the prioritized list. The quality weight is 0 when no quality is parsed; otherwise it is the quality's index in the profile ×100, plus the revision's Real ×10, plus the revision Version. [3]
- **S3** A release's history is null when no matching Grabbed history record is found. [2]

## Actions

- **A1** Calling with episodeId runs an interactive episode search, prioritizes the decisions, and returns them with the episode's history. [4][5] → `endpoint:Sonarr.Api.V5|GET /api/v5/Release`
- **A2** Calling with seriesId and seasonNumber (and no episodeId) runs a season search, prioritizes the decisions, and returns them with the season's history. [4][6] → `endpoint:Sonarr.Api.V5|GET /api/v5/Release`
- **A3** Calling with none of the parameters fetches the RSS releases from the indexers, makes RSS decisions, prioritizes them, and returns them with no history. [7][8] → `endpoint:Sonarr.Api.V5|GET /api/v5/Release`

## Rules

- **R1** episodeId takes precedence: if it is given, seriesId and seasonNumber are ignored. [4]
- **R2** If only one of seriesId or seasonNumber is given (without episodeId), the request fails with 400 Bad Request: "seriesId and seasonNumber must be provided together". [4][9]
- **R3** In episode and season searches, a search failure returns 400 Bad Request with the failure message; any other error is logged and returned as 500 Internal Server Error. [5][6]
- **R4** The episode search is flagged as user-invoked and interactive. The season search is user-invoked and interactive, with missingOnly and monitoredOnly both false. [5][6]
- **R5** The RSS fetch returns an empty list and logs a warning when no RSS-enabled indexers are available. [8]
- **R6** A Grabbed history record matches a release by guid or info URL, then for torrents by info hash or by title with the torrent protocol, and otherwise by same-NZB comparison. The Failed date is set from a DownloadFailed record with the same download id. [2]

## Side effects

- **E1** Each returned release's underlying remote episode is cached for 30 minutes under the key indexerId_guid. [1][10][11]
- **E2** The RSS path fetches from all RSS-enabled indexers. [8]

## Couldn't see

- the depth limit left out 76 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on E2 (unsupported): The slice shows EpisodeSearch and SeasonSearch only loading episodes and calling overloads that aren't in the slice. Nothing shown says they query indexers. Only the RSS fetch from RSS-enabled indexers is visible. **fixed**: Removed the claim that episode and season searches query indexers; kept only the visible RSS fetch from RSS-enabled indexers.

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.MapDecisions(System.Collections.Generic.IEnumerable{NzbDrone.Core.DecisionEngine.DownloadDecision},System.Collections.Generic.List{NzbDrone.Core.History.EpisodeHistory})`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.AddHistory(NzbDrone.Core.Parser.Model.ReleaseInfo,System.Collections.Generic.List{NzbDrone.Core.History.EpisodeHistory})`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseResourceMapper.MapDecision(NzbDrone.Core.DecisionEngine.DownloadDecision,System.Int32,NzbDrone.Core.Profiles.Qualities.QualityProfile)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.GetReleases(System.Nullable{System.Int32},System.Nullable{System.Int32},System.Nullable{System.Int32})`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.GetEpisodeReleases(System.Int32)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.GetSeasonReleases(System.Int32,System.Int32)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.GetRss`
8. `Sonarr.Core|M:NzbDrone.Core.Indexers.FetchAndParseRssService.Fetch`
9. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
10. `Sonarr.Api.V5|M:Sonarr.Api.V5.Release.ReleaseController.GetCacheKey(Sonarr.Api.V5.Release.ReleaseResource)`
11. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached`1.Set(System.String,`0,System.Nullable{System.TimeSpan})`

<sub>commit 0bffefe · inputs c260c46 · slice of 45 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
