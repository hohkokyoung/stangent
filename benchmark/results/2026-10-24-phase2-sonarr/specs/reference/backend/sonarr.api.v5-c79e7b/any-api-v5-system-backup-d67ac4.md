# Delete system backup

`endpoint:Sonarr.Api.V5|ANY /api/v5/system/backup` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/system/backup

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns no content (204) when the backup file is deleted successfully. [1]
- **S2** Returns Not Found when the backup does not exist or its file is missing from disk. [1][2]

## Actions

- **A1** Deletes the backup identified by the given numeric id by removing its file from disk. [1][3] → `endpoint:Sonarr.Api.V5|ANY /api/v5/system/backup`

## Rules

- **R1** The id is matched by finding the single backup whose computed id equals it, among all backups of all backup types. [4][5]
- **R2** A backup's id is a 31-bit hash of the text "backup-{type}-{name}". [6][7]
- **R3** The backup file path is the backup folder for its type (a lowercase type-named subfolder) combined with the backup name. [8][9]
- **R4** The file is deleted only if it exists on disk; otherwise Not Found is returned. [1][10]

## Side effects

- **E1** The backup file is permanently deleted from disk. [1][3]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.Delete(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.NotFoundException.#ctor(System.Object)`
3. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.DeleteFile(System.String)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupById(System.Int32)`
5. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackups`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupId(NzbDrone.Core.Backup.Backup)`
7. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupPath(NzbDrone.Core.Backup.Backup)`
9. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFolder(NzbDrone.Core.Backup.BackupType)`
10. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FileExists(System.String)`

<sub>commit 0bffefe · inputs bcac43b · slice of 21 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
