# Restore backup by id (API)

`endpoint:Sonarr.Api.V5|POST /api/v5/system/backup/restore/{id:int}` · backend · Sonarr.Api.V5

From the code: POST /api/v5/system/backup/restore/{id:int}

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success it returns 200 with a result whose RestartRequired flag is true. [1]
- **S2** If no backup matches the id, it returns 404 Not Found. [1]

## Actions

- **A1** A caller POSTs to restore/{id} with an integer backup id to restore that backup. The backup is looked up, its file path is built, and the backup service restores from it. [1][2] → `endpoint:Sonarr.Api.V5|POST /api/v5/system/backup/restore/{id:int}`

## Rules

- **R1** A backup id is a 31-bit hash of the text "backup-{Type}-{Name}". The id must match exactly one existing backup across all backup types. [3][4][5]
- **R2** The backup file path is the backup type's lowercase folder under the backup folder, plus the backup name. [2][6]
- **R3** Available backups are the files found in each backup type's folder, when that folder exists. [7]
- **R4** A .zip backup is extracted to a temporary folder. If it holds no config or database files, the restore fails with a NotFound 'Unable to restore database file from backup'. [8]
- **R5** Config and database files are validated before any is restored. [8]
- **R6** A .xml file is restored as a config file. Any other file name is restored as a database file. [8]

## Side effects

- **E1** Restoring replaces the app's config and/or database files with those from the backup. [8]
- **E2** For a zip restore, the temporary folder sonarr_backup_restore is cleared before extraction and deleted afterwards. [8]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.Restore(System.Int32)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupPath(NzbDrone.Core.Backup.Backup)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupId(NzbDrone.Core.Backup.Backup)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.Backup.BackupController.GetBackupById(System.Int32)`
5. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
6. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFolder(NzbDrone.Core.Backup.BackupType)`
7. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackups`
8. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.Restore(System.String)`

<sub>commit 0bffefe · inputs 1f79178 · slice of 20 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
