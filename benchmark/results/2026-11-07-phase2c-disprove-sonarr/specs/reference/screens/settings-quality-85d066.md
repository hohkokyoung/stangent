# Quality Settings

`page:/settings/quality` · screen · tsconfig.json

From the code: /settings/quality · Quality

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled 'QualitySettings' in the settings layout, with a heading 'Quality' under the scope 'Settings'. [1]
- **S2** A 'Quality Definitions' section shows a caption and an info notice about quality definition sizes. [2]
- **S3** The list has column headers 'Quality' and 'Title'. Each row shows the quality name and an editable text input holding the title. [2][3]
- **S4** The definitions area shows loading and error states, with the error message 'QualityDefinitionsLoadError' when loading fails. [2]
- **S5** The toolbar has a 'Reset Definitions' button that spins and is disabled while a reset command is queued or started. [1][4]

## Actions

- **A1** Editing a title in a row records a pending change for that definition. [3][5]
- **A2** Pressing save sends only the definitions with pending changes to the quality definition endpoint with a PUT. [1][5][6] → [`endpoint:Sonarr.Api.V3|ANY /api/v3/QualityDefinition`](../backend/sonarr.api.v3-c45bd0/any-api-v3-qualitydefinition-3004bc.md), [`endpoint:Sonarr.Api.V5|ANY /api/v5/QualityDefinition`](../backend/sonarr.api.v5-c79e7b/any-api-v5-qualitydefinition-399d10.md)
- **A3** Pressing 'Reset Definitions' opens a danger confirmation titled 'ResetQualityDefinitions' with a 'Reset' button. Cancelling closes it. [1]
- **A4** Confirming the reset runs the ResetQualityDefinitions command with resetTitles set to true and closes the dialog. [1][7]
- **A5** The page loads quality definitions from the quality definition endpoint. [8] → [`endpoint:Sonarr.Api.V3|GET /api/v3/QualityDefinition`](../backend/sonarr.api.v3-c45bd0/get-api-v3-qualitydefinition-a86196.md), [`endpoint:Sonarr.Api.V5|GET /api/v5/QualityDefinition`](../backend/sonarr.api.v5-c79e7b/get-api-v5-qualitydefinition-5f2a2b.md)

## Rules

- **R1** The same command with the same body run within 5 seconds of the previous one is ignored, with a console warning. [7]
- **R2** If an edited value equals the original, the pending change is removed. The page has pending changes while any definition differs. [9][5]
- **R3** The save state and the pending-changes state are passed to the settings page. [1][2]

## Side effects

- **E1** A successful save replaces the cached definitions with the server response and clears all pending changes. [6][5]
- **E2** Confirming a reset posts a command to /command and adds the returned command to the cached command list. [7]
- **E3** When a reset finishes, the cached quality definitions are invalidated and reloaded. [2]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime

## Cited symbols

1. `ts|frontend/src/Settings/Quality/Quality.tsx#Quality`
2. `ts|frontend/src/Settings/Quality/Definition/QualityDefinitions.tsx#QualityDefinitions`
3. `ts|frontend/src/Settings/Quality/Definition/QualityDefinition.tsx#QualityDefinition`
4. `ts|frontend/src/Commands/useCommands.ts#useCommandExecuting`
5. `ts|frontend/src/Settings/Quality/Definition/useQualityDefinitions.ts#useManageQualityDefinitions`
6. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
7. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
8. `ts|frontend/src/Settings/Quality/Definition/useQualityDefinitions.ts#useQualityDefinitions`
9. `ts|frontend/src/Helpers/Hooks/usePendingItemsStore.ts#usePendingItemsStore`

<sub>commit 0bffefe · inputs 63531ba · slice of 107 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
