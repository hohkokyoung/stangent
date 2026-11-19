# Parse release title (GET /api/v3/Parse)

`endpoint:Sonarr.Api.V3|GET /api/v3/Parse` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Parse

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a parse result containing the title, parsed episode info, matched series, matched episodes, languages, custom formats and custom format score. [1]
- **S2** When the title is blank, nothing (null) is returned. [1]
- **S3** When the title cannot be parsed, the result contains only the title. [1]
- **S4** When parsing works but no series/episodes are matched, the result contains the title and the parsed episode info only. [1]
- **S5** Parsed episode info includes release title, series title, quality, season/episode/absolute numbers, air date, languages, release group, release hash, release type and flags such as full season, daily, special and multi-season. Season number is -1 when none is parsed. [2]
- **S6** Custom formats in the result are returned without details (only id and name). [1][3]

## Actions

- **A1** A caller supplies a title and optionally a path; the endpoint parses it and returns the matched series, episodes, languages and custom format information. [1] → `endpoint:Sonarr.Api.V3|GET /api/v3/Parse`

## Rules

- **R1** If a path is given, it is parsed as a file path; otherwise the title is parsed. [1][4]
- **R2** If title parsing fails and no path was given, the title is parsed as a season title. [1][5]
- **R3** The series and episodes are looked up with tvdb id 0, tv rage id 0 and no imdb id. [1]
- **R4** Custom formats are calculated with size 0, and the score is the quality profile's custom format score, or 0 when the series has no quality profile. [1][6]
- **R5** The custom format score is the sum of scores of the profile's format items whose format matched. [6]
- **R6** A season-title parse sets the result as a full season with no season number, and unparsable titles with leftover brackets return null. [5]
- **R7** Aggregators run on the matched release one by one; a failure in one is logged as a warning and does not stop the others. [7]

## Side effects

- **E1** The matched release is augmented by the aggregation service before custom formats are calculated. [1][7]

## Couldn't see

- the depth limit left out 46 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Parse.ParseController.Parse(System.String,System.String)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Parse.ParsedEpisodeInfoResourceMapper.ToResource(NzbDrone.Core.Parser.Model.ParsedEpisodeInfo)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatResourceMapper.ToResource(NzbDrone.Core.CustomFormats.CustomFormat,System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.Parser.Parser.ParsePath(System.String)`
5. `Sonarr.Core|M:NzbDrone.Core.Parser.Parser.ParseSeasonTitle(System.String)`
6. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
7. `Sonarr.Core|M:NzbDrone.Core.Download.Aggregation.RemoteEpisodeAggregationService.Augment(NzbDrone.Core.Parser.Model.RemoteEpisode)`

<sub>commit 0bffefe · inputs 27dbfaf · slice of 218 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
