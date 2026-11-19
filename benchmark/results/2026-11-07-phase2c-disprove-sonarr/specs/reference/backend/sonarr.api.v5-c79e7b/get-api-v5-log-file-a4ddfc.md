# Log files list (GET /api/v5/log/file)

`endpoint:Sonarr.Api.V5|GET /api/v5/log/file` · backend · Sonarr.Api.V5

From the code: GET /api/v5/log/file

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of log files, each with Id, Filename, LastWriteTime, ContentsUrl and DownloadUrl. [1]
- **S2** Id is the file's 1-based position in the list before sorting; Filename is the file name without its folder path. [1]
- **S3** LastWriteTime is the file's last write time read from disk. [1][2]
- **S4** ContentsUrl and DownloadUrl are built from the configured URL base plus the log resource route and file name. [1]

## Actions

- **A1** A caller requests the list of log files and receives it as JSON. [3][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/log/file`

## Rules

- **R1** Results are ordered by last write time, newest first. [1]
- **R2** For the standard log controller, files come from the application's logs folder, non-recursively. [4][5][6]
- **R3** For the update log controller, the list is empty if the update log folder doesn't exist. [7][8][9]
- **R4** For the update log controller, only files in the update log folder (non-recursive) whose names match the log file route pattern, case-insensitively, are listed. [7]

## Side effects

Nothing found in the code.

## Couldn't see

- ❓ `src/NzbDrone.Host/Bootstrap.cs:112` configuration section 'Sonarr:Server' is in no appsettings file of this project (environment variable or secret?)
- the depth limit left out 10 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Logs.LogFileControllerBase.GetLogFilesResponse`
2. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FileGetLastWrite(System.String)`
3. `endpoint:Sonarr.Api.V5|GET /api/v5/log/file`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Logs.LogFileController.GetLogFiles`
5. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.GetLogFolder(NzbDrone.Common.EnvironmentInfo.IAppFolderInfo)`
6. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.GetFiles(System.String,System.Boolean)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Logs.UpdateLogFileController.GetLogFiles`
8. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
9. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.GetUpdateLogFolder(NzbDrone.Common.EnvironmentInfo.IAppFolderInfo)`

<sub>commit 0bffefe · inputs a95f819 · slice of 37 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
