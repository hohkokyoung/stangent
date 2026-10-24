# Series history (GET /api/v3/History/series)

`endpoint:Sonarr.Api.V3|GET /api/v3/History/series` · backend · Sonarr.Api.V3

From the code: GET /api/v3/History/series

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of history records for one series, each with id, episode id, series id, source title, languages, quality, date, download id, event type and data. [1]
- **S2** Each record includes its custom formats and a custom format score calculated from the series' quality profile. [1][2]
- **S3** Each record has a QualityCutoffNotMet flag, set when the record has a series. [3]
- **S4** The series details are included in each record only when includeSeries is true. [3][4]
- **S5** The episode details (season and episode number, title, air date, monitored, has file and so on) are included only when includeEpisode is true. [3][5]

## Actions

- **A1** A caller requests history for a series by seriesId, optionally filtered by seasonNumber and eventType, and may set includeSeries and includeEpisode. [6] → `endpoint:Sonarr.Api.V3|GET /api/v3/History/series`

## Rules

- **R1** When seasonNumber is supplied, only history for that season of the series is returned; otherwise history for the whole series is returned. [6][7][8]
- **R2** The optional eventType filter is passed through to the history lookup; it defaults to none. [6]
- **R3** includeSeries and includeEpisode default to false. [6]
- **R4** QualityCutoffNotMet is true when the record's quality is below the profile cutoff (or the first allowed quality if upgrades are not allowed). [9]
- **R5** The custom format score is the sum of the scores of the profile's format items whose format matches the record's custom formats. [2]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 35 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryResourceMapper.ToResource(NzbDrone.Core.History.EpisodeHistory,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
2. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.MapToResource(NzbDrone.Core.History.EpisodeHistory,System.Boolean,System.Boolean)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.GetSeriesHistory(System.Int32,System.Nullable{System.Int32},System.Nullable{NzbDrone.Core.History.EpisodeHistoryEventType},System.Boolean,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.History.HistoryService.GetBySeason(System.Int32,System.Int32,System.Nullable{NzbDrone.Core.History.EpisodeHistoryEventType})`
8. `Sonarr.Core|M:NzbDrone.Core.History.HistoryService.GetBySeries(System.Int32,System.Nullable{NzbDrone.Core.History.EpisodeHistoryEventType})`
9. `Sonarr.Core|M:NzbDrone.Core.DecisionEngine.Specifications.UpgradableSpecification.QualityCutoffNotMet(NzbDrone.Core.Profiles.Qualities.QualityProfile,NzbDrone.Core.Qualities.QualityModel,NzbDrone.Core.Qualities.QualityModel)`

<sub>commit 0bffefe · inputs b4e6243 · slice of 120 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
