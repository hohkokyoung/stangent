# Get releases (interactive search / RSS)

`endpoint:Sonarr.Api.V3|GET /api/v3/Release` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Release

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of releases, each mapped from a download decision, with a release weight and a quality weight. [1][2]

## Actions

- **A1** With an episodeId, searches indexers for that episode and returns the prioritized releases. [3][4] → `endpoint:Sonarr.Api.V3|GET /api/v3/Release`
- **A2** With both seriesId and seasonNumber (and no episodeId), searches for that season and returns the prioritized releases. [3][5] → `endpoint:Sonarr.Api.V3|GET /api/v3/Release`
- **A3** With none of the parameters, fetches the latest releases from RSS-enabled indexers, makes decisions on them and returns them prioritized. [6][7][8] → `endpoint:Sonarr.Api.V3|GET /api/v3/Release`

## Rules

- **R1** If only one of seriesId or seasonNumber is given (without episodeId), the request fails with 400 Bad Request: "seriesId and seasonNumber must be provided together". [3][9]
- **R2** If episodeId is given it takes precedence over seriesId and seasonNumber. [3]
- **R3** A search failure in episode or season search returns 400 Bad Request with the failure message; any other error returns 500 Internal Server Error with the error message. [4][5]
- **R4** Episode search is run as user-invoked and interactive. [4]
- **R5** Season search is run with missingOnly false, monitoredOnly false, user-invoked and interactive. [5]
- **R6** Each release's release weight is its position in the prioritized list. [1][2]
- **R7** Quality weight is the quality's index in the quality profile ×100, plus the revision's Real ×10, plus the revision Version. [2]
- **R8** A season search returns no releases when there are no episodes in the season (after the missing-only filter, if used). [10]
- **R9** The RSS fetch returns an empty list and logs a warning when no indexers are RSS-enabled. [7]

## Side effects

- **E1** Queries all RSS-enabled indexers in parallel when no filter is given. [7]
- **E2** Unexpected errors in episode or season search are logged. [4][5]

## Couldn't see

- the depth limit left out 34 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseControllerBase.MapDecisions(System.Collections.Generic.IEnumerable{NzbDrone.Core.DecisionEngine.DownloadDecision})`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseControllerBase.MapDecision(NzbDrone.Core.DecisionEngine.DownloadDecision,System.Int32)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseController.GetReleases(System.Nullable{System.Int32},System.Nullable{System.Int32},System.Nullable{System.Int32})`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseController.GetEpisodeReleases(System.Int32)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseController.GetSeasonReleases(System.Int32,System.Int32)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseController.GetRss`
7. `Sonarr.Core|M:NzbDrone.Core.Indexers.FetchAndParseRssService.Fetch`
8. `Sonarr.Core|M:NzbDrone.Core.DecisionEngine.DownloadDecisionMaker.GetRssDecision(System.Collections.Generic.List{NzbDrone.Core.Parser.Model.ReleaseInfo},System.Boolean)`
9. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.SeasonSearch(System.Int32,System.Int32,System.Boolean,System.Boolean,System.Boolean,System.Boolean)`

<sub>commit 0bffefe · inputs 5558d3c · slice of 26 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
