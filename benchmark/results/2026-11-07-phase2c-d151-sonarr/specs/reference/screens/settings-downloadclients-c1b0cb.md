# Download Client Settings

`page:/settings/downloadclients` · screen · tsconfig.json

From the code: /settings/downloadclients · DownloadClientSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Download Clients" under the "Settings" scope, with a section heading "Download Clients" and a description. [1]
- **S2** The toolbar has "Test All Clients" and "Manage Clients" items. The test item spins while a test is running. [1]
- **S3** A status indicator shows "Testing all download clients", a completed message, or a failed message, depending on the test-all state. It is hidden when none applies; it uses the alert role on error. [1]
- **S4** Download clients are shown as cards sorted by name ascending, followed by an "Add Download Client" card. A load error message is shown if loading fails. [2][3]
- **S5** Each client card shows its name, an Enabled or Disabled status, the priority when greater than 1, the last test status, a protocol label, and tags when present. [4]
- **S6** After a test-all, each card shows its result as not tested, failed (any non-warning failure), warning (only warnings), or passed. [5][2]
- **S7** The Add Download Client dialog lists available client types in Usenet and Torrents groups, with a loading indicator and an error alert if the schema fails to load. [6]
- **S8** Each client type in the Add dialog has a "Presets" menu when presets exist and a "More Info" link. [7]
- **S9** The add/edit client dialog has Name, Enable, provider fields, Client Priority (advanced), Tags, and Completed Download Handling options (Remove Completed, Remove Failed). [8]
- **S10** A "Completed Download Handling" fieldset is shown only when advanced settings are on and settings loaded without error. It has Enable, Auto Redownload Failed, and (when that is on) the interactive search option. [9]
- **S11** The options area shows a loading indicator while fetching and an error alert if loading fails. [9]
- **S12** Remote Path Mappings are listed with Host, Remote Path and Local Path columns, an info alert with a wiki link, and an "Add Remote Path Mapping" link. [10]
- **S13** The Manage Download Clients dialog shows a table with row selection and sorting, a loading indicator, an error message, or "No download clients found" when empty. [11]
- **S14** Each manage row shows name, protocol, implementation, enabled Yes/No, priority, remove completed Yes/No, remove failed Yes/No, and tags. [12]
- **S15** The remote path mapping dialog offers Host as a select of hosts from existing download clients, a Remote Path text input and a Local Path path input. [13][14]

## Actions

- **A1** "Test All Clients" tests all download clients and shows the result per card. [1][15] → [`endpoint:Sonarr.Api.V3|POST /api/v3/DownloadClient/testall`](../backend/sonarr.api.v3-c45bd0/post-api-v3-downloadclient-testall-6d9513.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/DownloadClient/testall`](../backend/sonarr.api.v5-c79e7b/post-api-v5-downloadclient-testall-f1fde0.md)
- **A2** "Manage Clients" opens the Manage Download Clients dialog. [1]
- **A3** The Save action of the page saves the Completed Download Handling options. [1][9]
- **A4** Clicking the Add card opens the client type picker. Choosing a type (or preset) opens the add dialog. [2][7]
- **A5** Clicking a client card opens its edit dialog. The clone action on a card opens a new client dialog prefilled from it. [4][2]
- **A6** In the client dialog the user can Test, Cancel, Save, toggle advanced settings, and Delete (existing clients only). Delete asks for confirmation. [8][4]
- **A7** In the Manage dialog the user can select clients and Delete, Edit or Set Tags for the selected ones, or Close. Delete asks for confirmation. [11] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/DownloadClient/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-downloadclient-bulk-466519.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/DownloadClient/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-downloadclient-bulk-fac060.md), [`endpoint:Sonarr.Api.V3|PUT /api/v3/DownloadClient/bulk`](../backend/sonarr.api.v3-c45bd0/put-api-v3-downloadclient-bulk-492a9d.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/DownloadClient/bulk`](../backend/sonarr.api.v5-c79e7b/put-api-v5-downloadclient-bulk-68ffa7.md)
- **A8** The bulk Edit dialog can change Enabled, Priority, Remove Completed and Remove Failed for selected clients, then Apply Changes. [16]
- **A9** The Tags dialog applies chosen tags to selected clients by add, remove or replace, and previews the resulting tags. [17]
- **A10** Users can add, edit and delete remote path mappings; delete asks for confirmation. [10][18][13]

## Rules

- **R1** Client priority must be between 1 and 50, both in the single and bulk edit dialogs. [8][16]
- **R2** Priority is shown on a card only when greater than 1. [4]
- **R3** A cloned client gets a copied name and has its API key and password field values cleared. [19]
- **R4** A test-all response with status 400 and an array body is treated as results, not an error. [20]
- **R5** In Manage, Delete, Edit and Set Tags are disabled until at least one client is selected. [11]
- **R6** Bulk edit sends only fields changed from "no change", and nothing is sent if none changed. [16]
- **R7** Saving an existing client with no changes skips testing and validation. Re-saving identical settings skips testing. After a warnings-only result, warning validation is skipped on the next save. [21]
- **R8** The client dialog closes automatically after a save finishes without error. [8]

## Side effects

- **E1** Test All calls POST DownloadClient/testall. [15]
- **E2** Bulk edit sends PUT DownloadClient/bulk and merges the returned clients into the cached list. [22]
- **E3** Bulk delete sends DELETE DownloadClient/bulk and removes the deleted clients from the cached list. [23]
- **E4** Deleting a single client or remote path mapping sends a DELETE request for its id and removes it from the cached list. [24]
- **E5** Saving the completed download handling options sends a PUT and updates the cached settings. [25]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:78` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useProviderSettings.ts:403` HTTP DELETE to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 71 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/DownloadClients/DownloadClientSettings.tsx#DownloadClientSettings`
2. `ts|frontend/src/Settings/DownloadClients/DownloadClients/DownloadClients.tsx#DownloadClients`
3. `ts|frontend/src/Settings/DownloadClients/DownloadClients/useDownloadClients.ts#useSortedDownloadClients`
4. `ts|frontend/src/Settings/DownloadClients/DownloadClients/DownloadClient.tsx#DownloadClient`
5. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestStatus`
6. `ts|frontend/src/Settings/DownloadClients/DownloadClients/AddDownloadClientModalContent.tsx#AddDownloadClientModalContent`
7. `ts|frontend/src/Settings/DownloadClients/DownloadClients/AddDownloadClientItem.tsx#AddDownloadClientItem`
8. `ts|frontend/src/Settings/DownloadClients/DownloadClients/EditDownloadClientModalContent.tsx#EditDownloadClientModalContent`
9. `ts|frontend/src/Settings/DownloadClients/Options/DownloadClientOptions.tsx#DownloadClientOptions`
10. `ts|frontend/src/Settings/DownloadClients/RemotePathMappings/RemotePathMappings.tsx#RemotePathMappings`
11. `ts|frontend/src/Settings/DownloadClients/DownloadClients/Manage/ManageDownloadClientsModalContent.tsx#ManageDownloadClientsModalContentInner`
12. `ts|frontend/src/Settings/DownloadClients/DownloadClients/Manage/ManageDownloadClientsModalRow.tsx#ManageDownloadClientsModalRow`
13. `ts|frontend/src/Settings/DownloadClients/RemotePathMappings/EditRemotePathMappingModalContent.tsx#EditRemotePathMappingModalContent`
14. `ts|frontend/src/DownloadClient/useDownloadClientHostOptions.ts#useDownloadClientHostOptions`
15. `ts|frontend/src/Settings/DownloadClients/DownloadClients/useDownloadClients.ts#useTestAllDownloadClients`
16. `ts|frontend/src/Settings/DownloadClients/DownloadClients/Manage/Edit/ManageDownloadClientsEditModalContent.tsx#ManageDownloadClientsEditModalContent`
17. `ts|frontend/src/Settings/DownloadClients/DownloadClients/Manage/Tags/TagsModalContent.tsx#TagsModalContent`
18. `ts|frontend/src/Settings/DownloadClients/RemotePathMappings/RemotePathMapping.tsx#RemotePathMapping`
19. `ts|frontend/src/Settings/DownloadClients/DownloadClients/useDownloadClients.ts#useManageDownloadClient`
20. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestAllResults`
21. `ts|frontend/src/Settings/useProviderSettings.ts#useManageProviderSettings`
22. `ts|frontend/src/Settings/DownloadClients/DownloadClients/useDownloadClients.ts#useBulkEditDownloadClients`
23. `ts|frontend/src/Settings/DownloadClients/DownloadClients/useDownloadClients.ts#useBulkDeleteDownloadClients`
24. `ts|frontend/src/Settings/useProviderSettings.ts#useDeleteProvider`
25. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`

<sub>commit 0bffefe · inputs 5b9121b · slice of 474 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
