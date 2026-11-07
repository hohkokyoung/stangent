# Import Series – Select Folder

`page:/add/import` · screen · tsconfig.json

From the code: /add/import · ImportSeriesSelectFolder

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Import Series" with the heading scope "Media". [1]
- **S2** A loading indicator is shown while root folders are being fetched for the first time. [1]
- **S3** If loading root folders fails, a danger alert shows the root folders load error message. [1]
- **S4** Once loaded without error, the page shows a section heading with a description, and a collapsible tips block with three tips about library import. [1]
- **S5** The tips give example good and bad series folder paths, which use Windows-style paths (C:\tv shows) on Windows and Unix-style paths (/tv shows) otherwise. [1]
- **S6** If at least one root folder exists, the list of existing root folders is shown and each row is selectable. [1][2]
- **S7** Each root folder row shows its path, either "not accessible" or the count of folders to import (or nothing to import), and the free space when known. [3][4]
- **S8** A root folder row shows an "Unavailable" badge when not accessible, or an "Empty" warning badge when the folder is empty. [5]
- **S9** If adding a root folder fails, a danger alert lists the error messages, or the raw error body if it is not a list. [1]
- **S10** The folder button reads "Start Import" (primary style) when there are no root folders, otherwise "Choose Another Folder". [1]
- **S11** The file browser modal is titled "File Browser" and has a path input, a list of rows with an icon and name, and Cancel and Ok buttons. [6]
- **S12** The file browser shows "My Computer" row when the parent is empty, a ".." parent row when a parent exists, then directories and files. [6]
- **S13** The file browser shows an error message if contents fail to load, and a loading indicator in the footer while fetching. [6]
- **S14** On a Windows service, the file browser shows a warning about mapped network drives. [6]
- **S15** File browser row icons differ by type: computer, drive, file, parent, and folder by default. [7]

## Actions

- **A1** Pressing the folder button opens the file browser modal to choose a folder. [1]
- **A2** Choosing a folder with Ok adds it as a root folder. [1][6][8] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/RootFolder`](../backend/sonarr.api.v3-c45bd0/any-api-v3-rootfolder-081795.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/RootFolder`](../backend/sonarr.api.v5-c79e7b/any-api-v5-rootfolder-242215.md)
- **A3** In the file browser, clicking a row or editing the path input changes the current path and loads its contents; Cancel closes without choosing. [6][9] → [`endpoint:Sonarr.Api.V3|GET /api/v3/FileSystem`](../backend/sonarr.api.v3-c45bd0/get-api-v3-filesystem-efed3d.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/FileSystem`](../backend/sonarr.api.v5-c79e7b/get-api-v5-filesystem-ea9e85.md)
- **A4** Clicking a selectable, accessible root folder row opens the import page for that folder. [3]
- **A5** Each root folder row has a remove icon that opens a confirmation modal; confirming deletes the root folder. [3][10]

## Rules

- **R1** After a root folder is successfully added, the user is navigated to /add/import/{id} of the new folder. [1]
- **R2** The file browser includes files by default and requests paths allowing folders without trailing slashes. [6][9]
- **R3** Path suggestions are the directories and files whose path starts with the typed path, case-insensitively. [9]
- **R4** A non-selectable, accessible root folder row shows an "Import" button; unavailable rows have no link or button to import. [3]
- **R5** File sizes are formatted in base 2 rounded to 1 decimal; non-numeric input gives an empty string. [4]

## Side effects

- **E1** Adding a root folder sends a POST to the root folder API and updates the cached root folder list with the new or updated item. [8][11]
- **E2** Confirming removal sends a DELETE for the root folder and refreshes the root folder list on success. [10]
- **E3** The root folder list is loaded from the root folder API. [12]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/RootFolder/useRootFolders.ts:61` HTTP DELETE {base}rootFolder/{*} matched no backend endpoint
- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/AddSeries/ImportSeries/SelectFolder/ImportSeriesSelectFolder.tsx#ImportSeriesSelectFolder`
2. `ts|frontend/src/RootFolder/RootFolders.tsx#RootFolders`
3. `ts|frontend/src/RootFolder/RootFolderRow.tsx#RootFolderRow`
4. `ts|frontend/src/Utilities/Number/formatBytes.ts#formatBytes`
5. `ts|frontend/src/RootFolder/RootFolderRow.tsx#renderBadge`
6. `ts|frontend/src/Components/FileBrowser/FileBrowserModalContent.tsx#FileBrowserModalContent`
7. `ts|frontend/src/Components/FileBrowser/FileBrowserRow.tsx#getIconName`
8. `ts|frontend/src/RootFolder/useRootFolders.ts#useAddRootFolder`
9. `ts|frontend/src/Path/usePaths.ts#usePaths`
10. `ts|frontend/src/RootFolder/useRootFolders.ts#useDeleteRootFolder`
11. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#addOrUpdateQueryClientItem`
12. `ts|frontend/src/RootFolder/useRootFolders.ts#useRootFolders`

<sub>commit 0bffefe · inputs f88be9a · slice of 197 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
