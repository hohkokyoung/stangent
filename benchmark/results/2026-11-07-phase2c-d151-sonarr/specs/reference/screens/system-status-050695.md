# System Status

`page:/system/status` · screen · tsconfig.json

From the code: /system/status · Status

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled Status under the System heading and has four sections: Health, Disk Space, About and More Info. [1]
- **S2** The Health section shows a loading indicator while loading, and next to the legend while fetching for the first time. [2]
- **S3** When health data has loaded and there are no issues, the Health section shows 'No issues with your configuration'. [2]
- **S4** When there are health issues, they are listed in a table with an icon for the type, the message, and action buttons. An info alert about health messages with a link to /system/logs/files appears below the table. [2]
- **S5** Each health row's icon is red/danger for type 'error', info for 'notice', and warning for 'warning' or any other type. The icon tooltip is the title-cased type. [2][3]
- **S6** Each health row has a Settings or other shortcut button depending on the source: indexer checks go to /settings/indexers, download client and import mechanism checks to /settings/downloadclients, notification status to /settings/connect, root folder check to /serieseditor (Series Editor), update check to /system/updates (Updates). Other sources show no shortcut. [4]
- **S7** The Disk Space section shows a loading indicator while fetching, otherwise a table of disks with path (plus label in parentheses when present), free space, total space and a usage progress bar. [5][6]
- **S8** The About section lists Version, Package Version, .NET version, Docker, Database (title-cased type and version), Database Migration, App Data Directory, Startup Directory, Mode and Uptime. [7][3]
- **S9** Uptime is shown as a time span (days plus hh:mm:ss) that updates every second; hovering shows the formatted start date and time including seconds. [8][9][10]
- **S10** The More Info section lists links: Home page sonarr.tv, Wiki wiki.servarr.com/sonarr, Forums forums.sonarr.tv, Twitter @sonarrtv, Discord discord.sonarr.tv, IRC (irc.libera.chat/#sonarr and web chat), Donations sonarr.tv/donate, Source github.com/Sonarr/Sonarr, and Feature requests (forums and GitHub issues). [11]

## Actions

- **A1** The page loads health issues from the health API. [12] → [`endpoint:Sonarr.Api.V5|GET /api/v5/Health`](../backend/sonarr.api.v5-c79e7b/get-api-v5-health-d80553.md)
- **A2** The page loads disk space information from the diskspace API. [13] → [`endpoint:Sonarr.Api.V5|GET /api/v5/diskspace`](../backend/sonarr.api.v5-c79e7b/get-api-v5-diskspace-7ca197.md)
- **A3** The page loads system status for the About section, and refetches it when the section is shown. [7][14] → [`endpoint:Sonarr.Api.V5|GET /api/v5/System/status`](../backend/sonarr.api.v5-c79e7b/get-api-v5-system-status-788691.md)
- **A4** For indexer-related health issues, a 'Test All' button runs the test-all call for indexers and spins while it runs. [2][15] → [`endpoint:Sonarr.Api.V5|POST /api/v5/Indexer/testall`](../backend/sonarr.api.v5-c79e7b/post-api-v5-indexer-testall-e7a0ae.md)
- **A5** For download-client-related health issues, a 'Test All' button runs the test-all call for download clients and spins while it runs. [2][16] → [`endpoint:Sonarr.Api.V5|POST /api/v5/DownloadClient/testall`](../backend/sonarr.api.v5-c79e7b/post-api-v5-downloadclient-testall-f1fde0.md)
- **A6** Each health row has a wiki button ('Read the Wiki for more information') linking to the item's wiki URL. [2]

## Rules

- **R1** Disk usage is 100 minus free/total as a percentage; the bar is danger above 90%, warning above 80%, otherwise primary. [5]
- **R2** Test All for indexers appears only for sources IndexerStatusCheck and IndexerLongTermStatusCheck; for download clients only for DownloadClientCheck and DownloadClientStatusCheck. [2]
- **R3** Package Version is shown only when present, using package author text when an author exists. The .NET version row shows only for .NET Core, and Docker 'Yes' only when containerized. [7]
- **R4** A test-all response of status 400 with an array body is treated as test results rather than an error. [17]
- **R5** API requests send the API key and a Sonarr client header. [18][19]

## Side effects

- **E1** Pressing Test All sends a POST to the indexer or download client testall endpoint. [15][16]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- ❓ `frontend/src/Helpers/Hooks/useApiQuery.ts:44` HTTP GET to a URL built at runtime

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on E1 (unsupported): The slice shows only the POST to the testall path. The endpoint code isn't in the slice, so nothing shows that it tests all configured providers. **fixed**: Removed the claim about what the endpoint does; the statement now only says a POST is sent to the testall endpoint.

## Cited symbols

1. `ts|frontend/src/System/Status/Status.tsx#Status`
2. `ts|frontend/src/System/Status/Health/Health.tsx#Health`
3. `ts|frontend/src/Utilities/String/titleCase.ts#titleCase`
4. `ts|frontend/src/System/Status/Health/HealthItemLink.tsx#HealthItemLink`
5. `ts|frontend/src/System/Status/DiskSpace/DiskSpace.tsx#DiskSpace`
6. `ts|frontend/src/Utilities/Number/formatBytes.ts#formatBytes`
7. `ts|frontend/src/System/Status/About/About.tsx#About`
8. `ts|frontend/src/System/Status/About/StartTime.tsx#StartTime`
9. `ts|frontend/src/Utilities/Date/formatTimeSpan.ts#formatTimeSpan`
10. `ts|frontend/src/Utilities/Date/formatDateTime.ts#formatDateTime`
11. `ts|frontend/src/System/Status/MoreInfo/MoreInfo.tsx#MoreInfo`
12. `ts|frontend/src/System/Status/Health/useHealth.ts#useHealth`
13. `ts|frontend/src/System/Status/DiskSpace/useDiskSpace.ts#useDiskSpace`
14. `ts|frontend/src/System/Status/useSystemStatus.ts#useSystemStatus`
15. `ts|frontend/src/Settings/Indexers/useIndexers.ts#useTestAllIndexers`
16. `ts|frontend/src/Settings/DownloadClients/DownloadClients/useDownloadClients.ts#useTestAllDownloadClients`
17. `ts|frontend/src/Settings/ProviderTestAllResult.ts#getProviderTestAllResults`
18. `ts|frontend/src/Helpers/Hooks/useApiQuery.ts#useApiQuery`
19. `ts|frontend/src/Helpers/Hooks/useApiMutation.ts#useApiMutation`

<sub>commit 0bffefe · inputs 9cd364d · slice of 187 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
