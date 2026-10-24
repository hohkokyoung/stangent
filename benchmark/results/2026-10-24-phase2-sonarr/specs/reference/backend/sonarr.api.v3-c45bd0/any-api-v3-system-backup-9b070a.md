# Delete system backup

`endpoint:Sonarr.Api.V3|ANY /api/v3/system/backup` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/system/backup

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success the endpoint returns an empty object. [1]
- **S2** When the backup or its file is not found, the endpoint responds with HTTP 404 Not Found. [1][2]

## Actions

- **A1** A caller deletes a backup by its numeric id; the backup file is removed from disk. [1][3] → `endpoint:Sonarr.Api.V3|ANY /api/v3/system/backup`

## Rules

- **R1** The id is looked up among all existing backups; if no backup matches, the request fails with Not Found. [4][1]
- **R2** A backup's id is a non-negative 31-bit hash of the text "backup-{type}-{name}". [5][6]
- **R3** The backup file path is the backup type's folder combined with the backup name. [7]
- **R4** Each backup type's folder is a subfolder of the backup folder named after the lower-cased type. [8]
- **R5** The list of backups is built from every backup type's folder, only for folders that exist, and each entry has name, type, size and last-write time. [9]
- **R6** If the backup file does not exist on disk, deletion is not attempted and Not Found is returned. [1][10]

## Side effects

- **E1** The backup file is permanently deleted from disk. [1][3]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R1 (unsupported): Only the case of no match is shown to give Not Found (backup == null). SingleOrDefault with more than one match throws a different exception, so the code does not show that 'otherwise' means Not Found. **fixed**: Removed the claim that exactly one match is required; the rule now says only that no match gives Not Found.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.DeleteBackup(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.NotFoundException.#ctor(System.Object)`
3. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.DeleteFile(System.String)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackup(System.Int32)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackupId(NzbDrone.Core.Backup.Backup)`
6. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
7. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackupPath(NzbDrone.Core.Backup.Backup)`
8. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFolder(NzbDrone.Core.Backup.BackupType)`
9. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackups`
10. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FileExists(System.String)`

<sub>commit 0bffefe · inputs f5f945e · slice of 21 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
