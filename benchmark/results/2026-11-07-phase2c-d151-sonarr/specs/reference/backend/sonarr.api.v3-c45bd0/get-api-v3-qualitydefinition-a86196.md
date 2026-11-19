# List quality definitions (GET /api/v3/QualityDefinition)

`endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition` · backend · Sonarr.Api.V3

From the code: GET /api/v3/QualityDefinition

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of all quality definitions. [1]
- **S2** Each item has Id, Quality, Title, Weight, MinSize, MaxSize and PreferredSize. [2]

## Actions

- **A1** A caller requests all quality definitions with a GET and receives them as a list. [3][1] → `endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition`

## Rules

- **R1** The list is ordered by each definition's Weight, ascending. [4]
- **R2** Definitions are cached for 5 seconds, so changes may take up to that long to appear. [5]
- **R3** A null definition maps to null in the response. [2]

## Side effects

- **E1** Definitions are read from the repository and stored in a cache keyed "all" for 5 seconds. Nothing is saved or changed. [5]

## Couldn't see

- the depth limit left out 10 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Qualities.QualityDefinitionController.GetAll`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Qualities.QualityDefinitionResourceMapper.ToResource(NzbDrone.Core.Qualities.QualityDefinition)`
3. `endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition`
4. `Sonarr.Core|M:NzbDrone.Core.Qualities.QualityDefinitionService.All`
5. `Sonarr.Core|M:NzbDrone.Core.Qualities.QualityDefinitionService.GetAll`

<sub>commit 0bffefe · inputs fe8a010 · slice of 23 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
