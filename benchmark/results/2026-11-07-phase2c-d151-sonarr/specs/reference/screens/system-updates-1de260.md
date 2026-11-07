# System Updates

`page:/system/updates` · screen · tsconfig.json

From the code: /system/updates · Updates

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Updates" with the heading scope "System". [1]
- **S2** A loading indicator shows until both the updates list and the update settings have been fetched, unless an error occurred. [1]
- **S3** When there are no updates, an info alert shows "NoUpdatesAreAvailable" text. [1]
- **S4** Each update shows its version, its release date in the short date format (with the long date and time as a tooltip), and a branch label when the branch is not 'main'. [1][2][3]
- **S5** The update matching the current version gets a "Currently Installed" label; other updates with an installed-on date get a "Previously Installed" label. Both labels have the install date and time as a tooltip. [1]
- **S6** If an update has changes, its "New" and "Fixed" change lists are shown, each rendered as markdown list items and hidden when empty. Otherwise "MaintenanceRelease" text is shown. [1][4]
- **S7** When updates exist but none is both installable and latest, an up-to-date check icon with the "OnLatestVersion" message is shown. [1]
- **S8** When fetching updates or settings, a small loading indicator appears beside the install or up-to-date message. [1]
- **S9** If fetching updates fails, a warning alert "FailedToFetchUpdates" is shown. If fetching settings fails, a danger alert "FailedToFetchSettings" is shown. [1]

## Actions

- **A1** Pressing "Install Latest" runs the application update command; if the latest version is a higher major version, a confirmation modal opens first. [1][5]
- **A2** Confirming "Install" in the major version modal closes it and runs the application update command with installMajorUpdate set to true. [1]
- **A3** Cancelling the major version modal closes it without installing. [1]
- **A4** The page loads the updates list from /update and the update settings from /settings/update. [6][7] → [`endpoint:Sonarr.Api.V3|GET /api/v3/Update`](../backend/sonarr.api.v3-c45bd0/get-api-v3-update-19f57e.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/Update`](../backend/sonarr.api.v5-c79e7b/get-api-v5-update-1c6adb.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/settings/update`](../backend/sonarr.api.v5-c79e7b/get-api-v5-settings-update-3dec22.md)

## Rules

- **R1** The "Install Latest" button shows only when some update is both installable and latest, and the update mechanism is 'builtIn' or 'script'. [1]
- **R2** For other update mechanisms with an update to install, a warning icon and a message explain that updates must be done externally. The message is the system's package update mechanism message, or a text for external, apt or docker, defaulting to external. [1]
- **R3** The update mechanism defaults to 'builtIn' when update settings are unavailable. [1]
- **R4** An update is a major update when the major version number of the first (latest) listed update is greater than that of the current version. [1]
- **R5** The install button spins while an application update command is queued or started. [1][8]
- **R6** The same command is not re-sent if run again within 5 seconds. [5]
- **R7** Dates display empty when no date is given. [2][3]

## Side effects

- **E1** Installing sends a POST to /command with the application update command and adds the returned command to the cached command list. [5]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/System/Updates/Updates.tsx#Updates`
2. `ts|frontend/src/Utilities/Date/formatDate.ts#formatDate`
3. `ts|frontend/src/Utilities/Date/formatDateTime.ts#formatDateTime`
4. `ts|frontend/src/System/Updates/UpdateChanges.tsx#UpdateChanges`
5. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
6. `ts|frontend/src/System/Updates/useUpdates.ts#useUpdates`
7. `ts|frontend/src/Settings/General/useUpdateSettings.ts#useUpdateSettings`
8. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`

<sub>commit 0bffefe · inputs 631c6db · slice of 127 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
