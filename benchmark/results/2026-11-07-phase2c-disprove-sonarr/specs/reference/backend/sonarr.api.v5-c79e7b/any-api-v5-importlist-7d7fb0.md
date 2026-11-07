# Import List provider API (create, update, delete)

`endpoint:Sonarr.Api.V5|ANY /api/v5/ImportList` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/ImportList

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Creating a provider returns a Created result with the saved resource and a link to fetch it by id. [1][2]
- **S2** Updating a provider returns an Accepted result with the resource and a link to fetch it by id, or NotFound when the id does not exist. [3][4]
- **S3** Deleting a provider returns No Content. [5]
- **S4** Getting a resource by id returns Ok with the resource, or NotFound when the model is not found. [6]
- **S5** An import list resource carries these settings: automatic add, search for missing episodes, should monitor, monitor new items, root folder path, quality profile, series type, language, season folder, list type, minimum refresh interval and tag existing. [7]

## Actions

- **A1** Create an import list: the body is mapped to a definition, validated, tested if enabled, then saved. [1] → `endpoint:Sonarr.Api.V5|ANY /api/v5/ImportList`
- **A2** Update an import list by id: looks up the existing definition, maps the body onto it, and saves it only if something changed. [3] → `endpoint:Sonarr.Api.V5|ANY /api/v5/ImportList`
- **A3** Delete an import list by id. [5] → `endpoint:Sonarr.Api.V5|ANY /api/v5/ImportList`
- **A4** Fetch a single import list by id. [6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/ImportList`

## Rules

- **R1** Create and update accept the query flags skipTesting (default false) and skipValidation (default None). [1][3]
- **R2** On create, the connection test runs only if the definition is enabled and skipTesting is not set. [1]
- **R3** On update, the test runs only if the definition is enabled, skipTesting is not set, and the definition or its settings changed. [3]
- **R4** On update, the definition is saved only when it or its settings changed; otherwise nothing is saved. [3]
- **R5** Settings are validated when mapping, unless skipValidation is All, and only if the definition is enabled or validation is forced. [8][9]
- **R6** With skipValidation None, a validation exception is thrown for any error or warning. [10]
- **R7** With skipValidation Warnings, a validation exception is thrown only for errors, not warnings. [10]
- **R8** If the import list language is not given, it defaults to English. [7]
- **R9** A masked API key or password field value keeps the existing stored value when the definition is updated. [11]
- **R10** On a PUT with resource id 0, the id from the route is copied into the body resource. [12]
- **R11** Calls to endpoints marked deprecated get a Deprecation: true response header and a logged warning. [12]

## Side effects

- **E1** Creating inserts the definition into the repository and publishes a ProviderAddedEvent. [13]
- **E2** Updating saves the definition and publishes a ProviderUpdatedEvent, which the import list updated handler receives. [14][15]
- **E3** Deleting removes the definition from the repository and publishes a ProviderDeletedEvent, which the import list item service, import list sync service and provider status service handle. [16][17][18][19]
- **E4** Testing a saved import list (id not 0) records a success or failure in the import list status service. [20]

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
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.DeleteProvider(System.Int32)`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.ImportLists.ImportListResourceMapper.ToModel(Sonarr.Api.V5.ImportLists.ImportListResource,NzbDrone.Core.ImportLists.ImportListDefinition)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.GetDefinition(`0,`3,Sonarr.Api.V5.Provider.SkipValidation,System.Boolean)`
9. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Validate(`3,Sonarr.Api.V5.Provider.SkipValidation)`
10. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,Sonarr.Api.V5.Provider.SkipValidation)`
11. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
12. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
13. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Create(`1)`
14. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Update(`1)`
15. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListUpdatedHandler.Handle(NzbDrone.Core.ThingiProvider.Events.ProviderUpdatedEvent{NzbDrone.Core.ImportLists.IImportList})`](../../backend/sonarr.core-1b4c77/importlistupdatedhandler.handle-iimportlist-be92b9.md)
16. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Delete(System.Int32)`
17. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListItems.ImportListItemService.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{NzbDrone.Core.ImportLists.IImportList})`](../../backend/sonarr.core-1b4c77/importlistitemservice.handleasync-iimportlist-a27a75.md)
18. [`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{NzbDrone.Core.ImportLists.IImportList})`](../../backend/sonarr.core-1b4c77/importlistsyncservice.handleasync-iimportlist-5f86d3.md)
19. [`Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.ProviderStatusServiceBase`2.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{`0})`](../../backend/sonarr.core-1b4c77/providerstatusservicebase.handleasync-providerdeletedevent-0-17699f.md)
20. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListFactory.Test(NzbDrone.Core.ImportLists.ImportListDefinition)`

<sub>commit 0bffefe · inputs bfb3f3f · slice of 109 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
