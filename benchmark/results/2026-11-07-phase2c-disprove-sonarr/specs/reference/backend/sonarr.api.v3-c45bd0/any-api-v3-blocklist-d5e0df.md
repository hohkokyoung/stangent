# Delete blocklist entry

`endpoint:Sonarr.Api.V3|ANY /api/v3/Blocklist` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Blocklist

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** A caller deletes a single blocklist entry by its numeric id. The call returns nothing. [1][2] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Blocklist`

## Rules

- **R1** The entry to delete is identified by an integer id passed to the delete-by-id route. [1]

## Side effects

- **E1** The blocklist service asks the blocklist repository to delete the record with that id. [3][4]

## Couldn't see

- the depth limit left out 1 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Blocklist.BlocklistController.DeleteBlocklist(System.Int32)`
2. `endpoint:Sonarr.Api.V3|ANY /api/v3/Blocklist`
3. `Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Delete(System.Int32)`
4. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Delete(System.Int32)`

<sub>commit 0bffefe · inputs 53a4cfe · slice of 7 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
