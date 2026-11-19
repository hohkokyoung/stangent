# Application Update Command (Install Update)

`Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.Execute(NzbDrone.Core.Update.Commands.ApplicationUpdateCommand)` · backend · Sonarr.Core

From the code: InstallUpdateService.Execute(ApplicationUpdateCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Progress messages are logged while the update runs, such as 'Checking for updates', 'No update available', 'Downloading update {version}', 'Verifying update package', 'Extracting Update package' and 'Sonarr will restart shortly.'. [1][2]
- **S2** When the update fails, the command fails with the message 'Startup folder not writable by user ...', 'Downloaded update package is corrupt', or a generic 'Failed' wrapping the original error. [3][4]

## Actions

- **A1** Runs the application update: looks for an available update and, if one is allowed, downloads, verifies, extracts and installs it, then logs that Sonarr is restarting to apply updates. [3][2]

## Rules

- **R1** No update is installed when none is available for the configured branch and current version. [1][5]
- **R2** A major version update is skipped unless the command allows installing major updates; the user is told to update manually from System: Updates. [1]
- **R3** If automatic updates are not enabled, the update is only installed when the command was triggered manually. [1]
- **R4** The built-in updater is disabled inside a docker container, and when an external update mechanism is in use. With an external mechanism, only the Script update mechanism can still install. [1]
- **R5** The update fails if the application's startup folder is the same as, or a parent of, the app data folder, because configuration would be lost during the upgrade. [6][7][8]
- **R6** Unless the update mechanism is Script, both the startup folder and its UI subfolder must be writable, otherwise the update fails. [2][9]
- **R7** When the startup folder ends with '_output' (developer environment), no update is performed. [2]
- **R8** A warning is logged when the temporary location has less than 1 GB free space. [2][10]
- **R9** The downloaded package must match the expected SHA256 hash (case-insensitive), otherwise the update fails with 'Update file ... is invalid'. [11][2]
- **R10** Archives ending in .zip are extracted as zip, others as tgz. Zip extraction validates the archive and rejects entries that would land outside the target folder. [12][13]
- **R11** With the Script mechanism, the script path must be set and the file must exist (case-sensitive match), otherwise the update fails. [14][15]
- **R12** With the built-in mechanism, if the update client executable does not exist after preparing the client, the update is aborted with a warning. [2]
- **R13** On macOS and Linux the update client executable is given permissions 755. [2]
- **R14** The updater is started with arguments: current process id, the quoted sandbox folder, the quoted executing application, and the preserved startup arguments. [16]

## Side effects

- **E1** Any existing update sandbox folder is deleted, then the update package is downloaded into it and extracted there. [2][17]
- **E2** A backup of type Update is created before installing, containing config.xml, the database (SQLite only), a version INFO file, zipped. [18][19][20][21]
- **E3** If the package's branch differs from the configured branch, the Branch setting is saved to the package's branch. Failure to save is logged and does not stop the update. [22]
- **E4** Saving a changed config value publishes a ConfigFileSavedEvent, handled by logging reconfiguration and OIDC options change handlers. [23][24][25]
- **E5** With the built-in mechanism, the update client folder is moved from the extracted package into the sandbox folder and the update client process is started to restart Sonarr. [2][26]
- **E6** With the Script mechanism, the Sonarr.Update client folder is deleted and the configured update script is started. [14]

## Couldn't see

- ❓ `src/NzbDrone.Host/Bootstrap.cs:114` configuration section 'Sonarr:Update' is in no appsettings file of this project (environment variable or secret?)
- the depth limit left out 66 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.GetUpdatePackage(NzbDrone.Core.Messaging.Commands.CommandTrigger,System.Boolean)`
2. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.InstallUpdate(NzbDrone.Core.Update.UpdatePackage)`
3. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.Execute(NzbDrone.Core.Update.Commands.ApplicationUpdateCommand)`
4. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandFailedException.#ctor(System.Exception)`
5. `Sonarr.Core|M:NzbDrone.Core.Update.CheckUpdateService.AvailableUpdate`
6. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.EnsureAppDataSafety`
7. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.IsParentPath(System.String,System.String)`
8. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.PathEquals(System.String,System.String,System.Nullable{System.StringComparison})`
9. `Sonarr.Core|M:NzbDrone.Core.Update.UpdateFolderNotWritableException.#ctor(System.String,System.Object[])`
10. `Sonarr.Common|M:NzbDrone.Common.Extensions.NumberExtensions.Gigabytes(System.Int32)`
11. `Sonarr.Core|M:NzbDrone.Core.Update.UpdateVerification.Verify(NzbDrone.Core.Update.UpdatePackage,System.String)`
12. `Sonarr.Common|M:NzbDrone.Common.ArchiveService.Extract(System.String,System.String)`
13. `Sonarr.Common|M:NzbDrone.Common.ArchiveService.ExtractZip(System.String,System.String)`
14. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.InstallUpdateWithScript(System.String)`
15. `Sonarr.Common|M:NzbDrone.Common.Disk.DiskProviderBase.FileExists(System.String,System.StringComparison)`
16. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.GetUpdaterArgs(System.String)`
17. `Sonarr.Common|M:NzbDrone.Common.Http.HttpClient.DownloadFileAsync(System.String,System.String,System.Threading.CancellationToken)`
18. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.Backup(NzbDrone.Core.Backup.BackupType)`
19. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.BackupDatabase`
20. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.BackupConfigFile`
21. `Sonarr.Core|M:NzbDrone.Core.Backup.BackupService.CreateVersionInfo(System.DateTime)`
22. `Sonarr.Core|M:NzbDrone.Core.Update.InstallUpdateService.EnsureValidBranch(NzbDrone.Core.Update.UpdatePackage)`
23. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.SaveConfigDictionary(System.Collections.Generic.Dictionary{System.String,System.Object})`
24. [`Sonarr.Core|M:NzbDrone.Core.Instrumentation.ReconfigureLogging.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.core-1b4c77/reconfigurelogging.handleasync-configfilesavedevent-31540a.md)
25. [`Sonarr.Http|M:Sonarr.Http.Authentication.OidcOptionsChangeTokenSource.Handle(NzbDrone.Core.Configuration.Events.ConfigFileSavedEvent)`](../../backend/sonarr.http-01d2b2/oidcoptionschangetokensource.handle-configfilesavedevent-705974.md)
26. `Sonarr.Common|M:NzbDrone.Common.Processes.IProcessProvider.Start(System.String,System.String,System.Collections.Specialized.StringDictionary,System.Action{System.String},System.Action{System.String})`

<sub>commit 0bffefe · inputs 68fa0cd · slice of 155 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
