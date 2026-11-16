# SONB-15 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-15` from `95c42a9`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-15/worktree`
- **Given:** the ticket, the code and the failing test (`--context ticket`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.19 of the ticket's $3 (the proof $0.15); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs` · `Sonarr.Core|T:NzbDrone.Core.CustomFormats.ResolutionSpecificationValidator` · why: failing test `NzbDrone.Api.Test.v3.CustomFormats.ResolutionSpecificationValidationFixture.Validate_accepts_unknown_resolution` · reaches: `frontend/src/Activity/Blocklist/Blocklist.tsx`, `frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx`, `frontend/src/Activity/Blocklist/useBlocklist.ts`, `frontend/src/Activity/History/Details/HistoryDetailsModal.tsx`, `frontend/src/Activity/History/History.tsx`, `frontend/src/Activity/History/HistoryFilterModal.tsx`, `frontend/src/Activity/History/HistoryRow.tsx`, `frontend/src/Activity/History/useEpisodeHistory.ts` and 867 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 1 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 1 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-15/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point NzbDrone.Core.CustomFormats.ResolutionSpecificationValidator |

## Edited files

- `src/NzbDrone.Core/CustomFormats/Specifications/ResolutionSpecification.cs`

Next: a person reads the diff (`git diff 95c42a9..stangent/fix-SONB-15`), then pushes and opens the pull request; stangent doesn't (D18, D347).
