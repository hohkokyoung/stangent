# General Settings

`page:/settings/general` · screen · tsconfig.json

From the code: /settings/general · GeneralSettings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "General" under the "Settings" scope and contains sections for Host, Security, Proxy, Logging, Analytics, Updates and Backups. [1]
- **S2** While settings are loading for the first time, a loading indicator is shown. [1]
- **S3** If loading fails, an error alert shows the general settings load error message and the form is hidden. [1]
- **S4** The Host section has Bind Address, Port Number, Url Base, Allowed Hosts, Instance Name, Application URL and Enable SSL fields. [2]
- **S5** SSL Port, SSL Cert Path, SSL Key Path and SSL Cert Password are shown only when Enable SSL is checked. [2]
- **S6** The Open Browser On Start checkbox is shown only on Windows when the app is not running as a service. [2]
- **S7** The Security section has Authentication, API Key, Certificate Validation and Trusted Networks fields. Authentication Required is shown only when the authentication method is not 'none'. [3]
- **S8** With the 'forms' authentication method, Username, Password and Password Confirmation fields are shown. [4][5]
- **S9** With the 'oidc' authentication method, Authority, Client Id, Client Secret, User Identifier and Scopes fields are shown. [4][6]
- **S10** The API key field is read-only, with a copy button and a reset button; the reset icon spins while a reset is running. [3]
- **S11** The Proxy section shows a Use Proxy checkbox. Type, Hostname, Port, Username, Password, Ignored Addresses and Bypass Proxy For Local Addresses appear only when it is checked. [7]
- **S12** Proxy type options are HTTP(S), Socks4 and Socks5. [7]
- **S13** The Logging section has a Log Level select, with a warning shown when the level is 'trace', and a Log Size Limit in MB. [8]
- **S14** The Analytics section has a checkbox to send anonymous usage data, with a restart-required warning. [9]
- **S15** The Updates section is shown only when advanced settings are shown. It has Branch, Automatic, Mechanism and, for the 'script' mechanism, Script Path. [10]
- **S16** The Mechanism options are the external package mechanism (title-cased) or Built-in, plus Script. [10]
- **S17** The Backups section is shown only when advanced settings are shown. It has Folder, Interval (days) and Retention (days). [11]

## Actions

- **A1** Editing any field stores a pending change; the page is marked as having pending changes until saved. [1][12]
- **A2** Pressing save sends the settings with the changed values merged in to the server and clears pending changes on success. [12][13]
- **A3** Pressing the API key reset button opens a confirmation (Reset API Key); confirming runs the reset API key command. [3][14]
- **A4** In the restart-required dialog, Restart Now triggers a server restart and Restart Later closes the dialog. [1][15]

## Rules

- **R1** Port Number, SSL Port and Proxy Port accept values from 1 to 65535. [2][7]
- **R2** Log Size Limit accepts values from 1 to 10 MB. [8]
- **R3** After a successful save that changed a restart-requiring setting, a restart-required dialog opens. [1]
- **R4** The restart dialog adds a Windows service note when the app runs as a Windows service. [1][16]
- **R5** When an API key reset finishes, the browser page reloads once saving is not in progress. [1]
- **R6** The same command is not run again within 5 seconds; the repeat is ignored. [14]
- **R7** Save validation errors and warnings (HTTP 400) are matched to fields by property name and shown on the form. [17][18]
- **R8** The Branch field is read-only when an external update mechanism is used. [10]

## Side effects

- **E1** Saving sends a PUT of the settings to the general settings API path and updates the cached settings with the response. [13]
- **E2** Confirming restart sends POST /system/restart, then marks the app as restarting and starts pinging the server until it responds. [15][19]
- **E3** Resetting the API key posts a command to /command and adds it to the cached command list. [14]

## Couldn't see

- ❓ `frontend/src/App/appStore.ts:122` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:12` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Settings/useSettings.ts:29` HTTP PUT to a URL built at runtime
- ❓ `frontend/src/Utilities/requestAction.js:37` HTTP GET to a URL built at runtime
- the depth limit left out 15 more point(s) one line further on

## Cited symbols

1. `ts|frontend/src/Settings/General/GeneralSettings.tsx#GeneralSettings`
2. `ts|frontend/src/Settings/General/HostSettings.tsx#HostSettings`
3. `ts|frontend/src/Settings/General/SecuritySettings.tsx#SecuritySettings`
4. `ts|frontend/src/Settings/General/AuthenticationMethodSettings.tsx#AuthenticationMethodSettings`
5. `ts|frontend/src/Settings/General/FormsAuthenticationSettings.tsx#FormsAuthenticationSettings`
6. `ts|frontend/src/Settings/General/OidcAuthenticationSettings.tsx#OidcAuthenticationSettings`
7. `ts|frontend/src/Settings/General/ProxySettings.tsx#ProxySettings`
8. `ts|frontend/src/Settings/General/LoggingSettings.tsx#LoggingSettings`
9. `ts|frontend/src/Settings/General/AnalyticSettings.tsx#AnalyticSettings`
10. `ts|frontend/src/Settings/General/UpdateSettings.tsx#UpdateSettings`
11. `ts|frontend/src/Settings/General/BackupSettings.tsx#BackupSettings`
12. `ts|frontend/src/Settings/useSettings.ts#useManageSettings`
13. `ts|frontend/src/Settings/useSettings.ts#useSaveSettings`
14. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
15. `ts|frontend/src/System/useSystem.ts#useRestart`
16. `ts|frontend/src/System/Status/useSystemStatus.ts#useIsWindowsService`
17. `ts|frontend/src/Utilities/selectSettings.ts#selectSettings`
18. `ts|frontend/src/Utilities/selectSettings.ts#getFailures`
19. `ts|frontend/src/App/appStore.ts#pingServerAfterTimeout`

<sub>commit 0bffefe · inputs 0ec23d9 · slice of 337 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
