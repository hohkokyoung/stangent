# Series lookup (search for new series)

`endpoint:Sonarr.Api.V3|GET /api/v3/series/lookup` · backend · Sonarr.Api.V3

From the code: GET /api/v3/series/lookup

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of series matching the search term, each mapped to a series resource with title, overview, images, seasons, translations, ids and other series fields. [1][2]
- **S2** Each result includes a remote poster URL when the series has a poster image. [3]
- **S3** Each result includes a folder name built from the series folder naming format, plus one folder per translation, each with its language and a folder name built from the translated title. [3][4]
- **S4** Each result includes empty statistics (zero counts) with a season count of seasons numbered above 0. [5][3]
- **S5** Each result lists translations with language, title and overview. [6]

## Actions

- **A1** Search for series by a term and an optional language id; returns matching series from the metadata source. [1][7] → `endpoint:Sonarr.Api.V3|GET /api/v3/series/lookup`
- **A2** Search with a term starting with "tvdb:" or "tvdbid:" followed by a TVDB id to look up a single series by id. [7]

## Rules

- **R1** The language defaults to English when not supplied. [1]
- **R2** If the language id is unknown or has no ISO language, the request fails with a bad request "Invalid language: {id}". [1][8][9]
- **R3** A search term that is a valid path (for any OS) is rejected with a bad request "Invalid search term '{term}'". [7][10][11]
- **R4** For a tvdb:/tvdbid: term, an empty, whitespace-containing, non-numeric or non-positive id returns an empty list; a series not found also returns an empty list. [7]
- **R5** For a tvdb id lookup, a series already in the library is returned; otherwise its info is fetched from the metadata source. [7][12]
- **R6** For a text search, the lowercased, trimmed term and the language's three-letter code are sent to the metadata search; results already in the library replace the remote ones. [7][13]
- **R7** If communication with the metadata source fails or its response is invalid, the search fails with a service-unavailable error. [7][14]
- **R8** Cover URLs for series not yet in the library (id 0) are routed through a proxy registration of the remote URL. [15]

## Side effects

- **E1** Calls the external SkyHook metadata service to search for series or fetch series info; nothing is saved by this endpoint. [7][12]
- **E2** Registers remote cover URLs with the media cover proxy for series not yet in the library. [15]

## Couldn't see

- the depth limit left out 78 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesLookupController.Search(System.String,System.Nullable{System.Int32})`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesLookupController.MapToResource(System.Collections.Generic.IEnumerable{NzbDrone.Core.Tv.Series})`
4. `Sonarr.Core|M:NzbDrone.Core.Organizer.FileNameBuilder.GetSeriesFolder(NzbDrone.Core.Tv.Series,System.String,NzbDrone.Core.Organizer.NamingConfig)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesStatisticsResourceMapper.ToResource(NzbDrone.Core.SeriesStats.SeriesStatistics,System.Collections.Generic.List{Sonarr.Api.V3.Series.SeasonResource})`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesTranslationResourceMapper.ToResource(NzbDrone.Core.Tv.SeriesTranslation)`
7. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.SearchForNewSeries(System.String,NzbDrone.Core.Languages.Language)`
8. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
9. `Sonarr.Core|M:NzbDrone.Core.Parser.IsoLanguages.Get(NzbDrone.Core.Languages.Language)`
10. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.InvalidSearchTermException.#ctor(System.String,System.Object[])`
11. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.IsPathValid(System.String,NzbDrone.Common.Disk.PathValidationType)`
12. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.GetSeriesInfo(System.Int32,NzbDrone.Core.Languages.Language,System.String)`
13. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.MapSearchResult(NzbDrone.Core.MetadataSource.SkyHook.Resource.ShowResource)`
14. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookException.#ctor(System.String,System.Exception,System.Object[])`
15. `Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.ConvertToLocalUrls(System.Int32,System.Collections.Generic.IEnumerable{NzbDrone.Core.MediaCover.MediaCover},System.Nullable{System.DateTime})`

<sub>commit 0bffefe · inputs 77d503a · slice of 167 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
