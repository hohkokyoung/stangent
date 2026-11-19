# Updater Log Files

`page:/system/logs/files/update` · screen · tsconfig.json

From the code: /system/logs/files/update · UpdateLogFiles

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Log Files" and has a heading "Log Files" with the scope "System". [1]
- **S2** A logs menu button in the toolbar shows the current view, which is "Updater Log Files" on this page. Its menu links to "Log Files" (/system/logs/files) and "Updater Log Files" (/system/logs/files/update). [1][2]
- **S3** The toolbar has "Refresh" and "Clear" buttons. The Refresh icon spins while log files are being fetched. The Clear icon spins while the delete-update-log-files command is queued or started. [1][3][4]
- **S4** An alert shows the log files location. It is the app data folder plus "UpdateLogs", joined with a backslash on Windows and a forward slash otherwise. [1][5][6]
- **S5** A loading indicator shows while log files are being fetched. [1]
- **S6** When not fetching and there are files, a table lists them. Each row shows the filename, the last write time as a relative date, and a "Download" link that opens in a new tab. [1][7]
- **S7** When not fetching and there are no files, an info alert shows the "No log files" message (translation key NoLogFiles). [1]

## Actions

- **A1** Pressing Refresh re-fetches the updater log file list. [4][8] → [`endpoint:Sonarr.Api.V3|GET /api/v3/log/file/update`](../backend/sonarr.api.v3-c45bd0/get-api-v3-log-file-update-a61e0d.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/log/file/update`](../backend/sonarr.api.v5-c79e7b/get-api-v5-log-file-update-44008f.md)
- **A2** Pressing Clear runs the DeleteUpdateLogFiles command, then re-fetches the list when the command finishes. [4][9]
- **A3** Choosing an item in the logs menu navigates to the Log Files or Updater Log Files page. [2]

## Rules

- **R1** Running the same command again within 5 seconds of the previous one is ignored, and a console warning is logged. [9]
- **R2** The "log level default" note is shown only when the current view is "Log Files". It does not appear on this updater page. [1]
- **R3** The file list is loaded from the /log/file/update path. It defaults to an empty list until data arrives. [8][4]

## Side effects

- **E1** Pressing Clear posts a command named DeleteUpdateLogFiles to /command. The command is stored in the local command cache on success. [9][4]
- **E2** Loading the list sends a GET request with the API key and client headers. [10][8]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on S4 (unsupported): The location Alert is rendered without a kind (only the NoLogFiles alert has kind={kinds.INFO}), so the source doesn't show it as an info alert. **fixed**: Removed the word "info" from the location alert statement, since that alert has no kind set.

## Cited symbols

1. `ts|frontend/src/System/Logs/LogFiles.tsx#LogFiles`
2. `ts|frontend/src/System/Logs/LogsNavMenu.tsx#LogsNavMenu`
3. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
4. `ts|frontend/src/System/Logs/Update/UpdateLogFiles.tsx#UpdateLogFiles`
5. `ts|frontend/src/Utilities/String/combinePath.ts#combinePath`
6. `ts|frontend/src/System/Status/useSystemStatus.ts#useSystemStatusData`
7. `ts|frontend/src/System/Logs/LogFilesTableRow.tsx#LogFilesTableRow`
8. `ts|frontend/src/System/Logs/useLogFiles.ts#useUpdateLogFiles`
9. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
10. `ts|frontend/src/Helpers/Hooks/useApiQuery.ts#useApiQuery`

<sub>commit 0bffefe · inputs 0f7dc7e · slice of 93 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
