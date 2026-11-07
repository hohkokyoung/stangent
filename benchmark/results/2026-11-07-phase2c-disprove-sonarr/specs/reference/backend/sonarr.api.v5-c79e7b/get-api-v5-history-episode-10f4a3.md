# Get episode history (API v5)

`endpoint:Sonarr.Api.V5|GET /api/v5/History/episode` · backend · Sonarr.Api.V5

From the code: GET /api/v5/History/episode

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of history records for the given episode, each with id, episode id, series id, source title, languages, quality, custom formats, custom format score, date, download id, event type and data. [1][2]
- **S2** Each record carries a quality-cutoff-not-met flag, set when the record has a series. [3]
- **S3** Each record includes the full series details only when the Series subresource is requested. [3][4]
- **S4** Each record includes the episode details (season/episode number, title, air date, runtime, monitored, has file, etc.) only when the Episode subresource is requested. [3][5]

## Actions

- **A1** A caller requests history for an episode by episodeId, optionally filtering by eventType and choosing includeSubresources. [1] → `endpoint:Sonarr.Api.V5|GET /api/v5/History/episode`

## Rules

- **R1** episodeId is required; eventType is optional and defaults to none (no filter). [1]
- **R2** The custom format score of a record is the sum of the quality profile's scores for the custom formats matched by that record. [6][2]
- **R3** Quality cutoff not met is true when the record's quality is below the cutoff (the profile cutoff if upgrades are allowed, otherwise the first allowed quality). [7][3]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 38 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryController.GetEpisodeHistory(System.Int32,System.Nullable{NzbDrone.Core.History.EpisodeHistoryEventType},Sonarr.Api.V5.History.HistorySubresource[])`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryResourceMapper.ToResource(NzbDrone.Core.History.EpisodeHistory,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.History.HistoryController.MapToResource(NzbDrone.Core.History.EpisodeHistory,System.Boolean,System.Boolean)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
6. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
7. `Sonarr.Core|M:NzbDrone.Core.DecisionEngine.Specifications.UpgradableSpecification.QualityCutoffNotMet(NzbDrone.Core.Profiles.Qualities.QualityProfile,NzbDrone.Core.Qualities.QualityModel,NzbDrone.Core.Qualities.QualityModel)`

<sub>commit 0bffefe · inputs bf66771 · slice of 125 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
