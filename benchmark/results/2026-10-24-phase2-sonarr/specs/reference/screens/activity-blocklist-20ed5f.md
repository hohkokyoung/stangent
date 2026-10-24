# Blocklist

`page:/activity/blocklist` · screen · tsconfig.json

From the code: /activity/blocklist · Blocklist

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled Blocklist, with the heading scope Activity. [1]
- **S2** A loading indicator shows while the first load is in progress. [1]
- **S3** If loading fails, a danger alert shows the blocklist load error message. [1]
- **S4** When there are no records, a message shows: the 'no blocklist items' text if the selected filter is 'all', otherwise the 'filter has no items' text. [1]
- **S5** When records exist, they are shown in a table with a select-all checkbox and a pager showing page, total pages and total records. [1]
- **S6** Each row can show columns: series title (link), source title, languages, quality, custom formats, date, indexer and actions; only visible columns are rendered. [2]
- **S7** The toolbar has Remove Selected, Clear, Options buttons and a filter menu with custom filters of type blocklist. [1][3]
- **S8** The Details modal shows Name and Protocol, plus Indexer and Message when a message exists, and Source when a source exists. [4]

## Actions

- **A1** Remove Selected opens a confirmation; on confirm it deletes the selected items in bulk. [1][5] → [`endpoint:Sonarr.Api.V5|DELETE /api/v5/Blocklist/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-blocklist-bulk-5f0c8c.md), [`endpoint:Sonarr.Api.V3|DELETE /api/v3/Blocklist/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-blocklist-bulk-c74ce8.md)
- **A2** Clear opens a confirmation; on confirm it runs the ClearBlocklist command, then returns to page 1 and refetches. [1]
- **A3** The remove icon on a row removes that single item from the blocklist. [2][6] → [`endpoint:Sonarr.Api.V5|DELETE /api/v5/Blocklist/bulk`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-blocklist-bulk-5f0c8c.md), [`endpoint:Sonarr.Api.V3|DELETE /api/v3/Blocklist/bulk`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-blocklist-bulk-c74ce8.md)
- **A4** The info icon on a row opens the Details modal, which has a Close button. [2][4]
- **A5** Users can pick a filter, build a custom filter, change sort, change table options and page size, and go to another page. [1][7]

## Rules

- **R1** Remove Selected is disabled when nothing is selected. [1]
- **R2** Clear is disabled when there are no records. [1]
- **R3** Changing the page size resets to page 1. [1]
- **R4** A row is not rendered if its series is not found. [2]
- **R5** Total pages is at least 1, computed as total records divided by page size, rounded up. [8]
- **R6** The list is requested from /blocklist with page, page size, sort key, sort direction and the selected filters, and keeps the previous data while refetching. [9][8]
- **R7** The same command cannot be run again within 5 seconds. [10]
- **R8** Going back in browser history resets the blocklist page to 1. [11]

## Side effects

- **E1** After a successful removal, the cached blocklist queries are invalidated so the list reloads. [5][6]
- **E2** Clear posts a ClearBlocklist command to the command endpoint. [10][1]
- **E3** The page registers a repopulate function that refetches the list, and unregisters it on leaving. [1][12][13]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/Activity/Blocklist/Blocklist.tsx#BlocklistContent`
2. `ts|frontend/src/Activity/Blocklist/BlocklistRow.tsx#BlocklistRow`
3. `ts|frontend/src/Filters/useCustomFilters.ts#useCustomFiltersList`
4. `ts|frontend/src/Activity/Blocklist/BlocklistDetailsModal.tsx#BlocklistDetailsModal`
5. `ts|frontend/src/Activity/Blocklist/useBlocklist.ts#useRemoveBlocklistItems`
6. `ts|frontend/src/Activity/Blocklist/useBlocklist.ts#useRemoveBlocklistItem`
7. `ts|frontend/src/Activity/Blocklist/BlocklistFilterModal.tsx#BlocklistFilterModal`
8. `ts|frontend/src/Helpers/Hooks/usePagedApiQuery.ts#usePagedApiQuery`
9. `ts|frontend/src/Activity/Blocklist/useBlocklist.ts#useBlocklist`
10. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
11. `ts|frontend/src/Helpers/Hooks/usePage.ts#usePage`
12. `ts|frontend/src/Utilities/pagePopulator.ts#registerPagePopulator`
13. `ts|frontend/src/Utilities/pagePopulator.ts#unregisterPagePopulator`

<sub>commit 0bffefe · inputs 654c02b · slice of 274 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
