# System status

`endpoint:Sonarr.Api.V5|GET /api/v5/System/status` · backend · Sonarr.Api.V5

From the code: GET /api/v5/System/status

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON system status object with app name, instance name, version, build time and branch. [1]
- **S2** Returns flags for debug build, production, admin, user-interactive, .NET Core (always true), Linux, macOS, Windows and Docker. [1]
- **S3** Returns the startup path, the app data path, the OS name and the OS version. [1][2]
- **S4** Returns the database type, database version and migration version. [1]
- **S5** Returns the authentication method, URL base, run mode and start time. [1]
- **S6** Returns the runtime name and runtime version. [1]
- **S7** Returns the package version, package author, package update mechanism and its message. [1]

## Actions

- **A1** A caller requests GET /api/v5/System/status and receives the status object with a 200 OK response. [1] → `endpoint:Sonarr.Api.V5|GET /api/v5/System/status`

## Rules

- **R1** The app data path returned is the app folder info's AppDataFolder. [2]

## Side effects

Nothing found in the code.

## Couldn't see

- ❓ `src/NzbDrone.Host/Bootstrap.cs:110` configuration section 'Sonarr:App' is in no appsettings file of this project (environment variable or secret?)
- ❓ `src/NzbDrone.Host/Bootstrap.cs:112` configuration section 'Sonarr:Server' is in no appsettings file of this project (environment variable or secret?)
- the depth limit left out 13 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.SystemController.GetStatus`
2. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.GetAppDataPath(NzbDrone.Common.EnvironmentInfo.IAppFolderInfo)`

<sub>commit 0bffefe · inputs 48a01c4 · slice of 136 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
