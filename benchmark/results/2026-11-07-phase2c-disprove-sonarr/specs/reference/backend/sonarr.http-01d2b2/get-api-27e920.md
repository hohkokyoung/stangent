# API version info

`endpoint:Sonarr.Http|GET /api` · backend · Sonarr.Http

From the code: GET /api

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON object with the current API version "v5". [1]
- **S2** Returns a list of deprecated API versions, which contains only "v3". [1]

## Actions

- **A1** A caller sends GET /api and receives a 200 OK response with the API info as application/json. [2][1] → `endpoint:Sonarr.Http|GET /api`

## Rules

- **R1** The current and deprecated version values are fixed in code (current "v5", deprecated ["v3"]), not read from settings or data. [1]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Http|M:NzbDrone.Http.ApiInfoController.GetApiInfo`
2. `endpoint:Sonarr.Http|GET /api`

<sub>commit 0bffefe · inputs f3f4a35 · slice of 5 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
