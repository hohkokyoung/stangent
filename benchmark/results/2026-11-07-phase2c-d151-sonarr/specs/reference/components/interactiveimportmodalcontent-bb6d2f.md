# Interactive Import modal content

`ts|frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx#InteractiveImportModalContent` · component · sonarr

From the code: InteractiveImportModalContent

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The list of importable files shown is loaded from the manual import API for the given download ids, series, season number and folder. [1][2]
- **S2** The loaded items are sorted on the client by the stored sort key and direction. The default sort key is relative path. [1][3]
- **S3** The stored options default to the 'all' filter, import mode 'chooseImportMode', and a default column set. [3]
- **S4** The sorted items are given to a selection provider that wraps the modal's inner content. [2]

## Actions

Nothing found in the code.

## Rules

- **R1** The filter-existing-files setting defaults to false and is sent as a query parameter on the request. [4][2]
- **R2** Sorting by series uses the series title, by quality uses the quality name, and by languages uses the language names joined by commas. Missing values sort as an empty string. [1]
- **R3** Fetched data is not kept after the modal closes, and it is not refetched when the window regains focus. [1]
- **R4** Sort key and direction can only be changed through the dedicated sort setter, and setting them through the general option setters throws an error. [5]

## Side effects

- **E1** Opening the modal sends a GET request to the manual import endpoint with the API key header. [1][6]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on S2 (unsupported): The default sort key 'relativePath' is in the options store (sortKey: 'relativePath'), so that part holds. But sort direction is passed in as a parameter, and the default direction isn't shown. This is minor; the statement says only 'default sort is by relative path', which is supported. **fixed**: Reworded to say only the default sort key is relative path, since the default direction is passed in and not shown.

## Cited symbols

1. `ts|frontend/src/InteractiveImport/useInteractiveImport.ts#useInteractiveImport`
2. `ts|frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx#InteractiveImportModalContent`
3. `ts|frontend/src/InteractiveImport/interactiveImportOptionsStore.ts#createInteractiveImportOptionsStore`
4. `ts|frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx#filterExistingFilesStore`
5. `ts|frontend/src/Helpers/Hooks/useOptionsStore.ts#createOptionsStore`
6. `ts|frontend/src/Helpers/Hooks/useApiQuery.ts#useApiQuery`

<sub>commit 0bffefe · inputs 4eaaefe · slice of 102 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
