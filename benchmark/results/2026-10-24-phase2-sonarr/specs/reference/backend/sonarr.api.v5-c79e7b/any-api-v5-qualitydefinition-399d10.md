# Update Quality Definition (API v5)

`endpoint:Sonarr.Api.V5|ANY /api/v5/QualityDefinition` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/QualityDefinition

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an Accepted response containing the updated quality definition resource, with a location URL pointing to the get-by-id action, or NotFound. [1][2]
- **S2** The get-by-id action returns the resource as Ok, or NotFound when the model is not found. [3]

## Actions

- **A1** Updates a quality definition from a JSON body: the resource is mapped to a model (Id, Quality, Title, Weight) and saved. [2][4]

## Rules

- **R1** Profile size limits are updated only when the model has a MinSize, MaxSize or PreferredSize value. [2]
- **R2** On PUT, if the body resource Id is 0 and the route has an id, the route id is used as the resource Id. [5]
- **R3** Resources in POST and PUT bodies are validated before the action runs, unless validation is skipped for the action. [5]
- **R4** Calls to endpoints marked deprecated are logged as a warning and get a Deprecation: true response header. [5]

## Side effects

- **E1** The quality definition is saved to the repository and the service cache is cleared. [6]
- **E2** When size values are present, every quality profile's matching quality item (or group member) gets its MinSize, MaxSize and PreferredSize overwritten, and all profiles are saved. [7][8]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Qualities.QualityDefinitionController.Update(Sonarr.Api.V5.Qualities.QualityDefinitionResource)`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Qualities.QualityDefinitionResourceMapper.ToModel(Sonarr.Api.V5.Qualities.QualityDefinitionResource)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
6. `Sonarr.Core|M:NzbDrone.Core.Qualities.QualityDefinitionService.Update(NzbDrone.Core.Qualities.QualityDefinition)`
7. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.UpdateAllSizeLimits(NzbDrone.Core.Profiles.Qualities.QualityProfileSizeLimit[])`
8. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileSizeLimit.#ctor(NzbDrone.Core.Qualities.QualityDefinition)`

<sub>commit 0bffefe · inputs ef7f34a · slice of 56 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
