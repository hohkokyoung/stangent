# List manual import candidates (GET /api/v3/ManualImport)

`endpoint:Sonarr.Api.V3|GET /api/v3/ManualImport` · backend · Sonarr.Api.V3

From the code: GET /api/v3/ManualImport

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of manual import items, each with id, path, relative path, folder name, name, size, series, season number, episodes, episode file id, release group, quality, languages, custom formats, custom format score, download id, indexer flags, release type and rejections. [1]
- **S2** Each item's id is a non-negative 31-bit hash of its file path. [1][2]
- **S3** Each rejection is shown with a reason (the rejection message) and a type. [3]
- **S4** Each item includes a quality weight computed from the quality definition weight plus the revision. [4]
- **S5** When the folder or download yields nothing (unknown download, or path is neither a folder nor a file), an empty list is returned. [5]

## Actions

- **A1** Caller requests manual import items by folder, downloadId, seriesId, seasonNumber and filterExistingFiles. [6] → `endpoint:Sonarr.Api.V3|GET /api/v3/ManualImport`

## Rules

- **R1** filterExistingFiles defaults to true. [6]
- **R2** When seriesId is given and downloadId is blank, the series' existing files (optionally limited to seasonNumber) are listed; otherwise the folder/download is scanned. [6][7]
- **R3** In series mode with no season number, video files in the series folder not already tracked are added as items with Unknown quality, Unknown language, no episodes and no rejections. [8][9]
- **R4** When a downloadId is given, the path comes from the tracked download's output path; an unknown download or one without an import item gives an empty list. [5]
- **R5** If the path is a single file, only that file is processed; if it is a folder, the folder is processed. [5]
- **R6** If the series is unknown for a folder and it has more than 100 video files, the files are not parsed. [10]
- **R7** A file whose series cannot be determined gets a rejection "Unknown Series". [11]
- **R8** Custom format score is the profile's score for the item's custom formats, or 0 when the series has no quality profile. [1][12]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 200 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.ManualImport.ManualImportResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportItem)`
2. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.ManualImport.ImportRejectionResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeImport.ImportRejection)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.ManualImport.ManualImportController.AddQualityWeight(Sonarr.Api.V3.ManualImport.ManualImportResource)`
5. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.GetMediaFiles(System.String,System.String,System.Nullable{System.Int32},System.Boolean)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.ManualImport.ManualImportController.GetMediaFiles(System.String,System.String,System.Nullable{System.Int32},System.Nullable{System.Int32},System.Boolean)`
7. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNullOrWhiteSpace(System.String)`
8. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.GetMediaFiles(System.Int32,System.Nullable{System.Int32})`
9. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.MediaFileService.FilterExistingFiles(System.Collections.Generic.List{System.String},System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile},NzbDrone.Core.Tv.Series)`
10. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.ProcessFolder(System.String,System.String,System.String,System.Nullable{System.Int32},System.Boolean)`
11. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.ProcessFile(System.String,System.String,System.String,System.String,NzbDrone.Core.Tv.Series)`
12. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`

<sub>commit 0bffefe · inputs 2e5cdd7 · slice of 106 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
