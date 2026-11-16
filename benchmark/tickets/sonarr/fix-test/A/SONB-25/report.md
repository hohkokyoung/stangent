# SONB-25 · fixed

every check passed.

- **Branch:** `stangent/fix-SONB-25` from `8c697af`; never pushed, no pull request (D347).
- **Worktree:** `.stangent/fix/SONB-25/worktree`
- **Given:** the ticket, the code and the failing test (`--context ticket`)
- **Model:** claude-sonnet-5-5, medium effort
- **Cost:** $0.17 of the ticket's $3 (the proof $0.11); 1 fixer call(s)

## Plan

- `src/NzbDrone.Core/Parser/LanguageParser.cs` · `Sonarr.Core|F:NzbDrone.Core.Parser.LanguageParser.LanguageRegex` · why: failing test `NzbDrone.Api.Test.Parser.DualLanguageParsingFixture.should_parse_both_french_and_english_from_fr_eng` · reaches: `frontend/src/AddSeries/AddNewSeries/AddNewSeries.tsx`, `frontend/src/AddSeries/AddNewSeries/AddNewSeriesModal.tsx`, `frontend/src/AddSeries/AddNewSeries/AddNewSeriesModalContent.tsx`, `frontend/src/AddSeries/AddNewSeries/AddNewSeriesSearchResult.tsx`, `frontend/src/AddSeries/AddNewSeries/useAddSeries.ts`, `frontend/src/App/App.tsx`, `frontend/src/App/AppRoutes.tsx`, `frontend/src/App/AppUpdatedModal.tsx` and 514 more

## Checks

| round | check | result | what it found |
|---|---|---|---|
| 1 | plan | ✅ passed | 1 change(s): every point and quote checks out |
| 1 | files | ✅ passed | 1 file(s) edited, each planned or in a planned file's reach |
| 1 | build | ✅ passed | `dotnet build 'src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj' -p:SolutionDir=/Users/hohkokyoung/stangent-bench/sonarr/.stangent/fix/SONB-25/worktree/src/ -p:NuGetAudit=false` built |
| 1 | tests | ✅ passed | the test passes here and nothing new fails |
| 1 | test-reach | ✅ passed | the proof's test reaches the planned point LanguageParser.LanguageRegex |

## Edited files

- `src/NzbDrone.Core/Parser/LanguageParser.cs`

Next: a person reads the diff (`git diff 8c697af..stangent/fix-SONB-25`), then pushes and opens the pull request; stangent doesn't (D18, D347).
