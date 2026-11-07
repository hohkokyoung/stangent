# General settings (save and read) API

`endpoint:Sonarr.Api.V5|ANY /api/v5/settings/ui` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/settings/ui

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** A successful save returns 202 Accepted with the saved settings resource and a link to the GET-by-id action for it. [1][2]
- **S2** Reading a resource by id returns it as JSON with 200 OK, or 404 Not Found when no such resource exists. [3]
- **S3** Save returns NotFound as an alternative result. [2]

## Actions

- **A1** Save general settings with a JSON body; every public property of the resource is written to both the config file and the database config store. [2][4] → `endpoint:Sonarr.Api.V5|ANY /api/v5/settings/ui`
- **A2** Fetch a settings resource by id. [3] → `endpoint:Sonarr.Api.V5|ANY /api/v5/settings/ui`

## Rules

- **R1** Trusted networks are normalized: split on commas, trimmed, empty entries removed and rejoined with ", "; blank input becomes an empty string. [4][5]
- **R2** The user is created or updated only when both username and password are non-blank. If the password equals the private placeholder, null is passed as the password. [4][6]
- **R3** If the OIDC client secret, SSL certificate password or proxy password equals the private placeholder, the currently stored value is kept instead. [4]
- **R4** The ApiKey value is never changed by a save to the config file. [7]
- **R5** Only keys that already exist with a non-null current value and whose new value differs from the current one are written. Config-service entries with a null new value are skipped. [7][8]
- **R6** On PUT or POST, a body resource with Id 0 on a PUT takes its id from the route; resources are validated unless validation is skipped. [9]
- **R7** Calls to endpoints marked deprecated are logged as a warning and get a "Deprecation: true" response header. [9]

## Side effects

- **E1** Changed values are written to the XML config file with the first letter of the key upper-cased, and the config cache is cleared. [7][10][11]
- **E2** Changed values are upserted into the database under the lower-cased key, and the cache is cleared. [12][8]
- **E3** When any config-file value changed, a ConfigFileSavedEvent is published. Logging is reconfigured and the OIDC options change token source handles it. [7][13][14]
- **E4** When any database config value changed, a ConfigSavedEvent is published. Pending releases, the task manager, localization and UI authorization handle it. [8][15][16][17][18]
- **E5** Saving with a username and password creates or updates the stored user. [19][4]

## Couldn't see

- the depth limit left out 18 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Settings.SettingsController`1.SaveSettings(`0)`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Settings.GeneralSettingsController.SaveSettings(Sonarr.Api.V5.Settings.GeneralSettingsResource)`
5. `Sonarr.Common|M:NzbDrone.Common.Network.IPNetworkParser.NormalizeList(System.String)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
7. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.SaveConfigDictionary(System.Collections.Generic.Dictionary{System.String,System.Object})`
8. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigService.SaveConfigDictionary(System.Collections.Generic.Dictionary{System.String,System.Object})`
9. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
10. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.SetValue(System.String,System.Object)`
11. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.FirstCharToUpper(System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigService.SetValue(System.String,System.String)`
13. [`Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.core-1b4c77/reconfigurelogging.handleasync-configfilesavedevent-31540a.md)
14. [`Sonarr.Http|M:Sonarr.Http.Authentication.OidcOptionsChangeTokenSource.Handle(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.http-01d2b2/oidcoptionschangetokensource.handle-configfilesavedevent-705974.md)
15. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-configsavedevent-79f05d.md)
16. [`Sonarr.Core|M:NzbDrone.Core.Jobs.TaskManager.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.core-1b4c77/taskmanager.handleasync-configsavedevent-7de6bb.md)
17. [`Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.core-1b4c77/localizationservice.handleasync-configsavedevent-9490a5.md)
18. [`Sonarr.Http|M:NzbDrone.Http.Authentication.UiAuthorizationHandler.Handle(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.http-01d2b2/uiauthorizationhandler.handle-configsavedevent-b4d043.md)
19. `Sonarr.Core|M:NzbDrone.Core.Authentication.IUserService.Upsert(System.String,System.String)`

<sub>commit 0bffefe · inputs d36d17a · slice of 56 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
