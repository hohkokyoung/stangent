# UI settings (GET /api/v5/settings/ui)

`endpoint:Sonarr.Api.V5|GET /api/v5/settings/ui` · backend · Sonarr.Api.V5

From the code: GET /api/v5/settings/ui

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns the UI settings resource as JSON with an OK response. [1][2]
- **S2** The UI settings resource is built from the config file provider and the config service. [2]

## Actions

- **A1** A caller requests the current UI settings with GET /api/v5/settings/ui and receives the settings resource. [3][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/settings/ui`

## Rules

- **R1** The returned resource always has Id set to 1. [1][4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 32 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Settings.SettingsController`1.GetConfig`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Settings.UiSettingsController.ToResource(NzbDrone.Core.Configuration.IConfigFileProvider,NzbDrone.Core.Configuration.IConfigService)`
3. `endpoint:Sonarr.Api.V5|GET /api/v5/settings/ui`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Settings.SettingsController`1.GetResourceById(System.Int32)`

<sub>commit 0bffefe · inputs 597c694 · slice of 19 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
