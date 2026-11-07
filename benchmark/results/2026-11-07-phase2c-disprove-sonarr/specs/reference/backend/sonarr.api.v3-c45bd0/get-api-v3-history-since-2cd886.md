# Get history since a date

`endpoint:Sonarr.Api.V3|GET /api/v3/History/since` · backend · Sonarr.Api.V3

From the code: GET /api/v3/History/since

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of history records, each with id, episode id, series id, source title, languages, quality, custom formats, custom format score, date, download id, event type and data. [1]
- **S2** Each record carries a quality-cutoff-not-met flag when the history record has a series. [2]
- **S3** When series is included, each record contains the series details (title, status, overview, network, path, quality profile id, seasons, ids, genres, tags and so on). [2][3]
- **S4** When episode is included, each record contains the episode details (season and episode number, title, air date, runtime, overview, has file, monitored, scene numbering and so on). [2][4]

## Actions

- **A1** A caller requests history since a given date, optionally filtered by event type, and optionally including series and episode details. [5] → `endpoint:Sonarr.Api.V3|GET /api/v3/History/since`

## Rules

- **R1** The date parameter is required; eventType is optional and defaults to none; includeSeries and includeEpisode default to false. [5]
- **R2** The custom format score is the sum of the scores of the series quality profile's format items that match the record's calculated custom formats. [6][1]
- **R3** Quality cutoff not met is true when the current quality ranks below the cutoff. The cutoff is the profile's cutoff if upgrades are allowed, otherwise its first allowed quality. [7]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 37 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryResourceMapper.ToResource(NzbDrone.Core.History.EpisodeHistory,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.MapToResource(NzbDrone.Core.History.EpisodeHistory,System.Boolean,System.Boolean)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.History.HistoryController.GetHistorySince(System.DateTime,System.Nullable{NzbDrone.Core.History.EpisodeHistoryEventType},System.Boolean,System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
7. `Sonarr.Core|M:NzbDrone.Core.DecisionEngine.Specifications.UpgradableSpecification.QualityCutoffNotMet(NzbDrone.Core.Profiles.Qualities.QualityProfile,NzbDrone.Core.Qualities.QualityModel,NzbDrone.Core.Qualities.QualityModel)`

<sub>commit 0bffefe · inputs ce87baa · slice of 94 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
