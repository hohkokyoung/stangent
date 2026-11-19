# Health API endpoint (get by id)

`endpoint:Sonarr.Api.V5|ANY /api/v5/Health` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Health

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the requested resource as JSON with an OK result when it is found. [1]
- **S2** Returns a Not Found result when the resource for the given id does not exist. [1]

## Actions

- **A1** A caller requests a single resource by id; the resource is looked up and returned as JSON. [1] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Health`

## Rules

- **R1** For actions marked to validate the id, the id argument is validated before the action runs, unless validation is skipped for that action. [2]
- **R2** For POST and PUT requests, each resource in the request body is validated; on PUT, a body resource with Id 0 takes the id from the route. [2]

## Side effects

- **E1** If the controller or action is marked deprecated, a warning with the caller's user agent is logged and the response gets the header Deprecation: true. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs a42e413 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
