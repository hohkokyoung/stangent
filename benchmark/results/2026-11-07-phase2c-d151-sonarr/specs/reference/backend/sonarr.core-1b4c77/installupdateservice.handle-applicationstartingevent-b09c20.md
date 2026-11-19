# Post-install update check on application start

`Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.Handle(NzbDrone.Core.Lifecycle.ApplicationStartingEvent)` · backend · Sonarr.Core

From the code: InstallUpdateService.Handle(ApplicationStartingEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When the application is starting, it checks whether a post-install update is required and, if so, may download and install the latest available update. [1]
- **A2** The latest update is looked up for the configured branch and current build version via the update service. [2][3]

## Rules

- **R1** Nothing happens unless an 'update_required' marker file exists in the app data folder. [1]
- **R2** The check is skipped unless automatic updates are on, the update mechanism is BuiltIn, and the deployment does not use an external update mechanism. [1]
- **R3** If no update is available, the marker file is deleted and nothing is installed. If one is available, the marker is deleted before installation starts. [1]
- **R4** If installation starts, startup waits 30 seconds for the installer, then logs an error and continues normal operation. If installation is cancelled, startup continues. Any exception is logged and startup continues. [1]
- **R5** Installation fails if the app data folder is the startup folder or inside it. [4]
- **R6** Unless the mechanism is Script, the startup folder and its UI folder must be writable, otherwise the update fails with a not-writable error. [5]
- **R7** If the startup folder path ends with '_output' (developer environment), the update is not installed. [5]
- **R8** A warning about low free space is logged if the total size of the temp location's drive is less than 1 GB (the check uses total size, not free space). [5][6]
- **R9** The downloaded package is verified by comparing its SHA256 hash, case-insensitively, with the package's hash. A mismatch fails the update as invalid. [7][5]
- **R10** Archives ending in .zip are extracted as zip, and all others as tgz. Zip entries that would extract outside the target folder are rejected. [8][9]
- **R11** With the Script mechanism, a script path must be configured and the file must exist, otherwise the update fails. [10]
- **R12** With the non-script mechanism, if the update client executable does not exist after preparing it, the update is aborted with a warning and not started. [5]
- **R13** On macOS and Linux the update client gets permissions 755. [5]
- **R14** The updater is started with the current process id, the quoted sandbox folder, the quoted executing application and the preserved startup arguments. [11]

## Side effects

- **E1** The 'update_required' marker file is deleted. [1][12]
- **E2** Any old update sandbox folder is deleted, and the update package is downloaded into the sandbox folder in the temp folder and extracted there. [5][13]
- **E3** If the package's branch differs from the configured branch, the configuration's Branch is saved with the package's branch. A save failure is only logged. A saved change publishes a config-saved event. [14][15]
- **E4** The config-saved event is handled by logging reconfiguration and by the OIDC options change token source. [16][17]
- **E5** A backup of type Update is created before installing. It includes the config file, the SQLite database and version info, zipped into the update backup folder. [18][19][20]
- **E6** With the built-in mechanism, the update client folder is moved into the sandbox and the update client process is started. The app announces that Sonarr will restart shortly. [5]
- **E7** With the Script mechanism, the update client folder is deleted and the configured script is started. [10]

## Couldn't see

- ❓ `src/NzbDrone.Host/Bootstrap.cs:114` configuration section 'Sonarr:Update' is in no appsettings file of this project (environment variable or secret?)
- the depth limit left out 83 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R8 (contradicted): The code compares the total size of the temp location's drive with 1 GB, not its free space. The log message says 'free space', but the check does not measure free space. **fixed**: Reworded the rule to say the check compares the drive's total size with 1 GB, not free space.

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.Handle(NzbDrone.Core.Lifecycle.ApplicationStartingEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Update.CheckUpdateService.AvailableUpdate`
3. `Sonarr.Core|M:NzbDrone.Core.Update.UpdatePackageProvider.GetLatestUpdate(System.String,System.Version)`
4. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.EnsureAppDataSafety`
5. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.InstallUpdate(NzbDrone.Core.Update.UpdatePackage)`
6. `Sonarr.Common|M:NzbDrone.Common.Extensions.NumberExtensions.Gigabytes(System.Int32)`
7. `Sonarr.Core|M:NzbDrone.Core.Update.UpdateVerification.Verify(NzbDrone.Core.Update.UpdatePackage,System.String)`
8. `Sonarr.Common|M:NzbDrone.Common.ArchiveService.Extract(System.String,System.String)`
9. `Sonarr.Common|M:NzbDrone.Common.ArchiveService.ExtractZip(System.String,System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.InstallUpdateWithScript(System.String)`
11. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.GetUpdaterArgs(System.String)`
12. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.DeleteFile(System.String)`
13. `Sonarr.Common|M:NzbDrone.Common.Http.HttpClient.DownloadFile(System.String,System.String)`
14. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.EnsureValidBranch(NzbDrone.Core.Update.UpdatePackage)`
15. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.SaveConfigDictionary(System.Collections.Generic.Dictionary{System.String,System.Object})`
16. [`Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.core-1b4c77/reconfigurelogging.handleasync-configfilesavedevent-31540a.md)
17. [`Sonarr.Http|M:Sonarr.Http.Authentication.OidcOptionsChangeTokenSource.Handle(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.http-01d2b2/oidcoptionschangetokensource.handle-configfilesavedevent-705974.md)
18. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.Backup(NzbDrone.Core.Backup.BackupType)`
19. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.BackupDatabase`
20. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.BackupConfigFile`

<sub>commit 0bffefe · inputs 9dff983 · slice of 150 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
