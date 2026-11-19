# Test Connection (notification) endpoint

`endpoint:Sonarr.Api.V5|POST /api/v5/Connection/test` · backend · Sonarr.Api.V5

From the code: POST /api/v5/Connection/test

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an empty 204 No Content response when the test passes. [1]
- **S2** When the test fails, a validation exception is raised carrying the validation failures instead of a normal response. [2]

## Actions

- **A1** POST a connection definition as JSON to test it. An optional skipValidation query value (default None) controls how strictly failures are enforced. [1] → `endpoint:Sonarr.Api.V5|POST /api/v5/Connection/test`
- **A2** If the posted resource has an Id above 0, the existing saved definition is looked up and used, e.g. to restore stored private field values. [1][3]

## Rules

- **R1** The posted resource is converted to a definition, with settings type found by the config contract name (case-insensitive) and fields read into it. [4][5]
- **R2** Settings validation runs before the test unless skipValidation is All; the test always forces it, even for a disabled definition. [6][7]
- **R3** With skipValidation None, the call fails if the result has any errors or warnings. [2]
- **R4** With skipValidation Warnings, only errors cause failure; warnings are ignored. [2]
- **R5** Private field values (API key or password) sent as the masked placeholder are replaced with the existing stored value when an existing definition is present. [3]
- **R6** A connection definition also carries its on-event triggers and supports-event flags (e.g. OnGrab, OnDownload, OnHealthIssue) from the posted resource. [8]

## Side effects

- **E1** The provider's own connection test is executed for the definition, which contacts the configured provider. [9][10]
- **E2** For a saved notification (Id not 0), a passing test records a success in the notification status service and a failing test records a failure. Unsaved definitions record nothing. [11]

## Couldn't see

- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:10` reflection: Assembly.Load
- ❓ `src/NzbDrone.Common/Reflection/ReflectionExtensions.cs:72` reflection: Assembly.GetExportedTypes
- ❓ `src/Sonarr.Http/ClientSchema/SchemaBuilder.cs:58` reflection: Activator.CreateInstance
- the depth limit left out 159 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Test(`0,Sonarr.Api.V5.Provider.SkipValidation)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.VerifyValidationResult(FluentValidation.Results.ValidationResult,Sonarr.Api.V5.Provider.SkipValidation)`
3. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ReadFromSchema(System.Collections.Generic.List{Sonarr.Http.ClientSchema.Field},System.Type,System.Object)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderResourceMapper`2.ToModel(`0,`1)`
5. `Sonarr.Common|M:NzbDrone.Common.Reflection.ReflectionExtensions.FindTypeByName(System.Reflection.Assembly,System.String)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.GetDefinition(`0,`3,Sonarr.Api.V5.Provider.SkipValidation,System.Boolean)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Validate(`3,Sonarr.Api.V5.Provider.SkipValidation)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.Connections.ConnectionResourceMapper.ToModel(Sonarr.Api.V5.Connections.ConnectionResource,NzbDrone.Core.Notifications.NotificationDefinition)`
9. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Test(`1)`
10. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Test(`3,Sonarr.Api.V5.Provider.SkipValidation)`
11. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.Test(NzbDrone.Core.Notifications.NotificationDefinition)`

<sub>commit 0bffefe · inputs bb52434 · slice of 59 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
