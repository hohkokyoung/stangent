# Queue Details Provider

`ts|frontend/src/Activity/Queue/Details/QueueDetailsProvider.tsx#QueueDetailsProvider` · component · sonarr

From the code: QueueDetailsProvider

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Makes the list of queue item details available to the components nested inside it. [1]

## Actions

- **A1** Fetches queue details from the /queue/details endpoint, passing the filter properties it receives as query parameters. [1][2] → [`endpoint:Sonarr.Api.V5|GET /api/v5/queue/details`](../backend/sonarr.api.v5-c79e7b/get-api-v5-queue-details-18f9cd.md), [`endpoint:Sonarr.Api.V3|GET /api/v3/queue/details`](../backend/sonarr.api.v3-c45bd0/get-api-v3-queue-details-3d027d.md)

## Rules

- **R1** The request is only made when at least one filter property is supplied. With no filter it is disabled. [1]
- **R2** Each request sends the Sonarr API key in the X-Api-Key header and the X-Sonarr-Client header set to Sonarr. [2]

## Side effects

- **E1** Sends a GET request to the queue details API. [2][1]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/Activity/Queue/Details/QueueDetailsProvider.tsx#QueueDetailsProvider`
2. `ts|frontend/src/Helpers/Hooks/useApiQuery.ts#useApiQuery`

<sub>commit 0bffefe · inputs 40da3ae · slice of 19 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
