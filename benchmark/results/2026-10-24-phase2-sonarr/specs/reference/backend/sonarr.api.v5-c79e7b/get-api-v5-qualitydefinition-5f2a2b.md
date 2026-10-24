# List quality definitions (GET /api/v5/QualityDefinition)

`endpoint:Sonarr.Api.V5|GET /api/v5/QualityDefinition` · backend · Sonarr.Api.V5

From the code: GET /api/v5/QualityDefinition

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all quality definitions. [1]
- **S2** Each item in the list has an Id, Quality, Title and Weight. [2]

## Actions

- **A1** A caller sends GET to /api/v5/QualityDefinition and receives all quality definitions with a 200 OK response. [3][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/QualityDefinition`

## Rules

- **R1** The list is ordered by each definition's Weight, ascending. [4]
- **R2** Definitions come from a cache that holds them for 5 seconds before they are reloaded from the repository. [5]
- **R3** Definitions are keyed by Quality, so there is one entry per quality. [5]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 10 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Qualities.QualityDefinitionController.GetAll`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Qualities.QualityDefinitionResourceMapper.ToResource(NzbDrone.Core.Qualities.QualityDefinition)`
3. `endpoint:Sonarr.Api.V5|GET /api/v5/QualityDefinition`
4. `Sonarr.Core|M:NzbDrone.Core.Qualities.QualityDefinitionService.All`
5. `Sonarr.Core|M:NzbDrone.Core.Qualities.QualityDefinitionService.GetAll`

<sub>commit 0bffefe · inputs 056f267 · slice of 17 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
