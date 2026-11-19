# Download release (POST /api/v3/Release)

`endpoint:Sonarr.Api.V3|POST /api/v3/Release` · backend · Sonarr.Api.V3

From the code: POST /api/v3/Release

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the submitted release object back to the caller after the download has been sent. [1]
- **S2** When the release is not in the cache, it fails with 404 and the message "Couldn't find requested release in cache, try searching again". [1][2]

## Actions

- **A1** Caller submits a previously searched release (identified by indexer id and guid) to be downloaded, optionally with a download client id. [1][3][4] → `endpoint:Sonarr.Api.V3|POST /api/v3/Release`

## Rules

- **R1** The release is looked up in the cache using the key indexerId + "_" + guid. [3][5]
- **R2** The request body must be JSON. [1]
- **R3** When ShouldOverride is true, series id, episode ids (with at least one item), quality and languages are all required, otherwise validation fails. [1][6][7][8]
- **R4** With override, the cached release is copied (parsed info cloned) and its series, episodes, quality and languages are replaced by the values from the request. [1][9][10]
- **R5** If the cached release has no series, the series and episode come from the request's episode id; otherwise from the series id, with episodes parsed from the release. [1][11][12][13]
- **R6** If the cached release has no series and the request has neither episode id nor series id, it fails with 404 "Unable to find matching series and episodes, will need to be manually provided". [1][2]
- **R7** If no episodes can be determined, it fails with 404 "Unable to parse episodes in the release, will need to be manually provided". [1][14][2]
- **R8** If the release has a series but no episodes, episodes are parsed from the release, falling back to the request's episode id when none are found. [1][13][11]
- **R9** A release download failure is returned as 409 Conflict with "Getting release from indexer failed: " plus the reason. [1][2]

## Side effects

- **E1** The release is sent to the download service, optionally to the specified download client. [4][1]
- **E2** A missing cache entry is logged at debug level, and a release download failure is logged as an error. [1]
- **E3** Failed override validation logs a warning with the validation message. [15][16]

## Couldn't see

- the depth limit left out 12 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseController.DownloadRelease(Sonarr.Api.V3.Indexers.ReleaseResource)`
2. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Indexers.ReleaseController.GetCacheKey(Sonarr.Api.V3.Indexers.ReleaseResource)`
4. `Sonarr.Core|M:NzbDrone.Core.Download.IDownloadService.DownloadReport(NzbDrone.Core.Parser.Model.RemoteEpisode,System.Nullable{System.Int32})`
5. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached`1.Find(System.String)`
6. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.EnsureCollectionExtensions.HasItems``1(NzbDrone.Common.EnsureThat.Param{System.Collections.Generic.List{``0}})`
7. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.EnsureNullableValueTypeExtensions.IsNotNull``1(NzbDrone.Common.EnsureThat.Param{System.Nullable{``0}})`
8. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.EnsureObjectExtensions.IsNotNull``1(NzbDrone.Common.EnsureThat.Param{``0})`
9. `Sonarr.Common|M:NzbDrone.Common.Extensions.ObjectExtensions.JsonClone``1(``0)`
10. `Sonarr.Core|M:NzbDrone.Core.Parser.Model.RemoteEpisode.#ctor`
11. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisode(System.Int32)`
12. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Int32)`
13. `Sonarr.Core|M:NzbDrone.Core.Parser.IParsingService.GetEpisodes(NzbDrone.Core.Parser.Model.ParsedEpisodeInfo,NzbDrone.Core.Tv.Series,System.Boolean,NzbDrone.Core.IndexerSearch.Definitions.SearchCriteriaBase)`
14. `Sonarr.Common|M:NzbDrone.Common.Extensions.EnumerableExtensions.Empty``1(System.Collections.Generic.IEnumerable{``0})`
15. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.ExceptionFactory.CreateForParamValidation(System.String,System.String)`
16. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.ExceptionFactory.CreateForParamNullValidation(System.String,System.String)`

<sub>commit 0bffefe · inputs 8758269 · slice of 103 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
