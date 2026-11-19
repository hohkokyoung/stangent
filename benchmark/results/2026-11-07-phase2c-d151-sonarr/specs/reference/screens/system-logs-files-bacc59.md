# System Log Files

`page:/system/logs/files` · screen · tsconfig.json

From the code: /system/logs/files · AppLogFiles

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Log Files" with the heading scope "System". [1]
- **S2** A toolbar shows a logs navigation menu labelled with the current view, plus "Refresh" and "Clear" buttons. [1]
- **S3** The nav menu offers two entries: "Log Files" (/system/logs/files) and "Updater Log Files" (/system/logs/files/update). [2]
- **S4** An info box shows the log files location, built from the app data folder and the "logs" subfolder (or "UpdateLogs" for the updater view), joined with a backslash on Windows and a slash otherwise. [1][3]
- **S5** When the current view is "Log Files", the info box also shows a note about the default log level. [1]
- **S6** A loading indicator is shown while log files are being fetched. [1]
- **S7** When not fetching and files exist, a table lists the files, with one row per file. [1]
- **S8** Each row shows the filename, the last write time as a relative date, and a "Download" link that opens in a new tab. [4]
- **S9** When not fetching and there are no files, an info message for "no log files" is shown. [1]
- **S10** The Refresh icon spins while fetching, and the Clear icon spins while the delete-log-files command is queued or started. [1][5]

## Actions

- **A1** Pressing Refresh re-fetches the list of log files. [6][7] → [`endpoint:Sonarr.Api.V3|GET /api/v3/log/file`](../backend/sonarr.api.v3-c45bd0/get-api-v3-log-file-8c8092.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/log/file`](../backend/sonarr.api.v5-c79e7b/get-api-v5-log-file-a4ddfc.md)
- **A2** Pressing Clear runs the DeleteLogFiles command and, when the command finishes, re-fetches the file list. [6][8]
- **A3** Choosing an entry in the nav menu navigates to the Log Files or Updater Log Files page. [2]

## Rules

- **R1** The same command is not sent again if it was run within the last 5 seconds; a warning is logged to the console instead. [8]
- **R2** The Clear button shows as executing only while the command status is queued or started. [5]
- **R3** This page is the application log view (type "app"), so it uses the "logs" folder. [6][1]

## Side effects

- **E1** Clear posts a new command to /command, and on success the command is added to the cached command list. [8]
- **E2** Loading the page requests the log file list from /log/file with the API key and client headers. [7][9]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on S9 (unsupported): The source only shows translate('NoLogFiles'). The literal text "No log files" is not in the slice, because translation values aren't shown. **fixed**: Removed the literal text and key; the statement now describes the message generically.

## Cited symbols

1. `ts|frontend/src/System/Logs/LogFiles.tsx#LogFiles`
2. `ts|frontend/src/System/Logs/LogsNavMenu.tsx#LogsNavMenu`
3. `ts|frontend/src/Utilities/String/combinePath.ts#combinePath`
4. `ts|frontend/src/System/Logs/LogFilesTableRow.tsx#LogFilesTableRow`
5. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
6. `ts|frontend/src/System/Logs/App/AppLogFiles.tsx#AppLogFiles`
7. `ts|frontend/src/System/Logs/useLogFiles.ts#useLogFiles`
8. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
9. `ts|frontend/src/Helpers/Hooks/useApiQuery.ts#useApiQuery`

<sub>commit 0bffefe · inputs e809081 · slice of 93 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
