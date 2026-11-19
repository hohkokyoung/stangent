# Import List Settings

`page:/settings/importlists` · screen · tsconfig.json

From the code: /settings/importlists · ImportListSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled 'Import List Settings' with the heading 'Import Lists' under the 'Settings' scope. [1]
- **S2** The toolbar has 'Test All Lists' and 'Manage Lists' buttons; the test button spins while testing is in progress. [1]
- **S3** A status indicator shows 'Testing all import lists' while testing, then a completed message on results or a failed message (as an alert) on error. It is hidden before any test. [1]
- **S4** Import lists show as cards sorted by name ascending, followed by an 'Add Import List' card. [2][3]
- **S5** Each list card shows its name, an 'Automatic Add' segment when enabled, the refresh interval, a test status label when test results exist, and its tags if any. [4]
- **S6** The list section shows an error message if loading fails. [2]
- **S7** A list's test status is 'notTested' with no result, 'failed' if any non-warning failure exists, 'warning' if only warnings exist, otherwise 'passed'. [5]
- **S8** The Options section ('Clean Library Level') appears only when advanced settings are shown. It shows a loading indicator while fetching and an error alert if loading fails. [6]
- **S9** The 'List Sync Tag' field appears only when the sync level is 'keepAndTag'. [6]
- **S10** The Import List Exclusions section is a table with select, title and TVDB ID columns plus an edit button per row. It has a pager, a 'Delete Selected' link and an 'Add Import List Exclusion' link. [7][8]
- **S11** The Manage Import Lists modal shows a table of lists, an info alert 'No import lists found' when empty, and Delete, Edit, Set Tags and Close buttons. [9]
- **S12** Each Manage row shows name, implementation, quality profile (or 'None'), root folder, automatic add as Yes/No, tags and tag-existing option. [10]
- **S13** The Add Import List modal groups available list types by list type, sorted by list order within each group. Items with presets show a Presets menu. [11][12]

## Actions

- **A1** Pressing 'Test All Lists' posts to the test-all endpoint and shows the result per list card. [13][1] → [`endpoint:Sonarr.Api.V3|POST /api/v3/ImportList/testall`](../backend/sonarr.api.v3-c45bd0/post-api-v3-importlist-testall-56ef26.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/ImportList/testall`](../backend/sonarr.api.v5-c79e7b/post-api-v5-importlist-testall-5a078d.md)
- **A2** Pressing 'Manage Lists' opens the Manage Import Lists modal. [1]
- **A3** The page Save button saves the Options section. [1][6]
- **A4** Pressing the 'Add Import List' card opens a modal to pick a list type. Selecting one opens the edit modal for that type. [2]
- **A5** Pressing a list card opens its edit modal. The clone action on the card opens an edit modal prefilled from that list. [4][2]
- **A6** The list edit modal has Name, Enable Automatic Add, search for missing episodes, monitor, root folder, quality profile, language, series type, season folder, provider fields, tags and tag existing. Buttons are Delete (existing only), Test, Cancel and Save. [14]
- **A7** Deleting a list asks for confirmation, then deletes it. [4][15]
- **A8** In the Manage modal, selected lists can be bulk deleted after confirmation. [9][16] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/ImportList/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-importlist-bulk-28a493.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/ImportList/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-importlist-bulk-9d0879.md)
- **A9** In the Manage modal, selected lists can be bulk edited for automatic add, quality profile, root folder, language and tag existing. [17][18] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/ImportList/bulk`](../backend/sonarr.api.v3-c45bd0/put-api-v3-importlist-bulk-c87b1b.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/ImportList/bulk`](../backend/sonarr.api.v5-c79e7b/put-api-v5-importlist-bulk-c13aed.md)
- **A10** In the Manage modal, 'Set Tags' applies tags to selected lists with an add, remove or replace choice. [19][9]
- **A11** Users can add an import list exclusion with a Title and a TVDB ID, or edit an existing one. [20][21]
- **A12** Users can delete one exclusion from its edit modal after confirmation, or delete selected exclusions after confirmation. [8][7] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/ImportListExclusion/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-importlistexclusion-bulk-958915.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/ImportListExclusion/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-importlistexclusion-bulk-60cd60.md)

## Rules

- **R1** Default values for a new list are: automatic add on, monitor 'all', series type 'standard', season folder on, empty root folder. Name is the preset name or the implementation name. [22]
- **R2** A cloned list is named from the original, gets id 0, and has API key and password field values cleared. [22]
- **R3** The Manage modal's Delete, Edit and Set Tags buttons are disabled until at least one list is selected. [9]
- **R4** In the bulk edit modal, only fields changed from 'no change' are sent. If nothing changed, nothing is saved. [17]
- **R5** Selecting a list sync tag stores the last selected tag id, or 0 when none is selected. [6]
- **R6** A saved list with no pending changes skips testing and validation on save. Resaving identical data skips testing, and after a warnings-only failure warnings are skipped on the next save or test. [23]
- **R7** Test-all results come from the response, or from the response body of a 400 error when it is an array. [24][13]
- **R8** The 'Delete Selected' link for exclusions is disabled when nothing is selected or a delete is running. [7]
- **R9** Exclusions are loaded page by page, and changing the page size returns to page 1. [7][25]

## Side effects

- **E1** Saving an exclusion sends a PUT (existing) or POST (new) to the exclusion endpoint, then refreshes the exclusion list. [21]
- **E2** Deleting exclusions refreshes the exclusion list. [26][27]
- **E3** Bulk edit and bulk delete of lists update the cached list data in place. [18][16]
- **E4** Saving Options sends a PUT of the settings and replaces the cached settings. [28][29]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/ImportLists/ImportListExclusions/useImportListExclusions.ts:78` HTTP POST to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime
- the depth limit left out 93 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/ImportLists/ImportListSettings.tsx#ImportListSettings`
2. `ts|frontend/src/Settings/ImportLists/ImportLists/ImportLists.tsx#ImportLists`
3. `ts|frontend/src/Settings/ImportLists/ImportLists/useImportLists.ts#useSortedImportLists`
4. `ts|frontend/src/Settings/ImportLists/ImportLists/ImportList.tsx#ImportList`
5. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestStatus`
6. `ts|frontend/src/Settings/ImportLists/Options/ImportListOptions.tsx#ImportListOptions`
7. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/ImportListExclusions.tsx#ImportListExclusionsContent`
8. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/ImportListExclusionRow.tsx#ImportListExclusionRow`
9. `ts|frontend/src/Settings/ImportLists/ImportLists/Manage/ManageImportListsModalContent.tsx#ManageImportListsModalContentInner`
10. `ts|frontend/src/Settings/ImportLists/ImportLists/Manage/ManageImportListsModalRow.tsx#ManageImportListsModalRow`
11. `ts|frontend/src/Settings/ImportLists/ImportLists/AddImportListModalContent.tsx#AddImportListModalContent`
12. `ts|frontend/src/Settings/ImportLists/ImportLists/AddImportListItem.tsx#AddImportListItem`
13. `ts|frontend/src/Settings/ImportLists/ImportLists/useImportLists.ts#useTestAllImportLists`
14. `ts|frontend/src/Settings/ImportLists/ImportLists/EditImportListModalContent.tsx#EditImportListModalContent`
15. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`
16. `ts|frontend/src/Settings/ImportLists/ImportLists/useImportLists.ts#useBulkDeleteImportLists`
17. `ts|frontend/src/Settings/ImportLists/ImportLists/Manage/Edit/ManageImportListsEditModalContent.tsx#ManageImportListsEditModalContent`
18. `ts|frontend/src/Settings/ImportLists/ImportLists/useImportLists.ts#useBulkEditImportLists`
19. `ts|frontend/src/Settings/ImportLists/ImportLists/Manage/Tags/TagsModalContent.tsx#TagsModalContent`
20. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/EditImportListExclusionModalContent.tsx#EditImportListExclusionModalContent`
21. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/useImportListExclusions.ts#useManageImportListExclusion`
22. `ts|frontend/src/Settings/ImportLists/ImportLists/useImportLists.ts#useManageImportList`
23. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`
24. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestAllResults`
25. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/useImportListExclusions.ts#useImportListExclusions`
26. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/useImportListExclusions.ts#useDeleteImportListExclusions`
27. `ts|frontend/src/Settings/ImportLists/ImportListExclusions/useImportListExclusions.ts#useDeleteImportListExclusion`
28. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
29. `ts|frontend/src/Settings/useSettings.ts#useManageSettings`

<sub>commit 0bffefe · inputs 8422a93 · slice of 595 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
