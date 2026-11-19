# Test download client settings

`endpoint:Sonarr.Api.V3|POST /api/v3/DownloadClient/test` · backend · Sonarr.Api.V3

From the code: POST /api/v3/DownloadClient/test

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success the endpoint returns the text "{}". [1]
- **S2** On failure it raises a validation exception listing the failures. With warnings included, the list holds errors followed by warnings. Otherwise it holds errors only. [2][3]

## Actions

- **A1** A caller posts a download client definition as JSON, with an optional forceTest query flag (default false), to test it without saving it. [1] → `endpoint:Sonarr.Api.V3|POST /api/v3/DownloadClient/test`
- **A2** When the posted id is above 0, the existing saved definition is looked up. Its stored settings are used to fill in masked API key and password values. [1][4][5]

## Rules

- **R1** The posted resource is converted to a definition. The settings type is found by case-insensitive name match on the config contract, and the settings values are read from the posted fields. [6][7]
- **R2** The download client definition also takes Enable, Protocol, Priority, RemoveCompletedDownloads and RemoveFailedDownloads from the posted resource. [8]
- **R3** The settings are always validated before the connection test, even if the client is not enabled. Warnings count as failures unless forceTest is true. [1][9][10]
- **R4** The connection test always treats warnings as failures. Any error always fails the request. [1][11][2]

## Side effects

- **E1** For a saved download client (id not 0), a passing test records a success and a failing test records a failure in the download client status service. An unsaved definition (id 0) records nothing. [12]
- **E2** The test runs the download client's own test against the configured client. [13]

## Couldn't see

- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:10` reflection: Assembly.Load
- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:72` reflection: Assembly.GetExportedTypes
- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 159 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.Test(`0,System.Boolean)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,System.Boolean)`
3. `Sonarr.Core|M:NzbDrone.Core.Validation.NzbDroneValidationResult.#ctor(System.Collections.Generic.IEnumerable{FluentValidation.Results.ValidationFailure})`
4. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProviderFactory`2.Find(System.Int32)`
5. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderResourceMapper`2.ToModel(`0,`1)`
7. `Sonarr.Common|M:NzbDrone.Common.Reflection.ReflectionExtensions.FindTypeByName(System.Reflection.Assembly,System.String)`
8. `Sonarr.Api.V3|M:Sonarr.Api.V3.DownloadClient.DownloadClientResourceMapper.ToModel(Sonarr.Api.V3.DownloadClient.DownloadClientResource,NzbDrone.Core.Download.DownloadClientDefinition)`
9. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.GetDefinition(`0,`3,System.Boolean,System.Boolean,System.Boolean)`
10. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.Validate(`3,System.Boolean)`
11. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.Test(`3,System.Boolean)`
12. `Sonarr.Core|M:NzbDrone.Core.Download.DownloadClientFactory.Test(NzbDrone.Core.Download.DownloadClientDefinition)`
13. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Test(`1)`

<sub>commit 0bffefe · inputs bcb17e4 · slice of 58 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
