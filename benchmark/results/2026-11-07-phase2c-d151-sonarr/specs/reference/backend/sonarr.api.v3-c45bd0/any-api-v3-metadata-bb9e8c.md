# Metadata provider API (/api/v3/Metadata)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Metadata` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Metadata

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Create returns the saved metadata provider resource with a Created response pointing at the get-by-id action. [1][2]
- **S2** Update returns the stored resource with an Accepted response, or NotFound if no provider exists for the id. [3][4]
- **S3** Get by id returns the resource as JSON, or NotFound when the model is not found. [5]
- **S4** Delete returns an empty object. [6]

## Actions

- **A1** Create a provider from a JSON body; optional forceSave query flag (default false). [2]
- **A2** Update a provider by id from a JSON body; optional forceSave flag (default false). Falls back to the id in the body if none is found by route id. [3]
- **A3** Delete a provider by id. [6][7]
- **A4** Fetch a provider by id. [5]

## Rules

- **R1** On create, settings are validated only when the definition is enabled; warnings count as failures unless forceSave is set. [8][2]
- **R2** On create, an enabled definition is tested before saving, with warnings included unless forceSave is set. [2][9]
- **R3** On update, the definition is tested only if it is enabled, forceSave is not set and the definition or its settings changed. [3]
- **R4** On update, the provider is saved only when the definition or settings changed. [3]
- **R5** A validation failure throws a validation exception: with warnings included it contains errors and warnings, otherwise only errors; with warnings not included, warnings alone pass. [10][11]
- **R6** On PUT, a body resource with Id 0 takes the id from the route. [12]
- **R7** Password and API key fields sent with the private placeholder value keep the existing stored value. [13]
- **R8** A metadata resource maps its Enable flag onto the definition, along with name, implementation, config contract, message, tags and settings fields. [14][15]

## Side effects

- **E1** Creating a provider inserts it in the repository and publishes a ProviderAddedEvent. [16]
- **E2** Updating a provider saves it and publishes a ProviderUpdatedEvent. [17]
- **E3** Deleting a provider removes it from the repository and publishes a ProviderDeletedEvent. [7]

## Couldn't see

- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:10` reflection: Assembly.Load
- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:72` reflection: Assembly.GetExportedTypes
- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 150 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.CreateProvider(`0,System.Boolean)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.UpdateProvider(System.Int32,`0,System.Boolean)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.DeleteProvider(System.Int32)`
7. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Delete(System.Int32)`
8. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.GetDefinition(`0,`3,System.Boolean,System.Boolean,System.Boolean)`
9. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.Test(`3,System.Boolean)`
10. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,System.Boolean)`
11. `Sonarr.Core|M:NzbDrone.Core.Validation.NzbDroneValidationResult.#ctor(System.Collections.Generic.IEnumerable{FluentValidation.Results.ValidationFailure})`
12. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
13. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
14. `Sonarr.Api.V3|M:Sonarr.Api.V3.Metadata.MetadataResourceMapper.ToModel(Sonarr.Api.V3.Metadata.MetadataResource,NzbDrone.Core.Extras.Metadata.MetadataDefinition)`
15. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderResourceMapper`2.ToModel(`0,`1)`
16. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Create(`1)`
17. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Update(`1)`

<sub>commit 0bffefe · inputs edffdcd · slice of 112 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
