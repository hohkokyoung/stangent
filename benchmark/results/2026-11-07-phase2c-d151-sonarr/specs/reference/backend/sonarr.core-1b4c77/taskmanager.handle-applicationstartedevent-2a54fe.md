# Scheduled task initialisation on application start

`Sonarr.Core|M:NzbDrone.Core.Jobs.TaskManager.Handle(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)` · backend · Sonarr.Core

From the code: TaskManager.Handle(ApplicationStartedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When the application starts, the task manager builds the default list of scheduled tasks and synchronises it with the stored scheduled tasks. [1]

## Rules

- **R1** Default tasks and intervals in minutes: RefreshMonitoredDownloads 1 (high priority); MessagingCleanup 5; ClearExpiredCache 5; ImportListSync 5; ApplicationUpdateCheck 360; UpdateSceneMapping 180; CheckHealth 360; RefreshSeries 720; Housekeeping 1440; CleanUpRecycleBin 1440. [1]
- **R2** The Backup task interval comes from the configured backup interval in days, limited to between 1 and 7 days, and converted to minutes (days × 60 × 24). [2]
- **R3** The RSS sync interval is the configured value. Values from 1 to 9 become 10, and negative values become 0. [3]
- **R4** A scheduled task has Low priority unless one is set. Only RefreshMonitoredDownloads is set to High. [4][1]
- **R5** A stored task that has no matching default task type is deleted. [1][5]
- **R6** For a stored task that matches a default, the stored record is kept. Its interval and priority are overwritten with the default values. [1]
- **R7** A default task with no stored record is treated as new. Its last execution time is set to the current UTC time. [1]

## Side effects

- **E1** Existing scheduled tasks are read from the repository. [6]
- **E2** Stored tasks that are no longer defaults are deleted from the database. [5]
- **E3** Each default task is saved to the database with an upsert. [7]
- **E4** Each task is also stored in the cache under its type name. [8]

## Couldn't see

- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Jobs.TaskManager.Handle(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Jobs.TaskManager.GetBackupInterval`
3. `Sonarr.Core|M:NzbDrone.Core.Jobs.TaskManager.GetRssSyncInterval`
4. `Sonarr.Core|M:NzbDrone.Core.Jobs.ScheduledTask.#ctor`
5. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Delete(System.Int32)`
6. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.All`
7. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Upsert(`0)`
8. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached`1.Set(System.String,`0,System.Nullable{System.TimeSpan})`

<sub>commit 0bffefe · inputs 5a6a60d · slice of 38 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
