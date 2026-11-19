# List backups (GET /api/v5/system/backup)

`endpoint:Sonarr.Api.V5|GET /api/v5/system/backup` · backend · Sonarr.Api.V5

From the code: GET /api/v5/system/backup

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of backups, each with Id, Name, Path, Size, Type and Time. [1]
- **S2** Each backup's Path is shown as /backup/{type in lowercase}/{name}. [1]
- **S3** Each backup's Name is the backup file's name, its Size is the file size, and its Time is the file's last-write time. [2][3][4]

## Actions

- **A1** A caller requests the list of existing backups and receives it with an OK response. [5][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/system/backup`

## Rules

- **R1** Results are sorted by Time, newest first. [1]
- **R2** The Id is a non-negative 31-bit hash of the text "backup-{Type}-{Name}". [6][7]
- **R3** Every backup type is checked. Its folder is the backup folder plus the lowercase type name, and it is skipped if it doesn't exist. [2][8][9]
- **R4** Only files directly in the folder (no subfolders) whose names match the backup file name pattern are listed. [10]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 6 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetAll`
2. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackups`
3. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.GetFileSize(System.String)`
4. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FileGetLastWrite(System.String)`
5. `endpoint:Sonarr.Api.V5|GET /api/v5/system/backup`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupId(NzbDrone.Core.Backup.Backup)`
7. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
8. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFolder(NzbDrone.Core.Backup.BackupType)`
9. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFiles(System.String)`

<sub>commit 0bffefe · inputs 9aac32c · slice of 31 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
