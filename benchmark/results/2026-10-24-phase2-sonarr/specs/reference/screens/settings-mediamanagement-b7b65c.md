# Media Management Settings

`page:/settings/mediamanagement` · screen · tsconfig.json

From the code: /settings/mediamanagement · MediaManagement

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled 'Media Management Settings' and has a heading 'Media Management' under the scope 'Settings'. [1]
- **S2** While settings load, a loading indicator is shown in a 'Naming Settings' field set; if loading fails, a danger alert 'MediaManagementSettingsLoadError' is shown there instead. [1]
- **S3** The 'Episode Naming' section shows checkboxes 'Rename Episodes' and 'Replace Illegal Characters', plus a Multi Episode Style select with options Extend, Duplicate, Repeat, Scene, Range and Prefixed Range, each with an example hint. [2]
- **S4** The Episode Naming section shows a loading indicator while fetching and a danger alert if naming settings fail to load. [2]
- **S5** Naming format fields show example output as help text, or an invalid-format error when the server returns no example for that format. [2]
- **S6** The File Management section shows: Unmonitor Deleted Episodes, Download Propers and Repacks, Analyse Video Files, Rescan Series Folder After Refresh, Change File Date, Recycling Bin, and Recycling Bin Cleanup. [1]
- **S7** A Root Folders section lists each root folder with its path, a count of folders to import or 'nothing to import', and free space when known. [1][3][4]
- **S8** A root folder row shows an 'Unavailable' badge when not accessible, or an 'Empty' badge when empty. [5]
- **S9** The root folder list shows a loading indicator on first load and a danger alert 'RootFoldersLoadError' on error. [3]
- **S10** If adding a root folder fails, an alert lists the error messages returned. [6]
- **S11** The naming token modal is titled 'File Name Tokens' for episode formats and 'Folder Name Tokens' otherwise, and shows grouped tokens with examples, separator and capitalization selects. [7]

## Actions

- **A1** Pressing save on the settings page saves the media management settings and the naming settings together. [1]
- **A2** Editing any field records a pending change that is saved only on save. [1][8]
- **A3** Pressing '?' next to a naming format field opens the token modal for that format (standard, daily, anime, series folder, season folder, specials folder). [2]
- **A4** Clicking a token in the naming modal inserts it at the caret (or appends it); a full-filename preset replaces the whole value. [7]
- **A5** 'Add Root Folder' opens a file browser; confirming with Ok adds the chosen path as a root folder. [6][9] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/RootFolder`](../backend/sonarr.api.v3-c45bd0/any-api-v3-rootfolder-081795.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/RootFolder`](../backend/sonarr.api.v5-c79e7b/any-api-v5-rootfolder-242215.md)
- **A6** The file browser lists directories and files from the file system; selecting a row navigates to it, and Cancel or Ok close it. [9][10] → [`endpoint:Sonarr.Api.V3|GET /api/v3/FileSystem`](../backend/sonarr.api.v3-c45bd0/get-api-v3-filesystem-efed3d.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/FileSystem`](../backend/sonarr.api.v5-c79e7b/get-api-v5-filesystem-ea9e85.md)
- **A7** Each root folder row has an 'Import' button (hidden when unavailable) and a remove button that opens a confirmation modal before deleting. [4][11]

## Rules

- **R1** The Folders and Importing sections, and advanced rows, appear only when advanced settings are shown. [1]
- **R2** The Permissions section (Set Permissions, chmod folder, chown group) appears only when advanced settings are shown and the system is not Windows. [1]
- **R3** The import script path appears only when 'Import Using Script' is checked; the extra file extensions field appears only when 'Import Extra Files' is checked. [1]
- **R4** The season pack upgrade threshold field appears only when season pack upgrade is 'threshold'; it is a percent from 0 to 100 in steps of 0.01, with examples shown. [1][12]
- **R5** Threshold examples are not shown if the threshold is not a number or is outside 0 to 100. [12]
- **R6** Warnings show when season pack upgrade is 'any' and when Download Propers and Repacks is 'doNotPrefer'. [1]
- **R7** Minimum free space is a number in MB; Recycling Bin Cleanup days is a number with minimum 0. [1]
- **R8** The Standard, Daily and Anime episode format fields appear only when Rename Episodes is on. [2]
- **R9** Colon Replacement appears only when Replace Illegal Characters is on, and the custom colon replacement field only when its value is 5 (Custom). [2]
- **R10** Series Folder Format and Specials Folder Format rows are advanced-only. [2]
- **R11** Naming examples are requested with the naming settings debounced by 300 ms. [2][13]
- **R12** The token modal's default separator is a space and default capitalization is 'title'; lower or upper capitalization changes the inserted token's case. [7][14]
- **R13** Presets appear only for episode formats; daily and anime presets and absolute-number tokens only for those types; season tokens only when season or season title applies. [15]
- **R14** The file browser shows a mapped network drives warning on Windows when running as a service. [9]
- **R15** Settings validation errors and warnings from a 400 save response are attached to the matching field by property name. [16][17]

## Side effects

- **E1** Saving writes the settings with a PUT to the settings path and clears pending changes on success. [18][8]
- **E2** Adding a root folder POSTs the path to the root folder API and adds the result to the cached list. [19]
- **E3** Confirming removal sends a DELETE for the root folder and refreshes the root folder list. [11]
- **E4** Naming examples are fetched with a GET to the naming examples endpoint using the current naming values. [20]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/RootFolder/useRootFolders.ts:61` HTTP DELETE {base}rootFolder/{*} matched no backend endpoint
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 17 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/MediaManagement/MediaManagement.tsx#MediaManagement`
2. `ts|frontend/src/Settings/MediaManagement/Naming/Naming.tsx#Naming`
3. `ts|frontend/src/RootFolder/RootFolders.tsx#RootFolders`
4. `ts|frontend/src/RootFolder/RootFolderRow.tsx#RootFolderRow`
5. `ts|frontend/src/RootFolder/RootFolderRow.tsx#renderBadge`
6. `ts|frontend/src/Settings/MediaManagement/RootFolder/AddRootFolder.tsx#AddRootFolder`
7. `ts|frontend/src/Settings/MediaManagement/Naming/NamingModal.tsx#NamingModal`
8. `ts|frontend/src/Settings/useSettings.ts#useManageSettings`
9. `ts|frontend/src/Components/FileBrowser/FileBrowserModalContent.tsx#FileBrowserModalContent`
10. `ts|frontend/src/Path/usePaths.ts#usePaths`
11. `ts|frontend/src/RootFolder/useRootFolders.ts#useDeleteRootFolder`
12. `ts|frontend/src/Settings/MediaManagement/MediaManagement.tsx#getSeasonPackUpgradeThresholdExamples`
13. `ts|frontend/src/Helpers/Hooks/useDebounce.ts#useDebounce`
14. `ts|frontend/src/Settings/MediaManagement/Naming/NamingOption.tsx#NamingOption`
15. `ts|frontend/src/Settings/MediaManagement/Naming/NamingModal.tsx#getNamingTokenGroups`
16. `ts|frontend/src/Utilities/selectSettings.ts#selectSettings`
17. `ts|frontend/src/Utilities/selectSettings.ts#getFailures`
18. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
19. `ts|frontend/src/RootFolder/useRootFolders.ts#useAddRootFolder`
20. `ts|frontend/src/Settings/MediaManagement/Naming/useNamingSettings.ts#useNamingExamples`

<sub>commit 0bffefe · inputs 7b467e7 · slice of 449 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
