# Tag API (v5)

`endpoint:Sonarr.Api.V5|ANY /api/v5/Tag` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Tag

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Create returns the created tag resource (looked up by its new id) with a Created result, or NotFound. [1][2]
- **S2** Update returns the updated tag resource with an Accepted result, or NotFound. [3][4]
- **S3** Delete returns No Content on success. [5]
- **S4** Get by id returns the resource as JSON with Ok, or NotFound when the model is not found. [6]

## Actions

- **A1** Create a tag from a JSON body with an id and label. [1][7] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Tag`
- **A2** Update an existing tag from a JSON body. [3][7] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Tag`
- **A3** Delete a tag by id. [5] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Tag`
- **A4** Get a tag by id. [6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/Tag`

## Rules

- **R1** Adding a tag whose label already exists returns the existing tag and inserts nothing. [8][9]
- **R2** Labels are converted to lower case when a tag is added or updated. [8][10]
- **R3** A tag that is still in use cannot be deleted; a conflict error says "'<label>' cannot be deleted since it's still in use". [11][12]
- **R4** Use is checked against delay profiles, import lists, notifications, release profiles, excluded release profiles, series, indexers, auto tags and download clients. [13]
- **R5** On POST and PUT, each resource in the body is validated; on PUT a resource id of 0 takes the id from the route. [14]
- **R6** Calls to endpoints marked deprecated are logged and get a "Deprecation: true" response header. [14]

## Side effects

- **E1** Adding a new tag inserts it into the repository and publishes a TagsUpdatedEvent. [8][15][16]
- **E2** Updating a tag saves it and publishes a TagsUpdatedEvent. [10][17]
- **E3** Deleting an unused tag removes it from the repository and publishes a TagsUpdatedEvent. [11][18]
- **E4** TagsUpdatedEvent is handled by the V3 and V5 tag controllers' Handle methods. [19][20]

## Couldn't see

- the depth limit left out 30 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagController.Create(Sonarr.Api.V5.Tags.TagResource)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedCreated(System.Int32)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagController.Update(Sonarr.Api.V5.Tags.TagResource)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagController.DeleteTag(System.Int32)`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagResourceMapper.ToModel(Sonarr.Api.V5.Tags.TagResource)`
8. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Add(NzbDrone.Core.Tags.Tag)`
9. `Sonarr.Core|M:NzbDrone.Core.Tags.ITagRepository.FindByLabel(System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Update(NzbDrone.Core.Tags.Tag)`
11. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Delete(System.Int32)`
12. `Sonarr.Core|M:NzbDrone.Core.Datastore.ModelConflictException.#ctor(System.Type,System.Int32,System.String)`
13. `Sonarr.Core|M:NzbDrone.Core.Tags.TagService.Details(System.Int32)`
14. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
15. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`
16. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
17. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Update(`0)`
18. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Delete(System.Int32)`
19. [`Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.Handle(NzbDrone.Core.Tags.TagsUpdatedEvent)`](../../backend/sonarr.api.v3-c45bd0/tagcontroller.handle-tagsupdatedevent-31b25f.md)
20. [`Sonarr.Api.V5|M:Sonarr.Api.V5.Tags.TagController.Handle(NzbDrone.Core.Tags.TagsUpdatedEvent)`](../../backend/sonarr.api.v5-c79e7b/tagcontroller.handle-tagsupdatedevent-78cd42.md)

<sub>commit 0bffefe · inputs 22ee8a3 · slice of 41 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
