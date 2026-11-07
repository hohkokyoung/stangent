# Metadata provider API (v5)

`endpoint:Sonarr.Api.V5|ANY /api/v5/Metadata` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Metadata

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Create returns the newly created metadata resource with a Created result, or NotFound if it cannot be loaded. [1][2]
- **S2** Update returns the updated resource with an Accepted result, or NotFound when no definition exists for the id. [3][4]
- **S3** Get by id returns the resource as Ok, or NotFound when the model is not found. [5]
- **S4** Delete returns NoContent. [6]
- **S5** Validation failures are returned as a validation exception containing the failures, including warnings unless warnings are skipped. [7]

## Actions

- **A1** Create a metadata provider from a JSON body; optional query flags skipTesting and skipValidation. [1]
- **A2** Update a metadata provider by id from a JSON body; optional query flags skipTesting and skipValidation. [3]
- **A3** Delete a metadata provider by id. [6]
- **A4** Fetch a metadata provider by id. [5]

## Rules

- **R1** On create, the definition is tested only when it is enabled and skipTesting is false. skipTesting defaults to false and skipValidation defaults to None. [1]
- **R2** On update, the definition is tested only if it is enabled, skipTesting is false and the definition or its settings changed. It is saved only if it changed. [3]
- **R3** Settings are validated when skipValidation is not All and the definition is enabled. [8][9]
- **R4** With skipValidation None, any error or warning fails; with Warnings, only errors fail. [7]
- **R5** Masked password or API key field values keep the existing stored value. [10]
- **R6** For PUT, a body id of 0 is filled from the route id. [11]
- **R7** A metadata resource's Enable flag is mapped to its definition. [12]

## Side effects

- **E1** Create inserts the definition and publishes a ProviderAddedEvent. [13]
- **E2** Update saves the definition and publishes a ProviderUpdatedEvent. [14]
- **E3** Delete removes the definition and publishes a ProviderDeletedEvent. [15]

## Couldn't see

- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:10` reflection: Assembly.Load
- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:72` reflection: Assembly.GetExportedTypes
- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 154 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.CreateProvider(`0,System.Boolean,Sonarr.Api.V5.Provider.SkipValidation)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedCreated(System.Int32)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.UpdateProvider(System.Int32,`0,System.Boolean,Sonarr.Api.V5.Provider.SkipValidation)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.DeleteProvider(System.Int32)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,Sonarr.Api.V5.Provider.SkipValidation)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.GetDefinition(`0,`3,Sonarr.Api.V5.Provider.SkipValidation,System.Boolean)`
9. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Validate(`3,Sonarr.Api.V5.Provider.SkipValidation)`
10. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
11. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
12. `Sonarr.Api.V5|M:Sonarr.Api.V5.Metadata.MetadataResourceMapper.ToModel(Sonarr.Api.V5.Metadata.MetadataResource,NzbDrone.Core.Extras.Metadata.MetadataDefinition)`
13. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Create(`1)`
14. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Update(`1)`
15. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Delete(System.Int32)`

<sub>commit 0bffefe · inputs ee9e498 · slice of 109 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
