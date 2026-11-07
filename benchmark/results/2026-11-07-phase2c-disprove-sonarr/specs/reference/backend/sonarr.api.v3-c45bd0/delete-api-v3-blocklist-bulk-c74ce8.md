# Bulk delete blocklist entries

`endpoint:Sonarr.Api.V3|DELETE /api/v3/Blocklist/bulk` · backend · Sonarr.Api.V3

From the code: DELETE /api/v3/Blocklist/bulk

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an empty JSON object once the deletion call completes. [1]

## Actions

- **A1** A caller sends DELETE /api/v3/Blocklist/bulk with a JSON body containing a list of ids, and those blocklist entries are removed. [2][1] → `endpoint:Sonarr.Api.V3|DELETE /api/v3/Blocklist/bulk`

## Rules

- **R1** The ids to delete are taken from the Ids list in the JSON request body. [1]

## Side effects

- **E1** The blocklist service deletes all the given ids from the blocklist repository in one DeleteMany call. [3][4]

## Couldn't see

- the depth limit left out 1 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R1 (unsupported): The source shows ids read from resource.Ids, but nothing shows the request body is required beyond [FromBody]; the required claim is a framework behavior not shown in the slice. **fixed**: Removed the claim that the request body is required; the rule now only states that ids come from the body's Ids list.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Blocklist.BlocklistController.Remove(Sonarr.Api.V3.Blocklist.BlocklistBulkResource)`
2. `endpoint:Sonarr.Api.V3|DELETE /api/v3/Blocklist/bulk`
3. `Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Delete(System.Collections.Generic.List{System.Int32})`
4. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.DeleteMany(System.Collections.Generic.IEnumerable{System.Int32})`

<sub>commit 0bffefe · inputs dcc7054 · slice of 8 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
