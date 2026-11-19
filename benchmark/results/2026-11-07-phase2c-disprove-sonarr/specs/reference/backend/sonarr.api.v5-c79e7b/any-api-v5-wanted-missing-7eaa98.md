# Wanted / Missing endpoint (REST controller base behaviour)

`endpoint:Sonarr.Api.V5|ANY /api/v5/wanted/missing` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/wanted/missing

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Get-by-id returns the resource as JSON with an OK result when it is found. [1]
- **S2** Get-by-id returns a Not Found result when the resource is not found (a model-not-found error). [1]
- **S3** Calls to a deprecated endpoint get a response header Deprecation set to true. [2]

## Actions

- **A1** A caller can fetch a single resource by its id; the result is OK with the resource or Not Found. [1] → `endpoint:Sonarr.Api.V5|ANY /api/v5/wanted/missing`

## Rules

- **R1** For POST and PUT requests, every resource in the action arguments (single or a list) is validated before the action runs. [2]
- **R2** On a PUT, if the body resource has Id 0 and the route has an id, the route id is used as the resource Id. [2]
- **R3** The id argument is validated only when the action carries an id-validation attribute and validation is not skipped. [2]
- **R4** A skip-validation attribute on the action can turn off validation, and can separately skip shared validation. [2]

## Side effects

- **E1** A warning "API call made to deprecated endpoint from {user agent}" is logged when the controller or action is marked deprecated. [2]
- **E2** On a PUT, the route id may be written into the body resource's Id. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs cd662b2 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
