# Wanted/Missing API endpoint (shared REST controller behaviour)

`endpoint:Sonarr.Api.V3|ANY /api/v3/wanted/missing` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/wanted/missing

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** A get-by-id action returns the requested resource as JSON with an Ok result. [1]
- **S2** When the resource is not found (model not found), the get-by-id action returns a NotFound result. [1]
- **S3** Calls to a deprecated controller or action get a response header Deprecation set to true. [2]

## Actions

- **A1** Fetch a single resource by id; returns it as JSON, or NotFound if it does not exist. [1] → `endpoint:Sonarr.Api.V3|ANY /api/v3/wanted/missing`

## Rules

- **R1** For POST and PUT requests, every resource in the action arguments (single or a collection) is validated before the action runs. [2]
- **R2** On a PUT, if the body resource's Id is 0 and the route has an id, the route id is used as the resource Id. [2]
- **R3** The id argument is validated only when the action carries an id-validation attribute and validation is not skipped via the skip-validation attribute. [2]
- **R4** A skip-validation attribute on the action can skip validation and can skip shared validation. [2]

## Side effects

- **E1** A call to a deprecated endpoint writes a warning log entry including the caller's user agent. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs 6af4b43 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
