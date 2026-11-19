# Custom Format API (create, update, delete)

`endpoint:Sonarr.Api.V5|ANY /api/v5/CustomFormat` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/CustomFormat

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Create returns the newly created custom format (Created) or NotFound, with a location pointing at the get-by-id action. [1][2]
- **S2** Update returns the updated custom format as Accepted, or NotFound. [3][4]
- **S3** Delete returns No Content. [5]
- **S4** Get by id returns the resource as Ok, or NotFound when the model does not exist. [6]

## Actions

- **A1** Create a custom format from a JSON body; it is mapped to a model, its specifications validated, then inserted. [1] → `endpoint:Sonarr.Api.V5|ANY /api/v5/CustomFormat`
- **A2** Update a custom format from a JSON body; it is mapped, its specifications validated, then saved. [3] → `endpoint:Sonarr.Api.V5|ANY /api/v5/CustomFormat`
- **A3** Delete a custom format by id. [5] → `endpoint:Sonarr.Api.V5|ANY /api/v5/CustomFormat`
- **A4** Fetch a custom format by id. [6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/CustomFormat`

## Rules

- **R1** IncludeCustomFormatWhenRenaming defaults to false when not supplied, and specifications default to an empty list. [7]
- **R2** Each specification's Implementation must match the type name of a known specification, otherwise an error "<implementation> is not a valid specification implementation" is thrown. [8]
- **R3** Specification name, negate and required flags are copied from the request, and the specification's field values are read from its fields. [8][9]
- **R4** Every specification is validated before create or update; if the validation result is not valid, a validation exception carrying its errors is thrown. [10][11]
- **R5** On PUT, if the body resource Id is 0 and the route has an id, the route id is used. [12]
- **R6** Calls to endpoints marked deprecated are logged as a warning and the response gets a "Deprecation: true" header. [12]

## Side effects

- **E1** Create inserts the custom format, clears the cache and publishes a CustomFormatAddedEvent, which quality profile handling responds to. [13][14]
- **E2** Update saves the custom format and clears the cache. [15]
- **E3** Delete loads the format, publishes a CustomFormatDeletedEvent before removal (handled by quality profile service), deletes it and clears the cache. [16][17]

## Couldn't see

- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 47 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R4 (unsupported): Source shows errors throw a ValidationException (result.IsValid check), but whether warnings alone leave IsValid true is not shown in the slice (NzbDroneValidationResult constructor adds only errors to base.Errors, which supports it weakly). Mostly supported; the 'warnings do not' claim relies on IsValid semantics not visible. **fixed**: Removed the claim about warnings; the rule now states only that an invalid result throws a validation exception.

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatController.Create(Sonarr.Api.V5.CustomFormats.CustomFormatResource)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedCreated(System.Int32)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatController.Update(Sonarr.Api.V5.CustomFormats.CustomFormatResource)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatController.DeleteFormat(System.Int32)`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatResourceMapper.ToModel(Sonarr.Api.V5.CustomFormats.CustomFormatResource,System.Collections.Generic.List{NzbDrone.Core.CustomFormats.ICustomFormatSpecification})`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatResourceMapper.MapSpecification(Sonarr.Api.V5.CustomFormats.CustomFormatSpecificationSchema,System.Collections.Generic.List{NzbDrone.Core.CustomFormats.ICustomFormatSpecification})`
9. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
10. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatController.Validate(NzbDrone.Core.CustomFormats.CustomFormat)`
11. `Sonarr.Api.V5|M:Sonarr.Api.V5.CustomFormats.CustomFormatController.VerifyValidationResult(FluentValidation.Results.ValidationResult)`
12. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
13. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.CustomFormatService.Insert(NzbDrone.Core.CustomFormats.CustomFormat)`
14. [`Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Handle(NzbDrone.Core.CustomFormats.Events.CustomFormatAddedEvent)`](../../backend/sonarr.core-1b4c77/qualityprofileservice.handle-customformataddedevent-7b4321.md)
15. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.CustomFormatService.Update(NzbDrone.Core.CustomFormats.CustomFormat)`
16. `Sonarr.Core|M:NzbDrone.Core.CustomFormats.CustomFormatService.Delete(System.Int32)`
17. [`Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Handle(NzbDrone.Core.CustomFormats.Events.CustomFormatDeletedEvent)`](../../backend/sonarr.core-1b4c77/qualityprofileservice.handle-customformatdeletedevent-04169f.md)

<sub>commit 0bffefe · inputs 55f9323 · slice of 64 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
