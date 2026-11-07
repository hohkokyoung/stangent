# Custom Format API (create, update, delete)

`endpoint:Sonarr.Api.V3|ANY /api/v3/CustomFormat` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/CustomFormat

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Creating a custom format returns the saved custom format resource with a created response pointing to the get-by-id action. [1][2]
- **S2** Updating a custom format returns the stored resource with an accepted response pointing to the get-by-id action. [3][4]
- **S3** Getting a custom format by id returns it with 200 OK, or 404 Not Found when no such record exists. [5]

## Actions

- **A1** Create a custom format by posting a JSON resource; it is converted to a model, its specifications are validated, and it is inserted. [1] → `endpoint:Sonarr.Api.V3|ANY /api/v3/CustomFormat`
- **A2** Update a custom format by putting a JSON resource; it is converted, its specifications are validated, and it is saved. [3] → `endpoint:Sonarr.Api.V3|ANY /api/v3/CustomFormat`
- **A3** Delete a custom format by id. [6] → `endpoint:Sonarr.Api.V3|ANY /api/v3/CustomFormat`

## Rules

- **R1** Each specification's implementation name must match exactly one known specification type; otherwise the request fails with "<implementation> is not a valid specification implementation". [7]
- **R2** Each specification takes its name, negate and required values from the request, and its other field values are read from the submitted fields. [7][8]
- **R3** Include-when-renaming defaults to false when not supplied, and specifications default to an empty list. [9]
- **R4** Every specification is validated before create or update; if any validation error exists, a validation exception with those errors is thrown and nothing is saved. [10][11]
- **R5** Validation failures flagged as warnings are kept separate from errors, and only errors make the result invalid. [12]
- **R6** On PUT, if the body resource id is 0 and the route has an id, the route id is used. [13]
- **R7** Calls to a deprecated endpoint are logged as a warning and the response gets a "Deprecation: true" header. [13]

## Side effects

- **E1** Creating a custom format inserts it in the repository, clears the custom format cache, and publishes a CustomFormatAddedEvent. [14][15]
- **E2** Updating a custom format saves it in the repository and clears the custom format cache. [16]
- **E3** Deleting a custom format loads it, publishes a CustomFormatDeletedEvent before removal, deletes it from the repository and clears the cache. [17][18]
- **E4** The quality profile service handles the added event, so new formats are added to profiles. [19]
- **E5** The quality profile service handles the deleted event, so the format is removed from profiles before deletion. [20]

## Couldn't see

- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 47 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatController.Create(Sonarr.Api.V3.CustomFormats.CustomFormatResource)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatController.Update(Sonarr.Api.V3.CustomFormats.CustomFormatResource)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatController.DeleteFormat(System.Int32)`
7. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatResourceMapper.MapSpecification(Sonarr.Api.V3.CustomFormats.CustomFormatSpecificationSchema,System.Collections.Generic.List{NzbDrone.Core.CustomFormats.ICustomFormatSpecification})`
8. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
9. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatResourceMapper.ToModel(Sonarr.Api.V3.CustomFormats.CustomFormatResource,System.Collections.Generic.List{NzbDrone.Core.CustomFormats.ICustomFormatSpecification})`
10. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatController.Validate(NzbDrone.Core.CustomFormats.CustomFormat)`
11. `Sonarr.Api.V3|M:Sonarr.Api.V3.CustomFormats.CustomFormatController.VerifyValidationResult(FluentValidation.Results.ValidationResult)`
12. `Sonarr.Core|M:NzbDrone.Core.Validation.NzbDroneValidationResult.#ctor(System.Collections.Generic.IEnumerable{FluentValidation.Results.ValidationFailure})`
13. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
14. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.CustomFormatService.Insert(NzbDrone.Core.CustomFormats.CustomFormat)`
15. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.Events.CustomFormatAddedEvent.#ctor(NzbDrone.Core.CustomFormats.CustomFormat)`
16. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.CustomFormatService.Update(NzbDrone.Core.CustomFormats.CustomFormat)`
17. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.CustomFormatService.Delete(System.Int32)`
18. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.Events.CustomFormatDeletedEvent.#ctor(NzbDrone.Core.CustomFormats.CustomFormat)`
19. [`Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Handle(NzbDrone.Core.CustomFormats.Events.CustomFormatAddedEvent)`](../../backend/sonarr.core-1b4c77/qualityprofileservice.handle-customformataddedevent-7b4321.md)
20. [`Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Handle(NzbDrone.Core.CustomFormats.Events.CustomFormatDeletedEvent)`](../../backend/sonarr.core-1b4c77/qualityprofileservice.handle-customformatdeletedevent-04169f.md)

<sub>commit 0bffefe · inputs a848597 · slice of 64 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
