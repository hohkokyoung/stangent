# Bulk series editor save

`endpoint:Sonarr.Api.V3|PUT /api/v3/series/editor` · backend · Sonarr.Api.V3

From the code: PUT /api/v3/series/editor

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the updated series as a list of series resources, with an Accepted response. [1][2]
- **S2** Each returned series includes fields such as id, title, status, path, quality profile, monitored flag, monitor new items, series type, season folder, tags, language, seasons and ratings. [3]

## Actions

- **A1** Applies the supplied changes to all series whose ids are listed in the request, then saves them in one update. [1][4][5] → `endpoint:Sonarr.Api.V3|PUT /api/v3/series/editor`

## Rules

- **R1** Monitored, monitor new items, quality profile, series type and season folder are changed only when a value is supplied in the request. [1]
- **R2** Root folder path is changed only when it is not null or whitespace. [1][6]
- **R3** Language is changed only when it is not null. [1]
- **R4** When tags are supplied, the apply mode decides the result: Add adds them to the existing tags, Remove removes them, Replace sets the tags to exactly the supplied ones. [1]
- **R5** Each modified series is validated by the series editor validator, and a validation exception with the errors is thrown if any series is invalid. [1]
- **R6** Validation runs on every series before any is saved, so an invalid series prevents the update. [1]

## Side effects

- **E1** Saves the changed series through the series service, passing the request's move-files flag. [1][5]

## Couldn't see

- the depth limit left out 4 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesEditorController.SaveAll(Sonarr.Api.V3.Series.SeriesEditorResource)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(System.Collections.Generic.IEnumerable{NzbDrone.Core.Tv.Series},System.Boolean)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(NzbDrone.Core.Tv.Series,System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.GetSeries(System.Collections.Generic.IEnumerable{System.Int32})`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.UpdateSeries(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`

<sub>commit 0bffefe · inputs 35e4bca · slice of 51 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
