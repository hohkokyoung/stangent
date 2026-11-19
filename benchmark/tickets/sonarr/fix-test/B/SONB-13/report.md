# SONB-13 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-13` from `3d0029e`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-13/worktree`
- **Given:** the ticket, the code, the failing test and the ticket bundle (`--context bundle`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.22 of the ticket's $3 (the proof $0.15); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs` · `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowserProxy.ProcessGetRequest``1(NzbDrone.Common.Http.HttpRequest,NzbDrone.Core.Notifications.Emby.MediaBrowserSettings)` · why: failing test `NzbDrone.Core.Test.NotificationTests.MediaBrowserProxyFixture.get_paths_should_send_authorization_header` · reaches: `frontend/src/Activity/Blocklist/Blocklist.tsx`, `frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx`, `frontend/src/Activity/Blocklist/useBlocklist.ts`, `frontend/src/Activity/History/Details/HistoryDetailsModal.tsx`, `frontend/src/Activity/History/History.tsx`, `frontend/src/Activity/History/HistoryFilterModal.tsx`, `frontend/src/Activity/History/HistoryRow.tsx`, `frontend/src/Activity/History/useEpisodeHistory.ts` and 862 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 1 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 1 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-13/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point MediaBrowserProxy.ProcessGetRequest |

## Edited files

- `src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs`

Next: a person reads the diff (`git diff 3d0029e..stangent/fix-SONB-13`), then pushes and opens the pull request; stangent doesn't (D18, D347).
