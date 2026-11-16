# SONB-20 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-20` from `6b479a5`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-20/worktree`
- **Given:** the ticket, the code and the failing test (`--context ticket`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.21 of the ticket's $3 (the proof $0.17); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/Extras/Metadata/Consumers/Plex/PlexMetadata.cs` · `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.Consumers.Plex.PlexMetadata.SeriesMetadata(NzbDrone.Core.Tv.Series,NzbDrone.Core.Extras.Metadata.SeriesMetadataReason)` · why: failing test `NzbDrone.Core.Test.Extras.Metadata.Consumers.Plex.SeriesMetadataFixture.should_use_special_episode_number_in_plexmatch_for_specials` · reaches: `frontend/src/Activity/Blocklist/Blocklist.tsx`, `frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx`, `frontend/src/Activity/Blocklist/useBlocklist.ts`, `frontend/src/Activity/History/Details/HistoryDetailsModal.tsx`, `frontend/src/Activity/History/History.tsx`, `frontend/src/Activity/History/HistoryFilterModal.tsx`, `frontend/src/Activity/History/HistoryRow.tsx`, `frontend/src/Activity/History/useEpisodeHistory.ts` and 806 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 1 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 1 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-20/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point PlexMetadata.SeriesMetadata |

## Edited files

- `src/NzbDrone.Core/Extras/Metadata/Consumers/Plex/PlexMetadata.cs`

Next: a person reads the diff (`git diff 6b479a5..stangent/fix-SONB-20`), then pushes and opens the pull request; stangent doesn't (D18, D347).
