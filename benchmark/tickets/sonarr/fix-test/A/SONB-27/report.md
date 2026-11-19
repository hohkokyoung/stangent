# SONB-27 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-27` from `4bdb040`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-27/worktree`
- **Given:** the ticket, the code and the failing test (`--context ticket`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.34 of the ticket's $3 (the proof $0.29); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs` · `Sonarr.Core|M:NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportService.Execute(NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportCommand)` · why: failing test `NzbDrone.Core.Test.MediaFiles.EpisodeImport.Manual.ManualImportServiceFixture.should_not_publish_untracked_completed_event_when_manually_importing_tracked_download` · reaches: `frontend/src/Store/Actions/interactiveImportActions.js`, `src/NzbDrone.Core/Authentication/UserService.cs`, `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs`, `src/NzbDrone.Core/Download/DownloadEventHub.cs`, `src/NzbDrone.Core/Download/History/DownloadHistoryService.cs`, `src/NzbDrone.Core/HealthCheck/Checks/ApiKeyValidationCheck.cs`, `src/NzbDrone.Core/HealthCheck/HealthCheckService.cs`, `src/NzbDrone.Core/Instrumentation/ReconfigureSentry.cs` and 15 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 1 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 1 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-27/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point ManualImportService.Execute |

## Edited files

- `src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs`

Next: a person reads the diff (`git diff 4bdb040..stangent/fix-SONB-27`), then pushes and opens the pull request; stangent doesn't (D18, D347).
