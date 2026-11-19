# Health checks list (API v5)

`endpoint:Sonarr.Api.V5|GET /api/v5/Health` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Health

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of the current health check results, each as a health item. [1]
- **S2** Each item contains Id, Source (the source's name), Type, Reason, Message and WikiUrl (the full URI). [2]
- **S3** The list holds the values of the stored health check results, so it is empty when there are none. [3]

## Actions

- **A1** A caller sends GET /api/v5/Health to retrieve all current health check results. [4][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/Health`

## Rules

- **R1** The endpoint returns the stored results as they are and runs no new checks itself. [3][1]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 8 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Health.HealthController.GetHealth`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Health.HealthResourceMapper.ToResource(NzbDrone.Core.HealthCheck.HealthCheck)`
3. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheckService.Results`
4. `endpoint:Sonarr.Api.V5|GET /api/v5/Health`

<sub>commit 0bffefe · inputs 9b2579e · slice of 23 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
