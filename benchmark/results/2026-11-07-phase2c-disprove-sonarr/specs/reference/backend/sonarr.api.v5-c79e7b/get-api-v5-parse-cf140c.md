# Parse release title or path

`endpoint:Sonarr.Api.V5|GET /api/v5/Parse` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Parse

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON parse result containing the submitted title, and when parsing succeeds the parsed episode info. [1]
- **S2** When the title is blank, or nothing could be parsed from it, the result contains only the title. [1]
- **S3** When the parsed info maps to a series, the result also includes the series, matched episodes, languages, custom formats (without details) and the custom format score. [1][2]
- **S4** When parsing succeeds but no series/episode mapping is found, the result contains the title and the parsed episode info only. [1]
- **S5** Each custom format in the result shows only its id and name, because details are excluded. [3]
- **S6** Each episode in the result includes id, series id, season and episode numbers, title, air dates, runtime, overview, file and monitored flags, and scene numbering. [4]

## Actions

- **A1** A caller sends GET /api/v5/Parse with optional title and path query values to parse a release name or file path. [1] → `endpoint:Sonarr.Api.V5|GET /api/v5/Parse`

## Rules

- **R1** If the title is null or whitespace, no parsing is done and only the title is returned. [1][5]
- **R2** If a path is provided (not blank), it is parsed as a path; otherwise the title is parsed as a release title. [1][6]
- **R3** If title parsing fails and no path was given, the title is parsed as a season title as a fallback. [1][7]
- **R4** Season-title parsing returns nothing for anime special markers, or when the remaining series title is blank or still contains brackets. [7]
- **R5** The custom format score is the sum of scores of the series quality profile's format items that match the release's custom formats, or 0 when there is no quality profile. [1][8]
- **R6** Custom formats are calculated with a size of 0, and the mapping uses tvdb id 0 and tvrage id 0 with no imdb id. [1]
- **R7** Failures in individual aggregators are logged as warnings and do not stop the augmentation. [9]

## Side effects

- **E1** The mapped remote episode is augmented by the aggregation service before custom formats are calculated. [1][9]

## Couldn't see

- the depth limit left out 46 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Parse.ParseController.Parse(System.String,System.String)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatResourceMapper.ToResource(System.Collections.Generic.IEnumerable{NzbDrone.Core.CustomFormats.CustomFormat},System.Boolean)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatResourceMapper.ToResource(NzbDrone.Core.CustomFormats.CustomFormat,System.Boolean)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
5. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNullOrWhiteSpace(System.String)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
7. `Sonarr.Core|M:NzbDrone.Core.Parser.Parser.ParseSeasonTitle(System.String)`
8. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
9. `Sonarr.Core|M:NzbDrone.Core.Download.Aggregation.RemoteEpisodeAggregationService.Augment(NzbDrone.Core.Parser.Model.RemoteEpisode)`

<sub>commit 0bffefe · inputs 8887a0d · slice of 171 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
