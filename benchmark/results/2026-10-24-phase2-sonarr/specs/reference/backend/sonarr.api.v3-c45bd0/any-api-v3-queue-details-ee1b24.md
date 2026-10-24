# Queue details endpoint (generic REST base behaviour)

`endpoint:Sonarr.Api.V3|ANY /api/v3/queue/details` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/queue/details

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success the get-by-id action returns the resource as JSON with an Ok result. [1]
- **S2** When the requested resource is not found, it returns a NotFound result. [1]
- **S3** For calls to a deprecated endpoint, the response carries the header Deprecation: true. [2]

## Actions

- **A1** A caller requests a resource by id; the id is passed to the resource lookup and the result is returned as Ok or NotFound. [1] → `endpoint:Sonarr.Api.V3|ANY /api/v3/queue/details`

## Rules

- **R1** Before the action runs, for POST and PUT requests each resource in the arguments is validated, unless validation is skipped for that method. [2]
- **R2** For a PUT whose body resource has Id 0, the id is taken from the route id when one is present. [2]
- **R3** The id argument is validated when the action carries an id-validation attribute and validation is not skipped. [2]
- **R4** Validation can be skipped, including shared validation, through a skip-validation attribute on the action. [2]

## Side effects

- **E1** A call to a deprecated controller or action logs a warning including the caller's user agent. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs c5692e6 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
