# UI Settings

`page:/settings/ui` · screen · tsconfig.json

From the code: /settings/ui · UISettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "UiSettings" (translated) with a heading "Ui" under the scope "Settings". [1]
- **S2** A loading indicator shows while data is being fetched, but only once data has been populated before. [1]
- **S3** When not fetching and loading languages or settings failed, a danger alert shows the message "UiSettingsLoadError" (translated). [1]
- **S4** The form is shown only when settings exist, both languages and settings have loaded, and there is no error. [1]
- **S5** The Calendar section has selects for First Day of Week and Week Column Header, the latter with help text. [1]
- **S6** The Dates section has selects for Short Date Format, Long Date Format, Time Format and Time Zone, and a checkbox for Show Relative Dates. [1]
- **S7** The Style section has a Theme select, a Header Color select and an Enable Color Impaired Mode checkbox, each with help text. [1]
- **S8** The Language section has a UI Language select with help text and a warning that a browser reload is required. [1]
- **S9** Each field shows its current value, pending state, and any validation errors or warnings returned from a failed save. [2][1]

## Actions

- **A1** Changing any field stores it as a pending change without saving. [1][3][4]
- **A2** Pressing save sends the current settings merged with pending changes via a PUT request. [1][3][5]

## Rules

- **R1** Theme options are Auto, Light and Dark. [1][6]
- **R2** The language list excludes the languages named Any, Original and Unknown. [1][7]
- **R3** If the selected UI language is not in the available language list, the field shows the error "InvalidUILanguage" (translated). [1]
- **R4** Only values that differ from the loaded settings count as changed and are sent on save. [3]
- **R5** Validation failures come from a 400 response; those flagged as warnings are shown as warnings and the rest as errors. [8][9]
- **R6** The language list is cached indefinitely and never considered stale. [10]

## Side effects

- **E1** The language list is fetched from the language API. [10]
- **E2** On successful save, the cached settings are replaced with the server's response and pending changes are cleared. [5][3]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- the depth limit left out 11 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/UI/UISettings.tsx#UISettings`
2. `ts|frontend/src/Utilities/selectSettings.ts#selectSettings`
3. `ts|frontend/src/Settings/useSettings.ts#useManageSettings`
4. `ts|frontend/src/Helpers/Hooks/usePendingChangesStore.ts#usePendingChangesStore`
5. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
6. `ts|frontend/src/Utilities/String/titleCase.ts#titleCase`
7. `ts|frontend/src/Language/useLanguages.ts#useFilteredLanguages`
8. `ts|frontend/src/Utilities/selectSettings.ts#getValidationFailures`
9. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#getValidationFailures`
10. `ts|frontend/src/Language/useLanguages.ts#useLanguages`

<sub>commit 0bffefe · inputs 1e5888d · slice of 144 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
