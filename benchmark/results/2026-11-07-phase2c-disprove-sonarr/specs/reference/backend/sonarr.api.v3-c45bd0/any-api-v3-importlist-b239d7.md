# Import List API (/api/v3/ImportList)

`endpoint:Sonarr.Api.V3|ANY /api/v3/ImportList` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/ImportList

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Fetching a single import list by id returns the resource, or Not Found when the model is not found. [1]
- **S2** Creating returns a Created response containing the newly saved provider resource, located via the get-by-id action. [2][3]
- **S3** Updating returns an Accepted response containing the resource, located via the get-by-id action. [4][5]
- **S4** Deleting returns an empty object. [6]
- **S5** Import list fields mapped from the request include automatic add, search for missing episodes, monitor, monitor new items, root folder path, quality profile, series type, language, season folder, list type, minimum refresh interval and tag existing. [7]

## Actions

- **A1** Create an import list from a JSON body; an optional forceSave query flag skips warning checks and the connection test failure on warnings. [3]
- **A2** Update an import list by id with a JSON body; returns Not Found if no list exists for the route id or the body id. [5]
- **A3** Delete an import list by id. [6]
- **A4** Get an import list by id. [1]

## Rules

- **R1** On create, settings are validated when the definition is enabled; if enabled, a connection test is also run, with warnings included unless forceSave is set. [3][8]
- **R2** On update, the test runs only if the definition is enabled, forceSave is not set, and the definition or its settings changed. [5]
- **R3** On update, the stored record is only saved when the definition or its settings changed. [5]
- **R4** Validation fails with a validation exception on any error; warnings also cause failure when warnings are included. [9]
- **R5** On PUT, if the body id is 0 the route id is used. [10]
- **R6** When a password or API key field is sent with the masked placeholder value, the existing stored value is kept. [11]
- **R7** If no language is provided, the import list language defaults to English. [7]

## Side effects

- **E1** Creating inserts the definition and publishes a provider-added event. [12]
- **E2** Updating saves the definition and publishes a provider-updated event. [13]
- **E3** Deleting removes the record and publishes a provider-deleted event. [14]
- **E4** Testing an existing import list (id not 0) records a success or failure in the import list status service. [15]
- **E5** On update, an import list handler reacts to the provider-updated event. [16]
- **E6** On delete, the import list sync service and import list item service handle the provider-deleted event. [17][18]

## Couldn't see

- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:10` reflection: Assembly.Load
- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:72` reflection: Assembly.GetExportedTypes
- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 150 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.CreateProvider(`0,System.Boolean)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.UpdateProvider(System.Int32,`0,System.Boolean)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.DeleteProvider(System.Int32)`
7. `Sonarr.Api.V3|M:Sonarr.Api.V3.ImportLists.ImportListResourceMapper.ToModel(Sonarr.Api.V3.ImportLists.ImportListResource,NzbDrone.Core.ImportLists.ImportListDefinition)`
8. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.GetDefinition(`0,`3,System.Boolean,System.Boolean,System.Boolean)`
9. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,System.Boolean)`
10. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
11. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
12. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Create(`1)`
13. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Update(`1)`
14. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Delete(System.Int32)`
15. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListFactory.Test(NzbDrone.Core.ImportLists.ImportListDefinition)`
16. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListUpdatedHandler.Handle(NzbDrone.Core.ThingiProvider.Events.ProviderUpdatedEvent{NzbDrone.Core.ImportLists.IImportList})`](../../backend/sonarr.core-1b4c77/importlistupdatedhandler.handle-iimportlist-be92b9.md)
17. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{NzbDrone.Core.ImportLists.IImportList})`](../../backend/sonarr.core-1b4c77/importlistsyncservice.handleasync-iimportlist-5f86d3.md)
18. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListItems.ImportListItemService.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{NzbDrone.Core.ImportLists.IImportList})`](../../backend/sonarr.core-1b4c77/importlistitemservice.handleasync-iimportlist-a27a75.md)

<sub>commit 0bffefe · inputs 4bea87c · slice of 112 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
