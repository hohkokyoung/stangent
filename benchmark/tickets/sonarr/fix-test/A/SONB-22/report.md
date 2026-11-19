# SONB-22 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-22` from `de0f1d4`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-22/worktree`
- **Given:** the ticket, the code and the failing test (`--context ticket`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.31 of the ticket's $3 (the proof $0.23); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs` · `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.Consumers.Xbmc.XbmcMetadata.EpisodeMetadata(NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)` · why: failing test `NzbDrone.Api.Test.XbmcMultiEpisodeNfoFixture.should_write_single_xml_declaration_for_multi_episode_nfo` · reaches: `frontend/src/Activity/Blocklist/Blocklist.tsx`, `frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx`, `frontend/src/Activity/Blocklist/useBlocklist.ts`, `frontend/src/Activity/History/Details/HistoryDetailsModal.tsx`, `frontend/src/Activity/History/History.tsx`, `frontend/src/Activity/History/HistoryFilterModal.tsx`, `frontend/src/Activity/History/HistoryRow.tsx`, `frontend/src/Activity/History/useHistory.ts` and 796 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 1 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 1 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-22/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point XbmcMetadata.EpisodeMetadata |

## Edited files

- `src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs`

Next: a person reads the diff (`git diff de0f1d4..stangent/fix-SONB-22`), then pushes and opens the pull request; stangent doesn't (D18, D347).
