# Release API endpoint (get by id and request validation)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Release` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Release

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the resource as JSON with a 200 OK result when the requested id is found. [1]
- **S2** Returns a 404 Not Found result when no resource exists for the requested id. [1]
- **S3** Responses from deprecated endpoints carry the header Deprecation: true. [2]

## Actions

- **A1** A caller fetches a single resource by id; the call returns the resource or Not Found. [1] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Release`

## Rules

- **R1** On POST and PUT requests, every resource in the action arguments (single or a collection) is validated before the action runs. [2]
- **R2** On a PUT, if the body resource has Id 0 and the route has an id, the route id is used as the resource id. [2]
- **R3** The id argument is validated only when the action has an id-validation attribute and validation is not skipped. [2]
- **R4** A skip-validation attribute on the action can turn off validation, and can separately skip shared validation. [2]
- **R5** An endpoint is treated as deprecated if its controller or action carries the deprecated attribute. [2]

## Side effects

- **E1** A call to a deprecated endpoint logs a warning, "API call made to deprecated endpoint from" followed by the caller's user agent. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs eb9b84b · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
