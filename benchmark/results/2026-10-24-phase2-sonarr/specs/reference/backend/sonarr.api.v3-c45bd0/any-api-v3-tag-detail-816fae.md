# Tag detail by ID (GET resource by id)

`endpoint:Sonarr.Api.V3|ANY /api/v3/tag/detail` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/tag/detail

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the requested resource as JSON with an OK result when it is found. [1]
- **S2** Returns a Not Found result when the resource for the given id does not exist. [1]

## Actions

- **A1** A caller requests a single resource by its integer id and receives the resource or Not Found. [1][2] → `endpoint:Sonarr.Api.V3|ANY /api/v3/tag/detail`

## Rules

- **R1** When the action is marked for id validation and validation is not skipped, the id argument is validated before the action runs. [3]
- **R2** For POST and PUT requests, each resource in the arguments is validated; on PUT, a resource with Id 0 takes the id from the route if present. [3]

## Side effects

- **E1** If the controller or action is marked deprecated, a warning is logged with the caller's user agent and the response gets header Deprecation: true. [3]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `endpoint:Sonarr.Api.V3|ANY /api/v3/tag/detail`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs b040115 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
