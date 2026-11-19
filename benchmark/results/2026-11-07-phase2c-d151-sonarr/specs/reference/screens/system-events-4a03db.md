# System Events (Logs)

`page:/system/events` · screen · tsconfig.json

From the code: /system/events · LogsTable

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Logs" with the heading "System" / "Events". [1]
- **S2** While the first load is in progress a loading indicator is shown. [1]
- **S3** When loaded without error and there are no records, an info alert shows the message for "NoEventsFound". [1]
- **S4** When loaded without error and records exist, a table of log events is shown with a pager below it showing page, total pages and total records. [1]
- **S5** Each row can show level (as an icon with the level as title), time (relative date), logger and message columns; hidden columns are not rendered. [2]
- **S6** The level icon depends on level: trace, debug and info show an info icon; warn a danger icon; error a bug icon; fatal a fatal icon; anything else an unknown icon. [2]
- **S7** The toolbar has Refresh, Clear and Options buttons and a filter menu. The Refresh icon spins while fetching, and the Clear icon spins while the clear-log command is queued or started. [1][3]
- **S8** The row details dialog is titled "Details" and shows the Message, plus the Exception when one exists, each in a horizontally scrolling area, with a Close button. [4]

## Actions

- **A1** Refresh goes to page 1 and refetches the events. [1]
- **A2** Clear runs the ClearLog command, then goes to page 1 and refetches. [1][5]
- **A3** Choosing a filter in the filter menu sets the selected filter, which changes the events query. [1][6]
- **A4** Clicking a column header sets the sort key and direction. [1]
- **A5** Options opens a table options modal where the page size can be changed; columns cannot be modified. Changing page size returns to page 1. [1]
- **A6** Clicking a row opens a details dialog for that event's message and exception. [2]
- **A7** The pager selects another page of events. [1][7] → [`endpoint:Sonarr.Api.V3|GET /api/v3/Log`](../backend/sonarr.api.v3-c45bd0/get-api-v3-log-b918e1.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/Log`](../backend/sonarr.api.v5-c79e7b/get-api-v5-log-e2bfe4.md)

## Rules

- **R1** Events are fetched from the /log API with page, page size, sort key, sort direction and the selected filter's filters as query parameters. [6][8]
- **R2** Total pages is the total records divided by page size, rounded up, with a minimum of 1. [8]
- **R3** The events page number defaults to 1 and resets to 1 on a back/forward (POP) navigation. [9][7]
- **R4** Previous results stay displayed while new data loads. [6]
- **R5** Running the same command again within 5 seconds is ignored with a console warning. [5]
- **R6** The details dialog shows the Exception section only when an exception exists. [4]

## Side effects

- **E1** Clear sends a POST to /command to run the ClearLog command, and stores the returned command in the command cache. [5][1]
- **E2** Requests are sent with the API key and client headers. [8]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:79` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Utilities/Fetch/fetchJson.ts:56` HTTP GET to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/System/Events/LogsTable.tsx#LogsTable`
2. `ts|frontend/src/System/Events/LogsTableRow.tsx#LogsTableRow`
3. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
4. `ts|frontend/src/System/Events/LogsTableDetailsModal.tsx#LogsTableDetailsModal`
5. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
6. `ts|frontend/src/System/Events/useEvents.ts#useEvents`
7. `ts|frontend/src/Helpers/Hooks/usePage.ts#usePage`
8. `ts|frontend/src/Helpers/Hooks/usePagedApiQuery.ts#usePagedApiQuery`
9. `ts|frontend/src/Helpers/Hooks/usePage.ts#pageStore`

<sub>commit 0bffefe · inputs cf93cb1 · slice of 179 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
