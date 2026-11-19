# Series lookup (search for new series)

`endpoint:Sonarr.Api.V5|GET /api/v5/series/lookup` · backend · Sonarr.Api.V5

From the code: GET /api/v5/series/lookup

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of series matching the search term, each with the usual series fields (title, overview, seasons, images, ids, ratings, etc.). [1][2]
- **S2** Each result includes the remote poster URL when the series has a poster image. [3]
- **S3** Each result includes a suggested folder name built from the naming config, plus a list of folder names per translation (language and folder). [3][4]
- **S4** Each result has empty statistics (zero counts) except the season count, which counts seasons with a number above 0. [5][3]
- **S5** Each result has an excluded flag, true when an import list exclusion exists for its TVDB id. [3][6]

## Actions

- **A1** Search for series by a term, with an optional language id; returns the matching series. [1] → `endpoint:Sonarr.Api.V5|GET /api/v5/series/lookup`
- **A2** A term starting with "tvdb:" or "tvdbid:" looks up a single series by that TVDB id. [7]

## Rules

- **R1** If no language is given, the configured preferred metadata language is used. [1]
- **R2** An unknown language id, or one without an ISO language, returns 400 Bad Request with "Invalid language: {id}". [1][8][9]
- **R3** If the term passes the any-OS path validity check, the search fails with 400 "Invalid search term '{term}'". [7][10][11]
- **R4** For a tvdb:/tvdbid: term, an empty, non-numeric, whitespace-containing or non-positive id returns an empty list; so does a series not found. [7]
- **R5** For a tvdb id lookup, a series already in the library is returned; otherwise it is fetched from SkyHook using the official season type. [7]
- **R6** Free-text searches send the lowercased, trimmed term and the language's three-letter code to SkyHook; results that exist in the library are used instead of the remote data. [7][12]
- **R7** Communication or response failures with SkyHook surface as a 503 Service Unavailable error. [13][7]
- **R8** Cover URLs for series not yet in Sonarr (id 0) are routed through a media cover proxy registered with the remote URL. [14]

## Side effects

- **E1** Each search calls the external SkyHook metadata service, except for tvdb id lookups of series already in the library. [7]
- **E2** Cover image URLs in the results are rewritten to local or proxy URLs. [3][14]

## Couldn't see

- the depth limit left out 79 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesLookupController.Search(System.String,System.Nullable{System.Int32})`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesLookupController.MapToResource(System.Collections.Generic.IEnumerable{NzbDrone.Core.Tv.Series})`
4. `Sonarr.Core|M:NzbDrone.Core.Organizer.FileNameBuilder.GetSeriesFolder(NzbDrone.Core.Tv.Series,System.String,NzbDrone.Core.Organizer.NamingConfig)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesStatisticsResourceMapper.ToResource(NzbDrone.Core.SeriesStats.SeriesStatistics,System.Collections.Generic.List{Sonarr.Api.V5.Series.SeasonResource})`
6. `Sonarr.Core|M:NzbDrone.Core.ImportLists.Exclusions.ImportListExclusionService.FindByTvdbId(System.Int32)`
7. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.SearchForNewSeries(System.String,NzbDrone.Core.Languages.Language)`
8. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
9. `Sonarr.Core|M:NzbDrone.Core.Parser.IsoLanguages.Get(NzbDrone.Core.Languages.Language)`
10. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.InvalidSearchTermException.#ctor(System.String,System.Object[])`
11. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.IsPathValid(System.String,NzbDrone.Common.Disk.PathValidationType)`
12. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.MapSearchResult(NzbDrone.Core.MetadataSource.SkyHook.Resource.ShowResource)`
13. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookException.#ctor(System.String,System.Exception,System.Object[])`
14. `Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.ConvertToLocalUrls(System.Int32,System.Collections.Generic.IEnumerable{NzbDrone.Core.MediaCover.MediaCover},System.Nullable{System.DateTime})`

<sub>commit 0bffefe · inputs 720615f · slice of 180 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
