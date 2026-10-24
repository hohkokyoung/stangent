# Get Episodes (API v5)

`endpoint:Sonarr.Api.V5|GET /api/v5/Episode` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Episode

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of episodes, each with id, series id, TVDB id, episode file id, season and episode number, title, air date (local and UTC), runtime, finale type, overview, last search time, has-file and monitored flags. [1]
- **S2** Each episode also carries absolute, scene absolute, scene episode and scene season numbers, and an unverified-scene-numbering flag. [1]
- **S3** When the Series subresource is requested, each episode includes its series details such as title, status, overview, network, path, quality profile id, seasons, genres, tags and ratings. [2][3]
- **S4** When the EpisodeFile subresource is requested, episodes that have a file include file details: path, size, date added, scene name, release group, languages, quality, media info, quality-cutoff-not-met flag, custom formats and score, indexer flags and release type. [4][3]
- **S5** When the Images subresource is requested, each episode includes its images. [3]
- **S6** Media info shows video codec, bit depth, bitrate, fps, dynamic range, resolution as widthxheight, run time, scan type, and audio and subtitle streams; it is empty when the file has no media info. [5]

## Actions

- **A1** Lists the episodes of a series when seriesId is given. [6][7] → `endpoint:Sonarr.Api.V5|GET /api/v5/Episode`
- **A2** Lists only the episodes of one season when seriesId and seasonNumber are both given. [6][8] → `endpoint:Sonarr.Api.V5|GET /api/v5/Episode`
- **A3** Returns the episodes with the given episodeIds when no seriesId is given. [6][9] → `endpoint:Sonarr.Api.V5|GET /api/v5/Episode`
- **A4** Returns the episodes linked to an episode file when only episodeFileId is given. [6][10][11] → `endpoint:Sonarr.Api.V5|GET /api/v5/Episode`
- **A5** Optional includeSubresources values Series, EpisodeFile and Images add that data to each episode. [6] → `endpoint:Sonarr.Api.V5|GET /api/v5/Episode`

## Rules

- **R1** Filter precedence: seriesId first (optionally narrowed by seasonNumber), then episodeIds, then episodeFileId. seasonNumber is ignored without seriesId. [6]
- **R2** If none of seriesId, episodeIds or episodeFileId is supplied, the request fails with a 400 Bad Request: "seriesId or episodeIds must be provided". [6][12]
- **R3** Episode file details are only added for episodes whose episode file id is not 0. [3]
- **R4** The custom format score is the sum of the quality profile scores of the matched custom formats, or 0 if there is no profile. [13][4]
- **R5** The episode file path is the series path combined with the file's relative path. [4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 95 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeResourceMapper.ToResource(NzbDrone.Core.Tv.Episode)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeControllerWithSignalR.MapToResource(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.EpisodeFiles.EpisodeFileResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeFile,NzbDrone.Core.Tv.Series,NzbDrone.Core.DecisionEngine.Specifications.IUpgradableSpecification,NzbDrone.Core.CustomFormats.ICustomFormatCalculationService)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.EpisodeFiles.MediaInfoResourceMapper.ToResource(NzbDrone.Core.MediaFiles.MediaInfo.MediaInfoModel,System.String)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Episodes.EpisodeController.GetEpisodes(System.Nullable{System.Int32},System.Nullable{System.Int32},System.Collections.Generic.List{System.Int32},System.Nullable{System.Int32},Sonarr.Api.V5.Episodes.EpisodeSubresource[])`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodeBySeries(System.Int32)`
8. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodesBySeason(System.Int32,System.Int32)`
9. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodes(System.Collections.Generic.IEnumerable{System.Int32})`
10. `Sonarr.Core|M:NzbDrone.Core.Tv.IEpisodeService.GetEpisodesByFileId(System.Int32)`
11. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeService.GetEpisodesByFileId(System.Int32)`
12. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
13. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`

<sub>commit 0bffefe · inputs bdcad56 · slice of 90 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
