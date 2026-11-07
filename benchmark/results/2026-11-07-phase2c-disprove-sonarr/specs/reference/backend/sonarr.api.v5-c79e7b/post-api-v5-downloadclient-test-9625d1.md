# Test download client (POST /api/v5/DownloadClient/test)

`endpoint:Sonarr.Api.V5|POST /api/v5/DownloadClient/test` · backend · Sonarr.Api.V5

From the code: POST /api/v5/DownloadClient/test

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success the endpoint returns an empty No Content response. [1]
- **S2** When validation fails, a validation exception is raised carrying the failures, with errors listed before warnings. [2][3]

## Actions

- **A1** A caller posts a JSON download client definition, with an optional skipValidation query value (default None), to test it without saving it. [1] → `endpoint:Sonarr.Api.V5|POST /api/v5/DownloadClient/test`

## Rules

- **R1** If the posted resource has an Id greater than 0, the existing saved definition is looked up and used. Otherwise no existing definition is used. [1][4]
- **R2** Settings are validated before the test unless skipValidation is All. Validation is forced for a test, even if the client is not enabled. [5][6]
- **R3** The posted resource is mapped to a definition: name, implementation, config contract, message, tags, and settings built from the posted fields. For a download client it also takes Enable, Protocol, Priority, RemoveCompletedDownloads and RemoveFailedDownloads. [7][8]
- **R4** A password or API key field sent with the private placeholder value keeps the value from the existing definition, when one exists. [9]
- **R5** With skipValidation None, a validation exception is thrown if the result has any errors or any warnings. [2]
- **R6** With skipValidation Warnings, a validation exception is thrown only for errors. Warnings are ignored. [2]

## Side effects

- **E1** The download client factory runs the client's own connection test. When the definition has a non-zero Id, it records the client's status as a success if the result is valid, or as a failure otherwise. [10][11]
- **E2** A definition with Id 0 (not yet saved) gets no status recorded. [10]

## Couldn't see

- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:10` reflection: Assembly.Load
- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:72` reflection: Assembly.GetExportedTypes
- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 159 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Test(`0,Sonarr.Api.V5.Provider.SkipValidation)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,Sonarr.Api.V5.Provider.SkipValidation)`
3. `Sonarr.Core|M:NzbDrone.Core.Validation.NzbDroneValidationResult.#ctor(System.Collections.Generic.IEnumerable{FluentValidation.Results.ValidationFailure})`
4. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProviderFactory`2.Find(System.Int32)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.GetDefinition(`0,`3,Sonarr.Api.V5.Provider.SkipValidation,System.Boolean)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Validate(`3,Sonarr.Api.V5.Provider.SkipValidation)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderResourceMapper`2.ToModel(`0,`1)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.DownloadClient.DownloadClientResourceMapper.ToModel(Sonarr.Api.V5.DownloadClient.DownloadClientResource,NzbDrone.Core.Download.DownloadClientDefinition)`
9. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
10. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadClientFactory.Test(NzbDrone.Core.Download.DownloadClientDefinition)`
11. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Test(`1)`

<sub>commit 0bffefe · inputs 4517e19 · slice of 59 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
