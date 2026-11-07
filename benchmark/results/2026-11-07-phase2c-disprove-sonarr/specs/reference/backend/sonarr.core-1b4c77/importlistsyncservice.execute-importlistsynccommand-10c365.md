# Import List Sync (command handler)

`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.Execute(NzbDrone.Core.ImportLists.ImportListSyncCommand)` · backend · Sonarr.Core

From the code: ImportListSyncService.Execute(ImportListSyncCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Logs progress messages such as "Starting Import List Sync", "Starting Import List Refresh for List {name}", "Processing {n} list items" and "Import List Sync Completed. Items found: {n}, Series added: {n}". [1][2][3]
- **S2** Logs "No list items to process" when the fetched list has no items. [3]

## Actions

- **A1** When the command carries a definition id, syncs only that import list; otherwise syncs all lists with automatic add enabled. [4]
- **A2** A full sync fetches items from all automatic-add lists, processes them, removes synced tags from series no longer on lists, then tries to clean the library. [1]
- **A3** A single-list sync fetches that list, processes its items, removes synced tags, then tries to clean the library. [2]

## Rules

- **R1** A full sync does nothing (logs "No import lists with automatic add enabled") if no list has automatic add enabled. [1]
- **R2** Lists blocked due to recent failures are skipped when fetching all lists. [5][6]
- **R3** A list is skipped in a full fetch if its last sync plus its minimum refresh interval is still in the future. [7]
- **R4** Items from lists that do not have automatic add enabled are ignored. [3]
- **R5** An item without a TVDB id is mapped by IMDb id, then TMDb id, then AniList id, then MAL id. Items with an AniList or MAL id that can't be mapped are rejected. [3]
- **R6** Items still without a TVDB id (0) are rejected. [3]
- **R7** Items whose TVDB id is in the list exclusions are rejected. [3]
- **R8** Series already in the library are not added again. Their tags are queued for update only if the list's tag-existing setting is not None and the list has tags. [3][8]
- **R9** A new series takes its settings from the list: root folder, quality profile, series type, language, season folder, tags, monitor-new-items and search-for-missing-episodes. It is monitored unless the list's monitor type is None. [3]
- **R10** If an item provides seasons, the add monitor option is Skip; otherwise the list's monitor setting is used. [3]
- **R11** If a series is already queued to add from another list, that list's tags are added to the queued series instead of adding it twice. [3]
- **R12** A tag is removed from a series only if its list has tag-existing type Sync, every automatic-add list with that tag synced successfully (not disabled and synced before), and the series is on none of those lists. [9][10]
- **R13** A series matches a list item by TVDB id, IMDb id, TMDb id, MAL id or AniList id. [11]
- **R14** Library cleaning is skipped if the list sync level is Disabled. Otherwise it runs only when every automatic-add list has synced without being disabled and at least one has removed items since the last clean. [12][13]
- **R15** For library series not on any list: LogOnly logs a message; KeepAndUnmonitor unmonitors monitored series; KeepAndTag adds the configured list-sync tag if missing. [14]
- **R16** A list's items are only recorded and its status updated when its fetch had no failure. [7][15]

## Side effects

- **E1** Adds new series to the library via the add-series service, ignoring errors. [3][16]
- **E2** Adds list tags to existing series and saves the changed tags. [17][18]
- **E3** Saving series tags publishes a SeriesBulkEditedEvent, handled by the V3 and V5 series controllers and the tracked download service. [18][19][20][21]
- **E4** Removes Sync tags from series no longer on their lists and saves the series tags. [9]
- **E5** Fetching stores each successfully fetched list's items and updates the list's sync status, including whether items were removed. [7][15]
- **E6** Library cleaning saves unmonitored or tagged series without moving files, then marks the lists as cleaned. [14]
- **E7** Looks up TVDB ids through the metadata source by IMDb, TMDb, AniList or MAL id. [22][23][24][25]

## Couldn't see

- the depth limit left out 50 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.SyncAll`
2. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.SyncList(NzbDrone.Core.ImportLists.ImportListDefinition)`
3. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.ProcessListItems(System.Collections.Generic.List{NzbDrone.Core.Parser.Model.ImportListItemInfo})`
4. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.Execute(NzbDrone.Core.ImportLists.ImportListSyncCommand)`
5. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListFactory.AutomaticAddEnabled(System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListFactory.FilterBlockedImportLists(System.Collections.Generic.IEnumerable{NzbDrone.Core.ImportLists.IImportList})`
7. `Sonarr.Core|M:NzbDrone.Core.ImportLists.FetchAndParseImportListService.Fetch`
8. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.QueueTagsOnPendingSeries(NzbDrone.Core.ImportLists.ImportListDefinition,System.Collections.Generic.Dictionary{System.Int32,System.Collections.Generic.HashSet{System.Int32}},System.Int32)`
9. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.RemoveTagsFromSeriesNotOnLists`
10. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.HasSyncedSuccessfully(System.Int32)`
11. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.IsSeriesOnList(NzbDrone.Core.Tv.Series,System.Collections.Generic.IEnumerable{NzbDrone.Core.Parser.Model.ImportListItemInfo})`
12. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.TryCleanLibrary`
13. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.AllListsSuccessfulWithAPendingClean`
14. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.CleanLibrary`
15. `Sonarr.Core|M:NzbDrone.Core.ImportLists.FetchAndParseImportListService.FetchSingleList(NzbDrone.Core.ImportLists.ImportListDefinition)`
16. `Sonarr.Core|M:NzbDrone.Core.Tv.IAddSeriesService.AddSeries(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean)`
17. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.UpdateTagsOnPendingSeries(System.Collections.Generic.Dictionary{System.Int32,System.Collections.Generic.HashSet{System.Int32}})`
18. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.UpdateTags(System.Collections.Generic.List{NzbDrone.Core.Tv.Series})`
19. [`Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.Handle(NzbDrone.Core.Tv.Events.SeriesBulkEditedEvent)`](../../backend/sonarr.api.v3-c45bd0/seriescontroller.handle-seriesbulkeditedevent-597ebe.md)
20. [`Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.Handle(NzbDrone.Core.Tv.Events.SeriesBulkEditedEvent)`](../../backend/sonarr.api.v5-c79e7b/seriescontroller.handle-seriesbulkeditedevent-6b4a8f.md)
21. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.SeriesBulkEditedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-seriesbulkeditedevent-943037.md)
22. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.SearchForNewSeriesByImdbId(System.String,NzbDrone.Core.Languages.Language)`
23. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.SearchForNewSeriesByTmdbId(System.Int32,NzbDrone.Core.Languages.Language)`
24. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.SearchForNewSeriesByAniListId(System.Int32,NzbDrone.Core.Languages.Language)`
25. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.SearchForNewSeriesByMyAnimeListId(System.Int32,NzbDrone.Core.Languages.Language)`

<sub>commit 0bffefe · inputs 9de723d · slice of 153 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
