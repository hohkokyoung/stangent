# SONB-23 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-23` from `aac4760`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-23/worktree`
- **Given:** the ticket, the code, the failing test and the ticket bundle (`--context bundle`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.24 of the ticket's $3 (the proof $0.18); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs` · `Sonarr.Core|T:NzbDrone.Core.Download.Clients.QBittorrent.QBittorrentProxyV2` · why: failing test `NzbDrone.Api.Test.QBittorrentTests.QBittorrentProxyAuthFixture.should_reauthenticate_when_username_changes_with_same_password` · reaches: `frontend/src/Activity/Blocklist/Blocklist.tsx`, `frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx`, `frontend/src/Activity/Blocklist/useBlocklist.ts`, `frontend/src/Activity/History/Details/HistoryDetailsModal.tsx`, `frontend/src/Activity/History/History.tsx`, `frontend/src/Activity/History/HistoryFilterModal.tsx`, `frontend/src/Activity/History/HistoryRow.tsx`, `frontend/src/Activity/History/useHistory.ts` and 778 more
- `src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV1.cs` · `Sonarr.Core|T:NzbDrone.Core.Download.Clients.QBittorrent.QBittorrentProxyV1` · why: ticket "It seems like it re-uses the cookie." · reaches: `frontend/src/Activity/Blocklist/Blocklist.tsx`, `frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx`, `frontend/src/Activity/Blocklist/useBlocklist.ts`, `frontend/src/Activity/History/Details/HistoryDetailsModal.tsx`, `frontend/src/Activity/History/History.tsx`, `frontend/src/Activity/History/HistoryFilterModal.tsx`, `frontend/src/Activity/History/HistoryRow.tsx`, `frontend/src/Activity/History/useHistory.ts` and 778 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 2 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 2 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-23/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point NzbDrone.Core.Download.Clients.QBittorrent.QBittorrentProxyV2 |

## Edited files

- `src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV1.cs`
- `src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs`

Next: a person reads the diff (`git diff aac4760..stangent/fix-SONB-23`), then pushes and opens the pull request; stangent doesn't (D18, D347).
