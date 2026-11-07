# Import list deleted: library clean-up

`Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{NzbDrone.Core.ImportLists.IImportList})` · backend · Sonarr.Core

From the code: ImportListSyncService.HandleAsync(IImportList})

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** When an import list is deleted, series in the library that are not on any import list are logged, unmonitored or tagged, depending on the list sync level. [1][2]

## Actions

- **A1** Handling a deleted import list event triggers an attempt to clean the library. [1][3]

## Rules

- **R1** Nothing happens if the list sync level is Disabled. [3][2]
- **R2** The clean only runs if every list with automatic add enabled (including blocked ones) has no disabled-till value and has been synced at least once, and at least one of them has removed an item since the last clean. [4][5]
- **R3** A series counts as on a list if any list item matches its TVDB id, its TMDB id, its IMDb id (when both are non-blank), one of its MAL ids or one of its AniList ids. [6][7]
- **R4** With LogOnly, a series not on any list is only logged as one to consider unmonitoring or removing. [2]
- **R5** With KeepAndUnmonitor, a monitored series not on any list is set to unmonitored. [2]
- **R6** With KeepAndTag, a series not on any list that lacks the list sync tag gets that tag added. [2]

## Side effects

- **E1** Changed series are saved without moving files. [2][8]
- **E2** After cleaning, every import list status has its removed-item-since-last-clean flag reset to false and is saved. [9][2]
- **E3** Info log messages are written for each series that is not found in the lists and is affected by the sync level. [2]

## Couldn't see

- the depth limit left out 23 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.HandleAsync(NzbDrone.Core.ThingiProvider.Events.ProviderDeletedEvent{NzbDrone.Core.ImportLists.IImportList})`
2. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.CleanLibrary`
3. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.TryCleanLibrary`
4. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.AllListsSuccessfulWithAPendingClean`
5. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListFactory.AutomaticAddEnabled(System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListSyncService.IsSeriesOnList(NzbDrone.Core.Tv.Series,System.Collections.Generic.IEnumerable{NzbDrone.Core.Parser.Model.ImportListItemInfo})`
7. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
8. `Sonarr.Core|M:NzbDrone.Core.Tv.ISeriesService.UpdateSeries(System.Collections.Generic.List{NzbDrone.Core.Tv.Series},System.Boolean)`
9. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListStatusService.MarkListsAsCleaned`

<sub>commit 0bffefe · inputs 4627522 · slice of 69 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
