# Get history (paged list)

`endpoint:Sonarr.Api.V5|GET /api/v5/History` · backend · Sonarr.Api.V5

From the code: GET /api/v5/History

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a paged list of history records with page, page size, sort direction, sort key, total records and the records. [1]
- **S2** Each history record shows id, episode id, series id, source title, languages, quality, custom formats, custom format score, date, download id, event type and data. [2]
- **S3** Each record's custom formats are returned without details, and its custom format score is the sum of the quality profile scores of the matching formats. [2][3]
- **S4** When the record has a series, it also shows whether the quality cutoff is not met. [4]
- **S5** The full series details are included in a record only when the Series subresource is requested. [4][5]
- **S6** The full episode details are included in a record only when the Episode subresource is requested. [4][6]

## Actions

- **A1** A caller requests history with paging, optional event type, episode id, download id, series ids, languages, quality and includeSubresources parameters. [7] → `endpoint:Sonarr.Api.V5|GET /api/v5/History`

## Rules

- **R1** Page defaults to 1 and page size defaults to 10 when not supplied. [8]
- **R2** Sorting is allowed only by date, series.sortTitle or quality (case-insensitive); any other sort key falls back to date. [7][9]
- **R3** The default sort direction is descending when the request's direction is Default. [7][9]
- **R4** The eventType filter, when given and non-empty, keeps only records whose event type is in the list. [7]
- **R5** The episodeId filter, when given, keeps only records for that episode. [7]
- **R6** The downloadId filter applies only when it is not null or whitespace. [7][10]
- **R7** The seriesIds filter, when given and non-empty, keeps only records for those series. [7]
- **R8** The languages and quality filters are passed to the history service's paged query. [7][11]
- **R9** Quality cutoff not met is true when the current quality is below the profile cutoff, or the first allowed quality when upgrades are not allowed. [12]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 35 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.Extensions.RequestExtensions.ApplyToPage``2(NzbDrone.Core.Datastore.PagingSpec{``1},System.Func{NzbDrone.Core.Datastore.PagingSpec{``1},NzbDrone.Core.Datastore.PagingSpec{``1}},System.Converter{``1,``0})`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryResourceMapper.ToResource(NzbDrone.Core.History.EpisodeHistory,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
3. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryController.MapToResource(NzbDrone.Core.History.EpisodeHistory,System.Boolean,System.Boolean)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryController.GetHistory(Sonarr.Http.PagingRequestResource,System.Int32[],System.Nullable{System.Int32},System.String,System.Int32[],System.Int32[],System.Int32[],Sonarr.Api.V5.History.HistorySubresource[])`
8. `Sonarr.Http|M:Sonarr.Http.PagingResource`1.#ctor(Sonarr.Http.PagingRequestResource)`
9. `Sonarr.Http|M:Sonarr.Http.PagingResourceMapper.MapToPagingSpec``2(Sonarr.Http.PagingResource{``0},System.Collections.Generic.HashSet{System.String},System.String,NzbDrone.Core.Datastore.SortDirection)`
10. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
11. `Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Paged(NzbDrone.Core.Datastore.PagingSpec{NzbDrone.Core.History.EpisodeHistory},System.Int32[],System.Int32[])`
12. `Sonarr.Core|M:NzbDrone.Core.DecisionEngine.Specifications.UpgradableSpecification.QualityCutoffNotMet(NzbDrone.Core.Profiles.Qualities.QualityProfile,NzbDrone.Core.Qualities.QualityModel,NzbDrone.Core.Qualities.QualityModel)`

<sub>commit 0bffefe · inputs 30ed2ea · slice of 131 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
