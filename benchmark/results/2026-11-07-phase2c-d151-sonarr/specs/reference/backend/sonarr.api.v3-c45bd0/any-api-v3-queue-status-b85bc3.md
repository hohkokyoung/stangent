# Queue status endpoint (generic REST get-by-id handling)

`endpoint:Sonarr.Api.V3|ANY /api/v3/queue/status` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/queue/status

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the resource for the given id as JSON with HTTP 200 when it is found. [1]
- **S2** Returns HTTP 404 Not Found when no resource exists for the id (a model-not-found error is raised). [1]

## Actions

- **A1** A caller requests a single resource by id; the call is answered with the resource or a Not Found result. [1][2] → `endpoint:Sonarr.Api.V3|ANY /api/v3/queue/status`

## Rules

- **R1** For POST and PUT requests, every resource in the action arguments (single or a collection) is validated before the action runs. [3]
- **R2** On a PUT, if the body resource has Id 0 and the route has an id, the route id is used as the resource Id. [3]
- **R3** The id argument is validated when the action carries an id-validation attribute and validation is not skipped. [3]
- **R4** Validation can be skipped (including shared validation) through a skip-validation attribute on the action. [3]

## Side effects

- **E1** If the controller or action is marked deprecated, a warning "API call made to deprecated endpoint from {user agent}" is logged. [3]
- **E2** For deprecated endpoints the response gets a header "Deprecation: true". [3]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `endpoint:Sonarr.Api.V3|ANY /api/v3/queue/status`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs b2a09f5 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
