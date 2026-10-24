# List tags (GET /api/v5/Tag)

`endpoint:Sonarr.Api.V5|GET /api/v5/Tag` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Tag

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all tags, each with its Id and Label. [1][2]

## Actions

- **A1** A caller requests all tags and receives them with an OK response. [3][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/Tag`

## Rules

- **R1** Tags are sorted by Label before being returned. [4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 5 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagController.GetAll`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagResourceMapper.ToResource(NzbDrone.Core.Tags.Tag)`
3. `endpoint:Sonarr.Api.V5|GET /api/v5/Tag`
4. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.All`

<sub>commit 0bffefe · inputs ead8bb9 · slice of 14 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
