# List tags (GET /api/v3/Tag)

`endpoint:Sonarr.Api.V3|GET /api/v3/Tag` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Tag

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all tags, each with an Id and a Label. [1][2]

## Actions

- **A1** A caller requests all tags and receives them as a list of tag resources. [3][1] → `endpoint:Sonarr.Api.V3|GET /api/v3/Tag`

## Rules

- **R1** Tags are returned ordered by Label. [4]
- **R2** The response is always a list; it is empty when there are no tags. [5][4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 5 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.GetAll`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagResourceMapper.ToResource(NzbDrone.Core.Tags.Tag)`
3. `endpoint:Sonarr.Api.V3|GET /api/v3/Tag`
4. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.All`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagResourceMapper.ToResource(System.Collections.Generic.IEnumerable{NzbDrone.Core.Tags.Tag})`

<sub>commit 0bffefe · inputs fe1d5f6 · slice of 14 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
