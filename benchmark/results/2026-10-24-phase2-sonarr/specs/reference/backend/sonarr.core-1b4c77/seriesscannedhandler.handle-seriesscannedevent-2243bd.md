# Series scanned: post-add actions and search

`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesScannedHandler.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScannedEvent)` · backend · Sonarr.Core

From the code: SeriesScannedHandler.Handle(SeriesScannedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a series scan completes, the handler runs the post-scan logic for the scanned series. [1][2]
- **A2** If the series has no add options, it runs the episode-refreshed search for the series and stops. [2][3]
- **A3** If the series has add options, it logs that the series was recently added and sets episode monitored status from those options. [2][4]

## Rules

- **R1** If both search-for-missing and search-for-cutoff-unmet are enabled, one series search is queued for the whole series instead of separate searches. [2][5]
- **R2** If only search-for-missing is enabled, a missing episode search is queued for the series, limited to monitored episodes. [2][6]
- **R3** If only search-for-cutoff-unmet is enabled, a cutoff-unmet episode search is queued for the series, limited to monitored episodes. [2][7]
- **R4** If the monitoring options are null, only the series is updated and the episodes are left unchanged. [4]
- **R5** If the monitor type is Skip, episode-level monitoring is skipped and season information is used. [4]
- **R6** The latest season (not specials) is monitored when the monitor type is All, or Future for a Continuing or Upcoming series. [4]
- **R7** With the Pilot monitor type, the first season is not monitored. Other seasons are monitored only if they have monitored episodes. [4]
- **R8** In the no-add-options search, only previously aired episodes for the series that still have no file are searched. [3]

## Side effects

- **E1** After queueing searches, the series add options are cleared and saved to the series record. [2][8]
- **E2** A series-add-completed event is published, which the notification service handles. [2][9]
- **E3** Setting monitored status updates the series' season monitored flags and the episodes' monitored state, then saves the series. [4]
- **E4** The series search command is handled by the series search service. [10]
- **E5** The missing and cutoff-unmet commands are handled by the episode search service. [11][12]
- **E6** When missing episodes are found, an episode search command is queued for them, and the series' cached entry is then removed. [3][13]

## Couldn't see

- the depth limit left out 15 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesScannedHandler.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScannedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesScannedHandler.HandleScanEvents(NzbDrone.Core.Tv.Series)`
3. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeRefreshedService.Search(NzbDrone.Core.Tv.Series)`
4. `Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeMonitoredService.SetEpisodeMonitoredStatus(NzbDrone.Core.Tv.Series,NzbDrone.Core.Tv.MonitoringOptions)`
5. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.SeriesSearchCommand.#ctor(System.Int32)`
6. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.MissingEpisodeSearchCommand.#ctor(System.Int32)`
7. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.CutoffUnmetEpisodeSearchCommand.#ctor(System.Int32)`
8. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.RemoveAddOptions(NzbDrone.Core.Tv.Series)`
9. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Tv.Events.SeriesAddCompletedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-seriesaddcompletedevent-f21532.md)
10. [`Sonarr.Core|M:NzbDrone.Core.IndexerSearch.SeriesSearchService.Execute(NzbDrone.Core.IndexerSearch.SeriesSearchCommand)`](../../backend/sonarr.core-1b4c77/seriessearchservice.execute-seriessearchcommand-f1cad9.md)
11. [`Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.Execute(NzbDrone.Core.IndexerSearch.MissingEpisodeSearchCommand)`](../../backend/sonarr.core-1b4c77/episodesearchservice.execute-missingepisodesearchcommand-748c1f.md)
12. [`Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.Execute(NzbDrone.Core.IndexerSearch.CutoffUnmetEpisodeSearchCommand)`](../../backend/sonarr.core-1b4c77/episodesearchservice.execute-cutoffunmetepisodesearchcommand-e5b993.md)
13. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchCommand.#ctor(System.Collections.Generic.List{System.Int32})`

<sub>commit 0bffefe · inputs b32421b · slice of 67 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
