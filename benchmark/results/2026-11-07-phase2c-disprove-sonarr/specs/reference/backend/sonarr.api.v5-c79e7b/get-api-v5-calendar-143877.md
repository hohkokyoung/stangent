# Calendar (episodes between dates)

`endpoint:Sonarr.Api.V5|GET /api/v5/Calendar` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Calendar

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of episodes airing in the requested date range, sorted by air date (UTC) ascending. [1]
- **S2** Each episode shows id, series id, TVDB id, episode file id, season and episode number, title, air date, air date UTC, runtime, finale type, overview, last search time, has-file, monitored, absolute and scene numbering and unverified scene numbering flag. [2]
- **S3** When the Series subresource is requested, each episode includes its series details (title, status, overview, network, path, quality profile, monitored flag, tags, ratings, external ids and more). [3][4]
- **S4** When the EpisodeFile subresource is requested, episodes that have a file include file details: path, size, date added, scene name, release group, languages, quality, media info, custom formats and score, quality-cutoff-not-met flag, indexer flags, release type. [5][4]
- **S5** When the Images subresource is requested, each episode includes its images. [4]

## Actions

- **A1** Caller requests the calendar with optional start, end, includeUnmonitored, includeSpecials, tags and includeSubresources query parameters. [1] → `endpoint:Sonarr.Api.V5|GET /api/v5/Calendar`

## Rules

- **R1** Start defaults to today and end defaults to two days after today. [1]
- **R2** includeUnmonitored defaults to false and includeSpecials defaults to true. [1]
- **R3** Start and end are converted to UTC before episodes are looked up. [6]
- **R4** Tags is a comma-separated list; each item is resolved as a tag id if all digits, otherwise by tag label. [1][7]
- **R5** When tags are given, only episodes whose series has at least one of those tags are returned; with no tags, no tag filtering occurs. [1][8]
- **R6** Episodes whose series is not found are left out. [1]
- **R7** The file details are included only for episodes with a non-zero episode file id. [4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 94 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Calendar.CalendarController.GetCalendar(System.Nullable{System.DateTime},System.Nullable{System.DateTime},System.Boolean,System.Boolean,System.String,Sonarr.Api.V5.Calendar.CalendarSubresource[])`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeControllerWithSignalR.MapToResource(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.EpisodeFiles.EpisodeFileResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.Tv.Series,NzbDrone.Core.DecisionEngine.Specifications.IUpgradableSpecification,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
6. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.EpisodesBetweenDates(System.DateTime,System.DateTime,System.Boolean,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.GetTag(System.String)`
8. `Sonarr.Common|M:NzbDrone.Common.Extensions.EnumerableExtensions.None``1(System.Collections.Generic.IEnumerable{``0},System.Func{``0,System.Boolean})`

<sub>commit 0bffefe · inputs 128b31f · slice of 108 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
