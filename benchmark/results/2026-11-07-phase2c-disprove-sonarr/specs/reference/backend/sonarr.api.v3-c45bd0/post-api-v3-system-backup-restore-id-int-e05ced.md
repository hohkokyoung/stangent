# Restore backup

`endpoint:Sonarr.Api.V3|POST /api/v3/system/backup/restore/{id:int}` · backend · Sonarr.Api.V3

From the code: POST /api/v3/system/backup/restore/{id:int}

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** On success it returns an object with RestartRequired set to true. [1]

## Actions

- **A1** Restores the application from the backup identified by the id in the route. [1][2] → `endpoint:Sonarr.Api.V3|POST /api/v3/system/backup/restore/{id:int}`

## Rules

- **R1** The id is matched against the list of existing backups; each backup's id is a 31-bit hash of 'backup-{type}-{name}'. Exactly one backup must match (more than one match raises an error). [3][4][5]
- **R2** If no backup matches the id, a NotFound (404) error is thrown. [1][6]
- **R3** The backup file path is the backup folder for its type (a lowercase type-named subfolder) combined with the backup name. [7][8]
- **R4** Backups are listed from each backup type's folder, if the folder exists. [9]
- **R5** A .zip backup is extracted to a temporary folder; if it contains no config files and no database files, restore fails with NotFound 'Unable to restore database file from backup'. [10]
- **R6** Config and database files from a zip are all validated before any are restored. [10]
- **R7** A .xml backup is validated and restored as a config file; any other file is validated and restored as a database. [10]

## Side effects

- **E1** Restoring replaces the application's config file and/or database with the backup's contents. [10]
- **E2** For zip backups, the temporary folder 'sonarr_backup_restore' is deleted before extraction and again afterwards, even on failure. [10]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.Restore(System.Int32)`
2. `Sonarr.Core|M:NzbDrone.Core.Backup.IBackupService.Restore(System.String)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackup(System.Int32)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackupId(NzbDrone.Core.Backup.Backup)`
5. `Sonarr.Common|M:NzbDrone.Common.Crypto.HashConverter.GetHashInt31(System.String)`
6. `Sonarr.Http|M:Sonarr.Http.REST.NotFoundException.#ctor(System.Object)`
7. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.Backup.BackupController.GetBackupPath(NzbDrone.Core.Backup.Backup)`
8. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackupFolder(NzbDrone.Core.Backup.BackupType)`
9. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.GetBackups`
10. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.Restore(System.String)`

<sub>commit 0bffefe · inputs 89013f3 · slice of 20 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
