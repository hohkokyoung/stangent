# System Tasks

`page:/system/tasks` · screen · tsconfig.json

From the code: /system/tasks · Tasks

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled Tasks with the scope System and shows two sections: Scheduled, then Queue. [1]
- **S2** The Scheduled section shows a loading indicator while tasks load, and an error message "Error loading tasks: <message>" if loading fails. [2]
- **S3** When no scheduled tasks exist and loading has finished, the Scheduled section shows "No scheduled tasks found." [2]
- **S4** Each scheduled task row shows name, interval, last execution, last duration, next execution and a Run button. [3]
- **S5** A scheduled task with interval 0 shows "disabled" as its interval and "-" as next execution. [3]
- **S6** Next execution shows "queued" if the next execution time has passed and the command is queued, "now" if passed and not queued, otherwise the next execution time. [3]
- **S7** Last duration shows "-" unless the last start time is after 2010-01-01. [3]
- **S8** The Run button spins while the task's command is queued or started. [3][4]
- **S9** The Queue section shows a loading indicator while loading, and "Error loading commands" on error; otherwise a table of queued commands. [5]
- **S10** Each queue row shows a trigger icon (manual, scheduled or default), a status icon, the command name, and queued, started and ended times ("-" if missing), duration and actions. [6][7]
- **S11** The status icon differs for queued, started (spinning), completed (success, with message), failed (danger, with message) and unknown statuses. [8]
- **S12** The command name cell lists the related series titles sorted by sort title, shows the season number if present, and shows "From: <user agent>" when a client user agent exists. [9]
- **S13** When more than 11 series titles apply, the first 10 are listed followed by "N more". [10]
- **S14** Times display as relative or formatted dates according to the UI settings, and refresh after 30 seconds. [6][7]

## Actions

- **A1** Pressing Run on a scheduled task executes the command named by that task. [3][11] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/Command`](../backend/sonarr.api.v3-c45bd0/any-api-v3-command-7feb4b.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/Command`](../backend/sonarr.api.v5-c79e7b/any-api-v5-command-bee921.md)
- **A2** For a queued command, a remove button opens a confirmation ("Cancel" / "YesCancel" / "NoLeaveIt"); confirming cancels the command. [6][12]

## Rules

- **R1** The remove button appears only when the command status is queued. [6]
- **R2** Running the same command again within 5 seconds is ignored with a console warning. [11]
- **R3** The command list is refetched at a regular interval. [13]

## Side effects

- **E1** Running a task sends a POST to the command endpoint and adds or updates the returned command in the cached command list. [11]
- **E2** Cancelling sends a DELETE for the command id and removes it from the cached command list. [12]
- **E3** The page loads scheduled tasks from the system task endpoint and commands from the command endpoint. [14][13]

## Couldn't see

- ❓ `frontend/src/Commands/useCommands.ts:101` HTTP DELETE {base}command/{*} matched no backend endpoint
- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- the depth limit left out 8 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/System/Tasks/Tasks.tsx#Tasks`
2. `ts|frontend/src/System/Tasks/Scheduled/ScheduledTasks.tsx#ScheduledTasks`
3. `ts|frontend/src/System/Tasks/Scheduled/ScheduledTaskRow.tsx#ScheduledTaskRow`
4. `ts|frontend/src/Utilities/Command/isCommandExecuting.ts#isCommandExecuting`
5. `ts|frontend/src/System/Tasks/Queued/QueuedTasks.tsx#QueuedTasks`
6. `ts|frontend/src/System/Tasks/Queued/QueuedTaskRow.tsx#QueuedTaskRow`
7. `ts|frontend/src/System/Tasks/Queued/QueuedTaskRow.tsx#getFormattedDates`
8. `ts|frontend/src/System/Tasks/Queued/QueuedTaskRow.tsx#getStatusIconProps`
9. `ts|frontend/src/System/Tasks/Queued/QueuedTaskRowNameCell.tsx#QueuedTaskRowNameCell`
10. `ts|frontend/src/System/Tasks/Queued/QueuedTaskRowNameCell.tsx#formatTitles`
11. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
12. `ts|frontend/src/Commands/useCommands.ts#useCancelCommand`
13. `ts|frontend/src/Commands/useCommands.ts#useCommands`
14. `ts|frontend/src/System/Tasks/useTasks.ts#useTasks`

<sub>commit 0bffefe · inputs 704d1da · slice of 169 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
