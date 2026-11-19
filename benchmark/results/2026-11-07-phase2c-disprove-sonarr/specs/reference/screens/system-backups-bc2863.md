# Backups

`page:/system/backups` · screen · tsconfig.json

From the code: /system/backups · Backups

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Backups" with the heading scope "System". [1]
- **S2** A loading indicator is shown while backups are being fetched. [1]
- **S3** If loading fails, a danger alert shows the "BackupsLoadError" message. [1]
- **S4** If there are no backups and no error after loading, an info alert shows "NoBackupsAreAvailable". [1]
- **S5** When backups exist, a table lists each one as a row with an icon, name link, size, relative date and action buttons. [1][2]
- **S6** The row icon and tooltip depend on backup type: manual shows "Manual", update shows "BeforeUpdate", anything else shows "Scheduled". [2]
- **S7** The backup name links to the backup file path under the app's URL base. [2]
- **S8** Backup size is shown in human-readable binary units rounded to 1 decimal; a non-numeric size shows blank. [3][2]
- **S9** The restore dialog shows three steps: Restore, Restart and Reload, each with a pending, spinning, success or error icon. [4][5]
- **S10** For a specific backup the restore dialog asks "WouldYouLikeToRestoreBackup" with the backup name; otherwise it shows a file input. [4]

## Actions

- **A1** "Backup Now" toolbar button runs the backup command and spins while it is queued or started. [1][6][7]
- **A2** "Restore Backup" toolbar button opens the restore dialog for uploading a backup file. [1]
- **A3** The page loads the backup list from the system backup endpoint. [8] → [`endpoint:Sonarr.Api.V3|GET /api/v3/system/backup`](../backend/sonarr.api.v3-c45bd0/get-api-v3-system-backup-c088f0.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/system/backup`](../backend/sonarr.api.v5-c79e7b/get-api-v5-system-backup-82a3f6.md)
- **A4** A row's restore button opens the restore dialog for that backup; Restore posts to the restore endpoint for its id. [2][9] → [`endpoint:Sonarr.Api.V3|POST /api/v3/system/backup/restore/{id:int}`](../backend/sonarr.api.v3-c45bd0/post-api-v3-system-backup-restore-id-int-e05ced.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/system/backup/restore/{id:int}`](../backend/sonarr.api.v5-c79e7b/post-api-v5-system-backup-restore-id-int-2123c5.md)
- **A5** With a chosen file and no backup id, Restore uploads the file as form field "restore". [4][10]
- **A6** A row's delete button opens a danger confirmation "DeleteBackup"; confirming deletes the backup and closes the dialog on success. [2][11]
- **A7** Cancel closes the restore dialog. [4]

## Rules

- **R1** The same backup command cannot be run again within 5 seconds; the repeat is ignored with a console warning. [7]
- **R2** The backup list is treated as fresh for 30 seconds. [8]
- **R3** The Restore button is disabled when there is no backup id and no file chosen, or while restoring, restarting or reloading. [4]
- **R4** The restore error message is the server's message, or "ErrorRestoringBackup" when none is given. [12]

## Side effects

- **E1** When the backup command finishes, the backup list is reloaded. [1]
- **E2** Successful restore, upload or delete invalidates the cached backup list. [9][10][11]
- **E3** After a successful restore the app posts a restart request, marks the app as restarting and starts pinging the server. [4][13]
- **E4** When the restart ends, the browser page is reloaded. [4]
- **E5** While restarting, the server status is polled every 5 seconds until it responds or returns 401. [14]

## Couldn't see

- ❓ `frontend/src/App/appStore.ts:122` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/System/Backup/useBackups.ts:26` HTTP DELETE {base}system/backup/{*} matched no backend endpoint
- ❓ `frontend/src/System/Backup/useBackups.ts:79` HTTP POST to a URL built at runtime
- the depth limit left out 6 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/System/Backup/Backups.tsx#Backups`
2. `ts|frontend/src/System/Backup/BackupRow.tsx#BackupRow`
3. `ts|frontend/src/Utilities/Number/formatBytes.ts#formatBytes`
4. `ts|frontend/src/System/Backup/RestoreBackupModalContent.tsx#RestoreBackupModalContent`
5. `ts|frontend/src/System/Backup/RestoreBackupModalContent.tsx#getStepIconProps`
6. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
7. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
8. `ts|frontend/src/System/Backup/useBackups.ts#useBackups`
9. `ts|frontend/src/System/Backup/useBackups.ts#useRestoreBackup`
10. `ts|frontend/src/System/Backup/useBackups.ts#useRestoreBackupUpload`
11. `ts|frontend/src/System/Backup/useBackups.ts#useDeleteBackup`
12. `ts|frontend/src/System/Backup/RestoreBackupModalContent.tsx#getErrorMessage`
13. `ts|frontend/src/System/useSystem.ts#useRestart`
14. `ts|frontend/src/App/appStore.ts#pingServerAfterTimeout`

<sub>commit 0bffefe · inputs 33b7d2e · slice of 175 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
