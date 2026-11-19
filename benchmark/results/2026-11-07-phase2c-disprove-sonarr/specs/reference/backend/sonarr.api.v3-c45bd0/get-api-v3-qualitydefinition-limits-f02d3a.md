# Quality Definition Limits

`endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition/limits` · backend · Sonarr.Api.V3

From the code: GET /api/v3/QualityDefinition/limits

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a limits object holding a minimum and a maximum value for quality definitions. [1]

## Actions

- **A1** A caller sends GET /api/v3/QualityDefinition/limits and receives the minimum and maximum quality definition limits with an OK response. [2][1] → `endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition/limits`

## Rules

- **R1** The returned minimum and maximum come from the fixed QualityDefinitionLimits Min and Max values, not from request input. [1]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Qualities.QualityDefinitionController.GetLimits`
2. `endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition/limits`

<sub>commit 0bffefe · inputs e1ba4b9 · slice of 6 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
