# Tag API (/api/v3/Tag)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Tag` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Tag

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Getting a tag by id returns the tag as JSON, or a not-found result when the tag does not exist. [1]
- **S2** Creating a tag responds with the created tag, located via the get-by-id action. [2][3]
- **S3** Updating a tag responds with an Accepted result containing the tag. [4][5]

## Actions

- **A1** Create a tag by posting a JSON tag with an id and label; the tag is added through the tag service. [3][6] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Tag`
- **A2** Update a tag by putting a JSON tag. [5] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Tag`
- **A3** Delete a tag by id. [7] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Tag`
- **A4** Fetch a tag by id. [1] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Tag`

## Rules

- **R1** Adding a tag whose label already exists returns the existing tag and does not insert a new one. [8]
- **R2** Tag labels are converted to lower case when a tag is added or updated. [8][9]
- **R3** A tag that is still in use cannot be deleted; a conflict error says "'<label>' cannot be deleted since it's still in use". [10][11]
- **R4** Use is checked against delay profiles, import lists, notifications, release profiles, excluded release profiles, series, indexers, auto tags and download clients. [12]
- **R5** Create and update accept only application/json bodies. [3][5]
- **R6** On PUT, if the body id is 0 and the route has an id, the route id is used. Resources are validated on POST and PUT. [13]
- **R7** Calls to deprecated endpoints are logged as a warning and get a "Deprecation: true" response header. [13]

## Side effects

- **E1** Adding a new tag inserts it in the repository and publishes a TagsUpdatedEvent. [8][14][15]
- **E2** Updating a tag saves it in the repository and publishes a TagsUpdatedEvent. [9][16]
- **E3** Deleting an unused tag removes it from the repository and publishes a TagsUpdatedEvent. [10][17]
- **E4** TagsUpdatedEvent is handled by the V3 and V5 tag controllers. [18][19]

## Couldn't see

- the depth limit left out 30 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.Create(Sonarr.Api.V3.Tags.TagResource)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.Update(Sonarr.Api.V3.Tags.TagResource)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagResourceMapper.ToModel(Sonarr.Api.V3.Tags.TagResource)`
7. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.DeleteTag(System.Int32)`
8. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Add(NzbDrone.Core.Tags.Tag)`
9. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Update(NzbDrone.Core.Tags.Tag)`
10. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Delete(System.Int32)`
11. `Sonarr.Core|M:NzbDrone.Core.Datastore.ModelConflictException.#ctor(System.Type,System.Int32,System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Details(System.Int32)`
13. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
14. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`
15. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
16. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Update(`0)`
17. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Delete(System.Int32)`
18. [`Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.Handle(NzbDrone.Core.Tags.TagsUpdatedEvent)`](../../backend/sonarr.api.v3-c45bd0/tagcontroller.handle-tagsupdatedevent-31b25f.md)
19. [`Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagController.Handle(NzbDrone.Core.Tags.TagsUpdatedEvent)`](../../backend/sonarr.api.v5-c79e7b/tagcontroller.handle-tagsupdatedevent-78cd42.md)

<sub>commit 0bffefe · inputs a245fa6 · slice of 41 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
