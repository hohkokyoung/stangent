# Get episodes (GET /api/v3/Episode)

`endpoint:Sonarr.Api.V3|GET /api/v3/Episode` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Episode

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of episodes, each with id, series id, TVDB id, episode file id, season and episode number, title, air date (local and UTC), runtime, finale type, overview and last search time. [1]
- **S2** Each episode also shows whether it has a file, whether it is monitored, absolute episode number, scene absolute/episode/season numbers and whether scene numbering is unverified. [1]
- **S3** When series details are requested, each episode includes its series (title, overview, network, path, quality profile id, genres, tags, ratings, external ids and similar fields). [2][3]
- **S4** When episode file details are requested, an episode with a file includes the file's path, size, date added, scene name, release group, languages, quality, media info, quality-cutoff-not-met flag, custom formats, custom format score, indexer flags and release type. [4][3]
- **S5** Media info includes audio and video codec, bitrates, channels, languages, resolution (WxH), runtime, scan type, frame rate and subtitles. [5]
- **S6** When images are requested, each episode includes its images. [3]

## Actions

- **A1** Caller lists episodes by seriesId and seasonNumber, returning that season's episodes of the series. [6][7] → `endpoint:Sonarr.Api.V3|GET /api/v3/Episode`
- **A2** Caller lists episodes by seriesId alone, returning all episodes of the series. [6][8] → `endpoint:Sonarr.Api.V3|GET /api/v3/Episode`
- **A3** Caller lists episodes by a list of episodeIds. [6][9] → `endpoint:Sonarr.Api.V3|GET /api/v3/Episode`
- **A4** Caller lists episodes by episodeFileId, returning the episodes belonging to that file. [6][10] → `endpoint:Sonarr.Api.V3|GET /api/v3/Episode`
- **A5** Caller can set includeSeries, includeEpisodeFile and includeImages to add series, file and image data to each episode. [6] → `endpoint:Sonarr.Api.V3|GET /api/v3/Episode`

## Rules

- **R1** Filter priority: seriesId (with optional seasonNumber) first, then episodeIds, then episodeFileId; the first one supplied is used. [6]
- **R2** If none of seriesId, episodeIds or episodeFileId is provided, the request fails with a bad request: "seriesId or episodeIds must be provided". [6][11]
- **R3** includeSeries, includeEpisodeFile and includeImages all default to false. [6]
- **R4** Episode file data is only added to episodes whose episode file id is not 0. [3]
- **R5** The custom format score is the sum of the quality profile scores of the file's matching custom formats, or 0 when there is no profile. [12][4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 104 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeControllerWithSignalR.MapToResource(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.EpisodeFiles.EpisodeFileResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.Tv.Series,NzbDrone.Core.DecisionEngine.Specifications.IUpgradableSpecification,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.EpisodeFiles.MediaInfoResourceMapper.ToResource(NzbDrone.Core.MediaFiles.MediaInfo.MediaInfoModel,System.String)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.Episodes.EpisodeController.GetEpisodes(System.Nullable{System.Int32},System.Nullable{System.Int32},System.Collections.Generic.List{System.Int32},System.Nullable{System.Int32},System.Boolean,System.Boolean,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodesBySeason(System.Int32,System.Int32)`
8. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodeBySeries(System.Int32)`
9. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodes(System.Collections.Generic.IEnumerable{System.Int32})`
10. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.GetEpisodesByFileId(System.Int32)`
11. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
12. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`

<sub>commit 0bffefe · inputs ea76d99 · slice of 89 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
