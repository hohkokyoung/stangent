# Manual Import Reprocess Items

`endpoint:Sonarr.Api.V3|POST /api/v3/ManualImport` · backend · Sonarr.Api.V3

From the code: POST /api/v3/ManualImport

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the submitted list of items, each updated with the reprocessed season number, full episodes, release type, indexer flags, rejections, custom formats and custom format score. [1]
- **S2** Each rejection is returned with a reason (the rejection message) and a type. [2]
- **S3** Custom formats are returned with only id and name; specification details are not included. [3][1]
- **S4** Episode IDs on each returned item are cleared (null) in favour of the full episode objects. [1]

## Actions

- **A1** POST a list of manual import items; each is reprocessed using its path, download id, series, season, episodes, release group, quality, languages, indexer flags and release type. [1] → `endpoint:Sonarr.Api.V3|POST /api/v3/ManualImport`

## Rules

- **R1** An empty items list is rejected with a bad request: "items must be provided". [1][4]
- **R2** The request body must be JSON. [1]
- **R3** If an item's languages are only Unknown (or one/none) and the reprocessed result has languages, the item's languages are replaced by the result's. [1]
- **R4** If an item's quality is Unknown, it is replaced by the reprocessed quality. [1]
- **R5** If an item's release group is null or whitespace, it is replaced by the reprocessed release group. [1][5]
- **R6** When episode IDs are given, the file is evaluated against those episodes and the user-chosen series, episodes, release group, quality, languages, indexer flags and release type are reapplied after augmenting, before the import decision. [6]
- **R7** If a season is given but no episodes, the item is rejected with "Episodes not selected" and the season selection is kept. [6]
- **R8** If neither episodes nor season is given, the file is processed from scratch; an unknown series gives the rejection "Unknown Series". [6][7]
- **R9** If the language cannot be parsed from the path and the series is known, the series original language is used. [6]
- **R10** Custom formats and score are only calculated when a series is known and the item has a season number. [8]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 169 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.ManualImport.ManualImportController.ReprocessItems(System.Collections.Generic.List{Sonarr.Api.V3.ManualImport.ManualImportReprocessResource})`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.ManualImport.ImportRejectionResourceMapper.ToResource(NzbDrone.Core.MediaFiles.EpisodeImport.ImportRejection)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatResourceMapper.ToResource(NzbDrone.Core.CustomFormats.CustomFormat,System.Boolean)`
4. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
5. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNullOrWhiteSpace(System.String)`
6. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.ReprocessItem(System.String,System.String,System.Int32,System.Nullable{System.Int32},System.Collections.Generic.List{System.Int32},System.String,NzbDrone.Core.Qualities.QualityModel,System.Collections.Generic.List{NzbDrone.Core.Languages.Language},System.Int32,NzbDrone.Core.Parser.Model.ReleaseType)`
7. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.ProcessFile(System.String,System.String,System.String,System.String,NzbDrone.Core.Tv.Series)`
8. `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.MapItem(NzbDrone.Core.MediaFiles.EpisodeImport.ImportDecision,System.String,System.String,System.String)`

<sub>commit 0bffefe · inputs 48979f9 · slice of 183 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
