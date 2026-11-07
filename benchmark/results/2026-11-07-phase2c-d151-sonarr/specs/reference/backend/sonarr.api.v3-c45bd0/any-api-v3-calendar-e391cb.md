# Calendar API endpoint (shared REST controller behaviour)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Calendar` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Calendar

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Get-by-id returns the resource as JSON with an Ok result when it is found. [1]
- **S2** Get-by-id returns a NotFound result when the resource is not found. [1]
- **S3** Responses from deprecated endpoints carry the header Deprecation: true. [2]

## Actions

- **A1** A caller requests a single resource by id and gets it as JSON, or NotFound if it does not exist. [1] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Calendar`

## Rules

- **R1** On POST and PUT, each resource in the request arguments (single or list) is validated before the action runs. [2]
- **R2** On PUT, if the body resource has Id 0 and the route has an id, the route id is used as the resource id. [2]
- **R3** The id argument is validated only when the action carries an id-validation attribute and validation is not skipped. [2]
- **R4** A skip-validation attribute on the action can turn off validation, and can separately skip shared validation. [2]

## Side effects

- **E1** A call to an endpoint marked deprecated (on the controller or action) logs a warning with the caller's user agent and sets the response header Deprecation: true. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs 04aa1db · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
