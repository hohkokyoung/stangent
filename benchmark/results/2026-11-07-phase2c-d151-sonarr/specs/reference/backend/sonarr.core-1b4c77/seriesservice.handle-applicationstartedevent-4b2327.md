# Clear stale pending paths on application start

`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.Handle(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)` · backend · Sonarr.Core

From the code: SeriesService.Handle(ApplicationStartedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When the application has started, the series service checks for series with a leftover pending path and clears it. This handler runs last among the handlers for this event. [1][2]

## Rules

- **R1** Only series whose pending path is not null, empty or whitespace are considered. [2][3]
- **R2** If no series has a pending path, nothing further happens. [2][4]
- **R3** A series is a pending-move series if a queued or started command is a single series move or a bulk series move that includes its id. Commands with other statuses are ignored. [5][6]
- **R4** A series with a pending path is stale, and gets cleared, only when no queued or started move command covers it. If no series is stale, nothing is changed. [2][5]
- **R5** Saving fails with an error if any series being updated has an Id of 0. [7]

## Side effects

- **E1** For each stale series, a warning is logged saying it has a stale PendingPath left over from an interrupted move and is being cleared. [2]
- **E2** The pending path of each stale series is set to null and saved. Only the pending path field is updated, in one transaction for all stale series. [2][7][8]
- **E3** After the save, an Updated model event is published for each updated series. [7][9]

## Couldn't see

- the depth limit left out 16 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.Handle(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.ClearStalePendingPaths`
3. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNotNullOrWhiteSpace(System.String)`
4. `Sonarr.Common|M:NzbDrone.Common.Extensions.EnumerableExtensions.Empty``1(System.Collections.Generic.IEnumerable{``0})`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.GetSeriesIdsWithPendingMove`
6. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.IManageCommandQueue.All`
7. `Sonarr.Core|M:NzbDrone.Core.Datastore.BasicRepository`1.SetFields(System.Collections.Generic.IList{`0},System.Linq.Expressions.Expression{System.Func{`0,System.Object}}[])`
8. `Sonarr.Core|M:NzbDrone.Core.Datastore.BasicRepository`1.UpdateFields(System.Data.IDbConnection,System.Data.IDbTransaction,System.Collections.Generic.IList{`0},System.Collections.Generic.List{System.Reflection.PropertyInfo})`
9. `Sonarr.Core|M:NzbDrone.Core.Datastore.BasicRepository`1.ModelUpdated(`0,System.Boolean)`

<sub>commit 0bffefe · inputs 6b1d441 · slice of 56 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
