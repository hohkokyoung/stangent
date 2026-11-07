# Metadata Source Settings

`page:/settings/metadatasource` · screen · tsconfig.json

From the code: /settings/metadatasource · MetadataSourceSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Metadata Source" under the "Settings" scope. [1]
- **S2** A loading indicator is shown while settings are being fetched and have not yet been fetched. [1]
- **S3** When fetching has finished with an error, a danger alert shows the message for loading the metadata source settings failing. [1]
- **S4** Once settings are fetched without error, an "Options" group is shown with a "Preferred Metadata Language" language select, with help text and no "Any" option. [1]
- **S5** A "Source" group shows TheTVDB logo (chosen by the current theme: dark or light), a "TheTvdb" heading and a description saying series and episode information is provided by TheTVDB, with a link to the TheTVDB subscribe page. [2][1]
- **S6** Validation errors and warnings from a failed save are passed to the form and attached to the matching setting by property name. [1][3][4]

## Actions

- **A1** Changing the preferred metadata language stores it as a pending change without saving it. [1][5][6]
- **A2** Pressing save sends the loaded settings merged with the pending changes to the server with a PUT. [1][5][7]

## Rules

- **R1** Only values that differ from the loaded settings count as changed. [5]
- **R2** Validation failures are read only from a 400 response, and those flagged as warnings are separated from errors. [8][9]
- **R3** The theme used for the TheTVDB logo comes from the UI settings, falling back to the default theme; "auto" follows the system dark/light preference. [10]

## Side effects

- **E1** Saving sends a PUT of the settings to the server, including the API key header. [7][11]
- **E2** After a successful save, the cached settings are replaced with the server's response and the pending changes are cleared. [7][5]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 11 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/MetadataSource/MetadataSourceSettings.tsx#MetadataSourceSettings`
2. `ts|frontend/src/Settings/MetadataSource/TheTvdb.tsx#TheTvdb`
3. `ts|frontend/src/Utilities/selectSettings.ts#selectSettings`
4. `ts|frontend/src/Utilities/selectSettings.ts#getFailures`
5. `ts|frontend/src/Settings/useSettings.ts#useManageSettings`
6. `ts|frontend/src/Helpers/Hooks/usePendingChangesStore.ts#usePendingChangesStore`
7. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
8. `ts|frontend/src/Utilities/selectSettings.ts#getValidationFailures`
9. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#getValidationFailures`
10. `ts|frontend/src/Helpers/Hooks/useTheme.ts#useTheme`
11. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#useApiMutation`

<sub>commit 0bffefe · inputs 2673dca · slice of 94 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
