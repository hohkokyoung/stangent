# Update Quality Definition (API)

`endpoint:Sonarr.Api.V3|ANY /api/v3/QualityDefinition` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/QualityDefinition

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success the endpoint returns an Accepted response with a location pointing to the get-by-id action; the body is the resource fetched by the definition's id. [1][2]
- **S2** Getting a quality definition by id returns it as JSON, or Not Found when no such record exists. [3]
- **S3** A quality definition has an Id, Quality, Title, Weight, MinSize, MaxSize and PreferredSize. [4]

## Actions

- **A1** A caller updates a quality definition by sending it in the request body; it is saved and the response is Accepted. [2] → `endpoint:Sonarr.Api.V3|ANY /api/v3/QualityDefinition`

## Rules

- **R1** Size limits are pushed to quality profiles only when the definition has a MinSize, MaxSize or PreferredSize value. [2]
- **R2** For POST or PUT requests, each resource in the arguments is validated. For PUT, a resource Id of 0 is filled from the route id when one is present. [5]
- **R3** The id argument is validated on actions marked for id validation, unless validation is skipped. [5]

## Side effects

- **E1** The quality definition is saved to the repository and the service's cache is cleared. [6][7][8]
- **E2** When a size is set, every quality profile has the matching quality item (or the item inside a group) updated with the new MinSize, MaxSize and PreferredSize, and all profiles are saved. [9][10][11]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on S1 (unsupported): Accepted calls GetResourceById(id) and returns AcceptedAtAction. The body is the resource fetched by id, which the page calls the saved definition. That GetResourceById code is not in the slice, so this is only partly supported. **fixed**: Reworded to state only what Accepted shows: location to get-by-id action and body fetched by id.
- F2 on R4 (unsupported): The code logs a warning and sets the header only when the controller or action has the deprecated attribute. This quality definition endpoint is not shown to be deprecated, so the rule is correct only as generic behavior. It still fits the code, so this finding is low-confidence. **fixed**: Removed the deprecation rule since this endpoint is not shown to be deprecated.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Qualities.QualityDefinitionController.Update(Sonarr.Api.V3.Qualities.QualityDefinitionResource)`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Qualities.QualityDefinitionResourceMapper.ToModel(Sonarr.Api.V3.Qualities.QualityDefinitionResource)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
6. `Sonarr.Core|M:NzbDrone.Core.Qualities.QualityDefinitionService.Update(NzbDrone.Core.Qualities.QualityDefinition)`
7. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Update(`0)`
8. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached.Clear`
9. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.UpdateAllSizeLimits(NzbDrone.Core.Profiles.Qualities.QualityProfileSizeLimit[])`
10. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileSizeLimit.#ctor(NzbDrone.Core.Qualities.QualityDefinition)`
11. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.UpdateMany(System.Collections.Generic.IList{`0})`

<sub>commit 0bffefe · inputs c275f11 · slice of 59 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
