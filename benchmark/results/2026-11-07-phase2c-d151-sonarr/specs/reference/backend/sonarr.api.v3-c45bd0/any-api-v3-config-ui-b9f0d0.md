# UI configuration API (/api/v3/config/ui)

`endpoint:Sonarr.Api.V3|ANY /api/v3/config/ui` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/config/ui

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** A save returns the saved UI config resource, looked up by its id, with an Accepted result that points to the get-by-id action. [1][2]
- **S2** Getting a resource by id returns it as JSON with 200 OK, or 404 Not Found when the resource isn't found. [3]

## Actions

- **A1** A caller saves the UI configuration by sending a resource in the request body. The resource's public properties are offered to the config file settings and the database config settings, which save only the values that qualify. [2][4][5] → `endpoint:Sonarr.Api.V3|ANY /api/v3/config/ui`
- **A2** A caller fetches a resource by id and gets 200 OK or 404 Not Found. [3] → `endpoint:Sonarr.Api.V3|ANY /api/v3/config/ui`

## Rules

- **R1** On POST or PUT, a PUT resource with id 0 takes its id from the route id when the route has one. The resource is then validated. [6]
- **R2** When the action requires an id and validation isn't skipped, the id argument is validated. [6]
- **R3** The config file save skips the ApiKey key. It also skips keys that have no current value, and it writes only values that differ from the current ones. [4]
- **R4** The database config save skips keys that have no current value and keys whose submitted value is null. It writes only values that differ from the current ones. [5]
- **R5** In the config file, keys are written with the first character in upper case and values are trimmed. In the database, keys are stored in lower case. [7][8][9]

## Side effects

- **E1** Changed values are written to the config file, and the config file cache is cleared. [4][7][10]
- **E2** Changed values are upserted into the database config store, and the config cache is cleared. [9][5]
- **E3** If any config file value changed, a ConfigFileSavedEvent is published. The logging reconfiguration and the OIDC options change token source handle it. [4][11][12][13]
- **E4** If any database config value changed, a ConfigSavedEvent is published. The pending release service, task manager, localization service and UI authorization handler handle it. [5][11][14][15][16][17]
- **E5** Calls to a deprecated endpoint are logged as a warning, and the response gets a Deprecation: true header. [6]

## Couldn't see

- the depth limit left out 12 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on A1 (contradicted): Not every public property is written. ApiKey is skipped in the config file, keys with no current value are skipped, and only values that differ from the current ones are saved. **fixed**: Reworded the save action so it no longer claims every property is written; it now says values are offered and only qualifying ones are saved.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Config.UiConfigController.SaveConfig(Sonarr.Api.V3.Config.UiConfigResource)`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
4. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.SaveConfigDictionary(System.Collections.Generic.Dictionary{System.String,System.Object})`
5. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigService.SaveConfigDictionary(System.Collections.Generic.Dictionary{System.String,System.Object})`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
7. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.SetValue(System.String,System.Object)`
8. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.FirstCharToUpper(System.String)`
9. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigService.SetValue(System.String,System.String)`
10. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached.Clear`
11. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
12. [`Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.core-1b4c77/reconfigurelogging.handleasync-configfilesavedevent-31540a.md)
13. [`Sonarr.Http|M:Sonarr.Http.Authentication.OidcOptionsChangeTokenSource.Handle(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.http-01d2b2/oidcoptionschangetokensource.handle-configfilesavedevent-705974.md)
14. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-configsavedevent-79f05d.md)
15. [`Sonarr.Core|M:NzbDrone.Core.Jobs.TaskManager.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.core-1b4c77/taskmanager.handleasync-configsavedevent-7de6bb.md)
16. [`Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.core-1b4c77/localizationservice.handleasync-configsavedevent-9490a5.md)
17. [`Sonarr.Http|M:NzbDrone.Http.Authentication.UiAuthorizationHandler.Handle(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`](../../backend/sonarr.http-01d2b2/uiauthorizationhandler.handle-configsavedevent-b4d043.md)

<sub>commit 0bffefe · inputs a0fb753 · slice of 35 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
