# Get history (paged)

`endpoint:Sonarr.Api.V3|GET /api/v3/History` · backend · Sonarr.Api.V3

From the code: GET /api/v3/History

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a page of history records with page, page size, sort key, sort direction, total records and the records list. [1]
- **S2** Each history record shows id, episode id, series id, source title, languages, quality, custom formats, custom format score, date, download id, event type and data. [2]
- **S3** Each record carries a quality-cutoff-not-met flag when the history item has a series. [3]
- **S4** The series details are included in each record only when includeSeries is true. [3][4]
- **S5** The episode details are included in each record only when includeEpisode is true. [3][5]

## Actions

- **A1** A caller requests history with optional paging, includeSeries, includeEpisode, eventType, episodeId, downloadId, seriesIds, languages and quality parameters. [6] → `endpoint:Sonarr.Api.V3|GET /api/v3/History`

## Rules

- **R1** Page defaults to 1 and page size defaults to 10 when not supplied. [7]
- **R2** Sorting is allowed only by "date" or "series.sortTitle"; any other sort key falls back to "date". [6][8]
- **R3** Sort direction defaults to descending when none is given. [7][6]
- **R4** Results are filtered by event type only when at least one eventType is supplied. [6]
- **R5** Results are filtered by episode id when episodeId is supplied. [6]
- **R6** Results are filtered by download id only when downloadId is not null or whitespace. [6][9]
- **R7** Results are filtered by series only when at least one series id is supplied. [6]
- **R8** Language and quality filters are passed to the history service paging query. [10][11]
- **R9** The custom format score is the sum of the quality profile scores for the custom formats matched by the history item. [12][2]
- **R10** The cutoff is not met when the current quality ranks below the profile cutoff (or the first allowed quality if upgrades are not allowed), or when a new quality is a revision upgrade. [13]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 35 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.Extensions.RequestExtensions.ApplyToPage``2(NzbDrone.Core.Datastore.PagingSpec{``1},System.Func{NzbDrone.Core.Datastore.PagingSpec{``1},NzbDrone.Core.Datastore.PagingSpec{``1}},System.Converter{``1,``0})`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryResourceMapper.ToResource(NzbDrone.Core.History.EpisodeHistory,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.MapToResource(NzbDrone.Core.History.EpisodeHistory,System.Boolean,System.Boolean)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.GetHistory(Sonarr.Http.PagingRequestResource,System.Boolean,System.Boolean,System.Int32[],System.Nullable{System.Int32},System.String,System.Int32[],System.Int32[],System.Int32[])`
7. `Sonarr.Http|M:Sonarr.Http.PagingResource`1.#ctor(Sonarr.Http.PagingRequestResource)`
8. `Sonarr.Http|M:Sonarr.Http.PagingResourceMapper.MapToPagingSpec``2(Sonarr.Http.PagingResource{``0},System.Collections.Generic.HashSet{System.String},System.String,NzbDrone.Core.Datastore.SortDirection)`
9. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.History.HistoryService.Paged(NzbDrone.Core.Datastore.PagingSpec{NzbDrone.Core.History.EpisodeHistory},System.Int32[],System.Int32[])`
11. `Sonarr.Core|M:NzbDrone.Core.History.IHistoryRepository.GetPaged(NzbDrone.Core.Datastore.PagingSpec{NzbDrone.Core.History.EpisodeHistory},System.Int32[],System.Int32[])`
12. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
13. `Sonarr.Core|M:NzbDrone.Core.DecisionEngine.Specifications.UpgradableSpecification.QualityCutoffNotMet(NzbDrone.Core.Profiles.Qualities.QualityProfile,NzbDrone.Core.Qualities.QualityModel,NzbDrone.Core.Qualities.QualityModel)`

<sub>commit 0bffefe · inputs 99d73b0 · slice of 130 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
