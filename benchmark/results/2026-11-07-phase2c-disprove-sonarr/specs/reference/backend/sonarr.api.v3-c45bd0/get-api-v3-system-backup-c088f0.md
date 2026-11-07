# List backups (GET /api/v3/system/backup)

`endpoint:Sonarr.Api.V3|GET /api/v3/system/backup` · backend · Sonarr.Api.V3

From the code: GET /api/v3/system/backup

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of backup files, each with Id, Name, Path, Size, Type and Time. [1]
- **S2** Each backup's Path is built as /backup/{type in lower case}/{name}. [1]
- **S3** Each backup's Id is a non-negative 31-bit integer hash of the text 'backup-{type}-{name}'. [2][3]
- **S4** Each backup's Name is the file name, Size is the file size on disk, and Time is the file's last write time. [4][5][6]

## Actions

- **A1** A caller requests the list of all existing backups. [1] → `endpoint:Sonarr.Api.V3|GET /api/v3/system/backup`

## Rules

- **R1** The results are sorted by Time, newest first. [1]
- **R2** Backups are gathered from one folder per backup type, named after the lower-cased type; a type's folder is skipped if it does not exist. [4][7][8]
- **R3** Files in each folder are listed with the recursive option off, and only files whose names match the backup file name pattern are included. [9]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 6 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R3 (unsupported): The code shows files are listed non-recursively (GetFiles(path, false)) and filtered by BackupFileRegex, but the 'false' parameter's meaning (no subfolders) isn't defined in the slice. Mostly supported though; the regex pattern itself isn't shown. **fixed**: Reworded to say files are listed with the recursive option off and filtered by the backup file name pattern, without claiming what the parameter means beyond the code.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackupFiles`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackupId(NzbDrone.Core.Backup.Backup)`
3. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackups`
5. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.GetFileSize(System.String)`
6. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FileGetLastWrite(System.String)`
7. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFolder(NzbDrone.Core.Backup.BackupType)`
8. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
9. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFiles(System.String)`

<sub>commit 0bffefe · inputs 04d2d2d · slice of 31 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
