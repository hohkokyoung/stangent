# Tag detail by id endpoint

`endpoint:Sonarr.Api.V5|ANY /api/v5/tag/detail` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/tag/detail

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the resource with the requested id as JSON with an OK result. [1]
- **S2** Returns a Not Found result when the resource with the requested id does not exist. [1]

## Actions

- **A1** A caller requests a single resource by id and receives it, or Not Found if no such resource exists. [1][2] → `endpoint:Sonarr.Api.V5|ANY /api/v5/tag/detail`

## Rules

- **R1** For actions marked to validate the id, and not marked to skip validation, the id argument is validated before the action runs. [3]
- **R2** For POST and PUT requests, each resource in the request arguments is validated before the action runs. [3]
- **R3** On a PUT with a resource id of 0, the id from the route is used as the resource id. [3]

## Side effects

- **E1** If the controller or action is marked deprecated, a warning with the caller's user agent is logged and the response gets a Deprecation: true header. [3]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `endpoint:Sonarr.Api.V5|ANY /api/v5/tag/detail`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs e824f04 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
