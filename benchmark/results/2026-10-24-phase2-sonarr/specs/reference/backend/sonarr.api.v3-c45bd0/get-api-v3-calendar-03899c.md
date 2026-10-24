# Calendar (GET /api/v3/Calendar)

`endpoint:Sonarr.Api.V3|GET /api/v3/Calendar` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Calendar

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of episodes airing in the requested date range, sorted by air date (UTC) ascending. [1]
- **S2** Each episode includes id, series id, TVDB id, episode file id, season and episode numbers, title, air date and air date UTC, runtime, finale type, overview, last search time, has-file, monitored, absolute and scene numbering fields. [2]
- **S3** When series is requested, each episode carries its series details (title, overview, network, air time, images, seasons, path, quality profile, monitored flag, external ids, genres, tags, ratings and more). [3][4]
- **S4** When episode file is requested, episodes that have a file carry file details: path, size, date added, scene name, release group, languages, quality, media info, quality-cutoff-not-met flag, custom formats, custom format score, indexer flags and release type. [5][4]
- **S5** Media info shows audio and video codec, bitrates, channels, languages, stream count, bit depth, fps, dynamic range, resolution as WxH, runtime, scan type and subtitles. [6]
- **S6** When episode images are requested, each episode carries its images. [4]

## Actions

- **A1** A caller requests the calendar with optional start, end, unmonitored, includeSeries, includeEpisodeFile, includeEpisodeImages and tags (comma-separated) parameters. [1] → `endpoint:Sonarr.Api.V3|GET /api/v3/Calendar`

## Rules

- **R1** If start is not given it defaults to today; if end is not given it defaults to two days after today. [1]
- **R2** unmonitored, includeSeries, includeEpisodeFile and includeEpisodeImages default to false, and tags defaults to empty. [1]
- **R3** Unmonitored episodes are included only when unmonitored is true; specials are always included. [1][7]
- **R4** The start and end dates are converted to UTC before episodes are looked up. [8]
- **R5** Episodes whose series cannot be found are left out. [1]
- **R6** When tags are given, only episodes whose series has at least one of the listed tags are returned. [1][9]
- **R7** Each entry in tags is treated as a tag id if it is all digits, otherwise as a tag label. [10]
- **R8** The tags filter applies only when the tags text is not null or whitespace. [11][1]
- **R9** The episode file is attached only if the episode has a file (episode file id not 0). [4]
- **R10** The custom format score is the sum of the quality profile scores for the file's matching custom formats, or 0 if there is no quality profile. [12][5]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 103 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Calendar.CalendarController.GetCalendar(System.Nullable{System.DateTime},System.Nullable{System.DateTime},System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.String)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeControllerWithSignalR.MapToResource(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.EpisodeFiles.EpisodeFileResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.Tv.Series,NzbDrone.Core.DecisionEngine.Specifications.IUpgradableSpecification,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.EpisodeFiles.MediaInfoResourceMapper.ToResource(NzbDrone.Core.MediaFiles.MediaInfo.MediaInfoModel,System.String)`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.EpisodesBetweenDates(System.DateTime,System.DateTime,System.Boolean,System.Boolean)`
8. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.EpisodesBetweenDates(System.DateTime,System.DateTime,System.Boolean,System.Boolean)`
9. `Sonarr.Common|M:NzbDrone.Common.Extensions.EnumerableExtensions.None``1(System.Collections.Generic.IEnumerable{``0},System.Func{``0,System.Boolean})`
10. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.GetTag(System.String)`
11. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`

<sub>commit 0bffefe · inputs 3473a64 · slice of 107 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
