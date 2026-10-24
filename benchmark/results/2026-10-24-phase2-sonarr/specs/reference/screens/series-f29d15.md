# Series Index

`page:/series` · screen · tsconfig.json

From the code: /series · SeriesIndex

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Series" and lists all series in one of three views: table, posters or overview. [1][2]
- **S2** While series are first loading, a loading indicator is shown. [1]
- **S3** If loading fails, an error alert shows the message for SeriesLoadError. [1]
- **S4** When no series exist at all, the page shows a message to import or add series, with buttons "Import Existing Series" (to /add/import) and "Add New Series" (to /add/new). [3]
- **S5** When series exist but the applied filter hides them all, the page shows a message that all series are hidden by the filter. [3][1]
- **S6** A footer below the list shows a colour legend and statistics: series, ended, continuing, monitored, unmonitored, episodes, files and total file size. [4]
- **S7** The footer statistics are computed from all series, not just the filtered ones. [4]
- **S8** The toolbar has Refresh/Update, RSS Sync, select-multiple, Options, and View, Sort and Filter menus. [1]
- **S9** The View menu offers Table, Posters and Overview. [5]
- **S10** The Sort menu offers: Monitored Status, Title, Network, Original Country, Original Language, Quality Profile, Next Airing, Previous Airing, Added, Seasons, Episodes, Episode Count, Latest Season, Path, Size on Disk, Average Size per Episode, Tags and Rating. [6]
- **S11** The refresh button label is Update All, Update Selected when something is selected, or Update Filtered when a non-'all' filter is selected. [1]
- **S12** In select mode a select-all button (or Unselect All when all are selected) and a footer with Edit, Rename Files, Set Tags, Update Monitoring, Delete and Delete Files buttons appear, plus a selected-count text. [1][7][8]
- **S13** A jump bar of first characters appears on the side when sorted by title and the list is loaded. [1][9]
- **S14** The Options modal shows poster options (view posters), overview options (view overview), or table options with Show Banners and Show Search (table view). [1][10][11][12]

## Actions

- **A1** Refresh/Update runs a RefreshSeries command for the selected series (in select mode with selection) or for all currently listed series. [1]
- **A2** RSS Sync runs the RssSync command. [1]
- **A3** Choosing a view saves it as the view option and scrolls the list to top. [1]
- **A4** Choosing a sort item sets the sort key. [1]
- **A5** Choosing a filter saves it as the selected filter. [1]
- **A6** Pressing a jump bar letter scrolls the list to the first series starting with that character. [1][13]
- **A7** The select toggle enters or leaves select mode; leaving clears the selection. [1][14]
- **A8** Saving the bulk Edit modal sends changes for the selected series to the series editor endpoint. [8][15] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/series/editor`](../backend/sonarr.api.v3-c45bd0/put-api-v3-series-editor-2dc8ba.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/series/editor`](../backend/sonarr.api.v5-c79e7b/put-api-v5-series-editor-b4ba9e.md)
- **A9** Applying tags (add, remove or replace) sends the tags to the series editor endpoint for the selected series. [8][16][15] → [`endpoint:Sonarr.Api.V3|PUT /api/v3/series/editor`](../backend/sonarr.api.v3-c45bd0/put-api-v3-series-editor-2dc8ba.md), [`endpoint:Sonarr.Api.V5|PUT /api/v5/series/editor`](../backend/sonarr.api.v5-c79e7b/put-api-v5-series-editor-b4ba9e.md)
- **A10** Saving Update Monitoring posts the chosen monitor option for the selected series to the SeasonPass endpoint. [8][17] → [`endpoint:Sonarr.Api.V3|POST /api/v3/SeasonPass`](../backend/sonarr.api.v3-c45bd0/post-api-v3-seasonpass-d38909.md), [`endpoint:Sonarr.Api.V5|POST /api/v5/SeasonPass`](../backend/sonarr.api.v5-c79e7b/post-api-v5-seasonpass-71b638.md)
- **A11** Confirming Delete removes the selected series via the series editor delete endpoint, optionally deleting files and adding a list exclusion. [18][19] → [`endpoint:Sonarr.Api.V3|DELETE /api/v3/series/editor`](../backend/sonarr.api.v3-c45bd0/delete-api-v3-series-editor-7bcd3d.md), [`endpoint:Sonarr.Api.V5|DELETE /api/v5/series/editor`](../backend/sonarr.api.v5-c79e7b/delete-api-v5-series-editor-a840b3.md)
- **A12** Confirming Rename Files runs the RenameSeries command for the selected series. [20]
- **A13** Confirming Delete Files runs the DeleteSeriesFiles command for the selected series. [21]

## Rules

- **R1** Series are filtered by the selected filter (built-in or custom) and sorted by the sort key, with ascending sortTitle as secondary sort. [22][23]
- **R2** Refresh and RSS Sync buttons, Options, and the View, Sort and Filter menus are disabled when there are no series. [1]
- **R3** The jump bar is built only when sorting by sortTitle; digit-leading titles group under '#', and order is reversed for descending sort. [1]
- **R4** The jump bar is hidden when it has fewer than 5 items. [9]
- **R5** Repeating the same command within 5 seconds is ignored with a console warning. [24]
- **R6** All select-mode footer buttons are disabled when nothing is selected. Edit, Rename Files, Set Tags and Update Monitoring are also disabled while files are being renamed; Delete is also disabled while deleting; Delete Files is also disabled while its command is executing. [8]
- **R7** In the bulk Edit modal, only fields changed from 'no change' are sent; if nothing changed nothing is saved. [25]
- **R8** Changing the root folder asks whether to move the files before saving. [25][26]
- **R9** Poster view columns and table row heights depend on options (table row height is 72 with banners, 48 without). [27]

## Side effects

- **E1** Bulk edit, tags and monitoring saves update the cached series list from the response or variables. [15][17]
- **E2** A successful bulk delete removes the deleted series from the cached list, and the selection is cleared when deletion finishes without error. [19][8]
- **E3** View, sort, filter, column and table/poster/overview option changes are saved in the series options store. [1][28]

## Couldn't see

- ❓ `frontend/src/Helpers/Hooks/useApiMutation.ts:42` HTTP GET to a URL built at runtime
- the depth limit left out 219 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R6 (contradicted): Delete Files is not disabled while files are being renamed. It is disabled only when nothing is selected or when the delete-files command is executing. The same goes for Delete, which is disabled only when nothing is selected or while deleting. So 'the others while files are being renamed' is too broad. **fixed**: Rewrote the rule to list exactly which buttons are disabled under which conditions.

## Cited symbols

1. `ts|frontend/src/Series/Index/SeriesIndex.tsx#SeriesIndexBody`
2. `ts|frontend/src/Series/Index/SeriesIndex.tsx#getViewComponent`
3. `ts|frontend/src/Series/NoSeries.tsx#NoSeries`
4. `ts|frontend/src/Series/Index/SeriesIndexFooter.tsx#SeriesIndexFooter`
5. `ts|frontend/src/Series/Index/Menus/SeriesIndexViewMenu.tsx#SeriesIndexViewMenu`
6. `ts|frontend/src/Series/Index/Menus/SeriesIndexSortMenu.tsx#SeriesIndexSortMenu`
7. `ts|frontend/src/Series/Index/Select/SeriesIndexSelectAllButton.tsx#SeriesIndexSelectAllButton`
8. `ts|frontend/src/Series/Index/Select/SeriesIndexSelectFooter.tsx#SeriesIndexSelectFooter`
9. `ts|frontend/src/Components/Page/PageJumpBar.tsx#PageJumpBar`
10. `ts|frontend/src/Series/Index/Table/SeriesIndexTableOptions.tsx#SeriesIndexTableOptions`
11. `ts|frontend/src/Series/Index/Posters/Options/SeriesIndexPosterOptionsModalContent.tsx#SeriesIndexPosterOptionsModalContent`
12. `ts|frontend/src/Series/Index/Overview/Options/SeriesIndexOverviewOptionsModalContent.tsx#SeriesIndexOverviewOptionsModalContent`
13. `ts|frontend/src/Components/Page/PageJumpBarItem.tsx#PageJumpBarItem`
14. `ts|frontend/src/Series/Index/Select/SeriesIndexSelectModeButton.tsx#SeriesIndexSelectModeButton`
15. `ts|frontend/src/Series/useSeries.ts#useSaveSeriesEditor`
16. `ts|frontend/src/Series/Index/Select/Tags/TagsModalContent.tsx#TagsModalContent`
17. `ts|frontend/src/Series/useSeries.ts#useUpdateSeriesMonitor`
18. `ts|frontend/src/Series/Index/Select/Delete/DeleteSeriesModalContent.tsx#DeleteSeriesModalContent`
19. `ts|frontend/src/Series/useSeries.ts#useBulkDeleteSeries`
20. `ts|frontend/src/Series/Index/Select/Organize/OrganizeSeriesModalContent.tsx#OrganizeSeriesModalContent`
21. `ts|frontend/src/Series/Index/Select/Delete/Files/DeleteSeriesFilesModalContent.tsx#DeleteSeriesFilesModalContent`
22. `ts|frontend/src/Series/useSeries.ts#useSeriesIndex`
23. `ts|frontend/src/Utilities/Filter/clientSideFilterAndSort.ts#sort`
24. `ts|frontend/src/Commands/useCommands.ts#useExecuteCommand`
25. `ts|frontend/src/Series/Index/Select/Edit/EditSeriesModalContent.tsx#EditSeriesModalContent`
26. `ts|frontend/src/Series/MoveSeries/MoveSeriesModal.tsx#MoveSeriesModal`
27. `ts|frontend/src/Series/Index/Table/SeriesIndexTable.tsx#SeriesIndexTable`
28. `ts|frontend/src/Series/seriesOptionsStore.ts#setSeriesTableOptions`

<sub>commit 0bffefe · inputs 36b3cbf · slice of 780 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
