# Indexer Settings

`page:/settings/indexers` · screen · tsconfig.json

From the code: /settings/indexers · IndexerSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Indexer Settings" and shows an "Indexers" heading with a section description, followed by the indexer cards and an Options section. [1]
- **S2** Toolbar buttons "Test All Indexers" (spinning while testing) and "Manage Indexers" are shown. [1]
- **S3** A status line shows "Testing all indexers" while testing, a completed message when results exist, or a failed message (as an alert) when the test call fails without per-indexer results. [1]
- **S4** Indexers are listed as cards sorted by name ascending, followed by an "Add Indexer" card. A load error message is shown if loading fails. [2][3]
- **S5** Each indexer card shows its name, Enabled/Disabled status, priority (only when any indexer's priority is not 25), and test status after a test-all run. [4][2]
- **S6** Each card shows a protocol label, Rss, Automatic Search and Interactive Search labels (only when supported and enabled), and a tag list when the indexer has tags. [4]
- **S7** The test status of a card is Not tested, Failed (any non-warning failure), Warning (only warnings) or Passed. [5][6]
- **S8** The Options section shows Minimum Age (minutes), Retention (days) and Maximum Size (MB) number inputs, plus an advanced RSS Sync Interval (minutes, 0-120) input. [7]
- **S9** The Options section shows a loading indicator while fetching and an error alert if loading options fails. [7]
- **S10** The Add Indexer dialog lists available indexer types grouped under Usenet and Torrents, with a loading indicator and an error alert on failure. Items with presets have a Presets menu and a More Info link. [8][9]
- **S11** The Manage Indexers dialog shows a table of indexers with selection checkboxes, name, protocol, implementation, Rss/Automatic Search/Interactive Search Yes/No labels, priority, max single episode age and tags. It shows "No indexers found" when empty. [10][11]

## Actions

- **A1** Pressing "Test All Indexers" runs a test of all indexers. [1][12] → [`endpoint:Sonarr.Api.V3|POST /api/v3/Indexer/testall`](../backend/sonarr.api.v3-c45bd0/post-api-v3-indexer-testall-9e4275.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/Indexer/testall`](../backend/sonarr.api.v5-c79e7b/post-api-v5-indexer-testall-e7a0ae.md)
- **A2** Pressing "Manage Indexers" opens the Manage Indexers dialog. [1]
- **A3** The toolbar save button saves the pending changes of the Options section. [1][13]
- **A4** Pressing the Add Indexer card opens the Add Indexer dialog; selecting an indexer type or preset closes it and opens the edit dialog. [2]
- **A5** Pressing a card opens the edit dialog; its clone action opens the edit dialog prefilled from that indexer. [4][2]
- **A6** The edit dialog offers Test, Cancel and Save, plus Delete when editing an existing indexer; Delete asks for confirmation then deletes the indexer. [14][4]
- **A7** In Manage Indexers, selected indexers can be bulk deleted (after confirmation), bulk edited, or have tags applied; the table can be sorted by column. [10] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/Indexer/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-indexer-bulk-42f488.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/Indexer/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-indexer-bulk-cc3bd5.md), [`endpoint:Sonarr.Api.V3|PUT /api/v3/Indexer/bulk`](../backend/sonarr.api.v3-c45bd0/put-api-v3-indexer-bulk-1b6d6a.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/Indexer/bulk`](../backend/sonarr.api.v5-c79e7b/put-api-v5-indexer-bulk-ca29bb.md)

## Rules

- **R1** Delete, Edit and Set Tags in Manage Indexers are disabled until at least one indexer is selected. [10]
- **R2** Bulk edit only sends fields changed from "no change" (Rss, Automatic Search, Interactive Search, priority, max single episode age); nothing is sent if none changed. Priority is 1-50 and max age is at least 0 days. [15]
- **R3** Tags can be applied to selected indexers with mode add (default), remove or replace. [16]
- **R4** In the edit dialog, indexer priority is 1-50, and Rss/search checkboxes are disabled when the indexer does not support them. [14]
- **R5** Cloning copies the indexer with name from the copied-profile default and clears apiKey and password field values. [17]
- **R6** A new indexer from a schema defaults its name to the implementation name, with RSS enabled if supported and automatic and interactive search enabled if search is supported. [17]
- **R7** Test-all results can come from a 400 error response body array; otherwise the error is treated as a failure. [18][12]
- **R8** Saving an existing indexer with no changes skips testing and validation; re-saving identical data skips testing; after a warnings-only result the next save skips warning validation. [19]
- **R9** The edit dialog closes automatically after a save finishes without error. [14]

## Side effects

- **E1** Test All sends a POST to the indexer testall endpoint. [12]
- **E2** Bulk edit sends a PUT to the indexer bulk endpoint and updates cached indexers with the result. [20]
- **E3** Bulk delete sends a DELETE to the indexer bulk endpoint and removes the deleted indexers from the cached list. [21]
- **E4** Deleting a single indexer sends a DELETE for that indexer and removes it from the cached list. [22][23]
- **E5** Saving Options sends a PUT of the settings and refreshes the cached settings, clearing pending changes. [24][13]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 77 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/Indexers/IndexerSettings.tsx#IndexerSettings`
2. `ts|frontend/src/Settings/Indexers/Indexers/Indexers.tsx#Indexers`
3. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useSortedIndexers`
4. `ts|frontend/src/Settings/Indexers/Indexers/Indexer.tsx#Indexer`
5. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestStatus`
6. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestStatusLabel`
7. `ts|frontend/src/Settings/Indexers/Options/IndexerOptions.tsx#IndexerOptions`
8. `ts|frontend/src/Settings/Indexers/Indexers/AddIndexerModalContent.tsx#AddIndexerModalContent`
9. `ts|frontend/src/Settings/Indexers/Indexers/AddIndexerItem.tsx#AddIndexerItem`
10. `ts|frontend/src/Settings/Indexers/Indexers/Manage/ManageIndexersModalContent.tsx#ManageIndexersModalContentInner`
11. `ts|frontend/src/Settings/Indexers/Indexers/Manage/ManageIndexersModalRow.tsx#ManageIndexersModalRow`
12. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useTestAllIndexers`
13. `ts|frontend/src/Settings/useSettings.ts#useManageSettings`
14. `ts|frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx#EditIndexerModalContent`
15. `ts|frontend/src/Settings/Indexers/Indexers/Manage/Edit/ManageIndexersEditModalContent.tsx#ManageIndexersEditModalContent`
16. `ts|frontend/src/Settings/Indexers/Indexers/Manage/Tags/TagsModalContent.tsx#TagsModalContent`
17. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useManageIndexer`
18. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestAllResults`
19. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`
20. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useBulkEditIndexers`
21. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useBulkDeleteIndexers`
22. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`
23. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useDeleteIndexer`
24. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`

<sub>commit 0bffefe · inputs d2d3a31 · slice of 439 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
