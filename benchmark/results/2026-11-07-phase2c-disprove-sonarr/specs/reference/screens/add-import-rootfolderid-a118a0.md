# Import Series (root folder)

`page:/add/import/:rootFolderId` · screen · tsconfig.json

From the code: /add/import/:rootFolderId · ImportSeries

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Import Series" under the "Media" scope heading. [1]
- **S2** While root folders are first loading, a loading indicator is shown. [1]
- **S3** If root folders fail to load, a danger alert with the root-folders load error message is shown. [1]
- **S4** If the root folder has no unmapped folders, an info alert says all series in the root folder path have been imported. [1]
- **S5** When unmapped folders exist, they are listed as one card per folder, and a footer is shown. [1][2]
- **S6** Each card shows a select checkbox, a poster (or a "?" placeholder), the folder name or matched series title, and the folder path or relative path. [3]
- **S7** Cards that are existing, unmatched or unresolved duplicates show a status label: "Already in library", "Pending" or "Duplicate match". [4][3]
- **S8** Ready cards show per-folder selects: Monitor, Quality Profile, Series Type, Season Type, Metadata Language and Season Folder (Yes/No), disabled until a series is matched. [3]
- **S9** The footer shows a Defaults bar with Monitor, Quality Profile, Series Type, Metadata Language and Season Folder selects. [5]
- **S10** The footer shows a select-all checkbox, a selected-of-total count, a Ready count, a Pending count (when non-zero) and an Already in library count (when non-zero). [6]
- **S11** The footer has a Compact Rows checkbox. [6]
- **S12** If an import fails, a warning icon opens a popover titled "Import Errors" listing the error messages. [6]
- **S13** The series match control shows a search field with a loading indicator while looking up, or a warning icon (no match found, existing, or error message). [7]
- **S14** Lookup results list the series title, year and network with a link to TheTVDB. [8][7]

## Actions

- **A1** Pressing the search button ("Search N Folder(s)") starts looking up all folders not yet searched; it appears only when unsearched items exist and no lookup is running. [6][9]
- **A2** Pressing Stop (shown while lookups are queued) cancels processing and clears the lookup queue. [6][10]
- **A3** Pressing the Import button imports the selected importable folders as series via a POST to the series import endpoint. [6][11] → [`endpoint:Sonarr.Api.V3|POST /api/v3/series/import`](../backend/sonarr.api.v3-c45bd0/post-api-v3-series-import-8649c1.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/series/import`](../backend/sonarr.api.v5-c79e7b/post-api-v5-series-import-53f645.md)
- **A4** "Apply to selected" copies the default language, monitor, quality profile, season folder and series type onto every importable selected item. [6][5]
- **A5** The select-all checkbox selects all ready items and one chosen item per duplicate group, or clears the selection when unchecked. [6]
- **A6** The Metadata Settings button opens a modal to choose and save the preferred metadata language. [6][12]
- **A7** Users can search for and pick a different series match for a folder; picking one sets it as the selected series and, if its type is not standard, sets the series type to it. [7]
- **A8** Search results can be navigated with Arrow keys, chosen with Enter and dismissed with Escape. [7]
- **A9** The Compact Rows checkbox toggles compact rows. [6]

## Rules

- **R1** An item is 'existing' if its matched TVDB id is already in the library, 'duplicate' if another folder matched the same TVDB id, 'ready' if matched and unique, otherwise 'unmatched'. [13]
- **R2** Only ready items and one chosen item per duplicate group are importable; extra selected duplicates are deselected. [14]
- **R3** Ready items are auto-selected; unmatched and existing items are disabled and unselected. [2]
- **R4** The Import button is disabled when nothing importable is selected or a lookup is running. [6]
- **R5** The Metadata Settings button is disabled (with a tooltip) once any item has been searched or while a lookup runs. [6]
- **R6** A folder is auto-matched to the first lookup result only when not editing, no series is selected yet, and the query equals the folder name. [7]
- **R7** Lookup search input is debounced by 300 ms and the lookup runs only for a non-empty term, when it is the current queue item. [7][15]
- **R8** New items default to season type 'official' and the current default options; if the selected season type is not offered by the series, it resets to 'official'. [16][3]
- **R9** Each imported series is sent as monitored, monitor new items 'all', no tags, with no search for missing or cutoff-unmet episodes; the same TVDB id is sent only once. [11]
- **R10** If the default quality profile is unset or not found, it is set to the first quality profile. [1]

## Side effects

- **E1** After a successful import, root folder data is invalidated, the new series are appended to the cached series list, and imported folders are removed from the page. [11]
- **E2** Leaving the page or changing the root folder clears all import items. [1][17]
- **E3** Saving the metadata settings modal sends a PUT of the metadata source settings and closes the modal on success. [18][19]
- **E4** The page loads root folders from the root folder endpoint. [20]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/RootFolder/useRootFolders.ts:41` HTTP GET {base}rootFolder/{*} matched no backend endpoint
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 46 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/AddSeries/ImportSeries/Import/ImportSeries.tsx#ImportSeries`
2. `ts|frontend/src/AddSeries/ImportSeries/Import/ImportSeriesList.tsx#ImportSeriesList`
3. `ts|frontend/src/AddSeries/ImportSeries/Import/ImportSeriesCard.tsx#ImportSeriesCard`
4. `ts|frontend/src/AddSeries/ImportSeries/Import/ImportSeriesCard.tsx#getMatchStatusLabel`
5. `ts|frontend/src/AddSeries/ImportSeries/Import/ImportSeriesDefaults.tsx#ImportSeriesDefaults`
6. `ts|frontend/src/AddSeries/ImportSeries/Import/ImportSeriesFooter.tsx#ImportSeriesFooter`
7. `ts|frontend/src/AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSelectSeries.tsx#ImportSeriesSelectSeries`
8. `ts|frontend/src/AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSearchResult.tsx#ImportSeriesSearchResult`
9. `ts|frontend/src/AddSeries/ImportSeries/Import/importSeriesStore.ts#startProcessing`
10. `ts|frontend/src/AddSeries/ImportSeries/Import/importSeriesStore.ts#stopProcessing`
11. `ts|frontend/src/AddSeries/ImportSeries/Import/useImportSeries.ts#useImportSeries`
12. `ts|frontend/src/Settings/MetadataSource/MetadataSettingsModalContent.tsx#MetadataSettingsModalContent`
13. `ts|frontend/src/AddSeries/ImportSeries/Import/importSeriesEligibility.ts#getImportSeriesEligibility`
14. `ts|frontend/src/AddSeries/ImportSeries/Import/importSeriesEligibility.ts#getImportSeriesSelection`
15. `ts|frontend/src/AddSeries/AddNewSeries/useAddSeries.ts#useLookupSeries`
16. `ts|frontend/src/AddSeries/ImportSeries/Import/importSeriesStore.ts#useEnsureImportSeriesItems`
17. `ts|frontend/src/AddSeries/ImportSeries/Import/importSeriesStore.ts#clearImportSeries`
18. `ts|frontend/src/Settings/MetadataSource/useMetadataSourceSettings.ts#useSaveMetadataSourceSettings`
19. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
20. `ts|frontend/src/RootFolder/useRootFolders.ts#useRootFolders`

<sub>commit 0bffefe · inputs 4893aab · slice of 489 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
