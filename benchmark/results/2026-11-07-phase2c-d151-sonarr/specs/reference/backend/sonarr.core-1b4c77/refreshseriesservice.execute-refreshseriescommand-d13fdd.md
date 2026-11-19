# Refresh Series command

`Sonarr.Core|M:NzbDrone.Core.Tv.RefreshSeriesService.Execute(NzbDrone.Core.Tv.Commands.RefreshSeriesCommand)` · backend · Sonarr.Core

From the code: RefreshSeriesService.Execute(RefreshSeriesCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** If the series is not found on TheTVDB, the series status is set to Deleted and an error "Series '{0}' (tvdbid {1}) was not found, it may have been removed from TheTVDB." is logged. [1][2]
- **S2** A progress message "Updating {0}" with the series title is logged for each series being refreshed. [1]

## Actions

- **A1** Runs a refresh for the series ids in the command, or for all series ordered by sort title when no ids are given. [2]
- **A2** Refreshing a series fetches its info and episodes from the metadata source by TVDB id, language and season type. [1][3]

## Rules

- **R1** When no series ids are given, a series is refreshed if the trigger is Manual or the refresh check says so; otherwise it is skipped with the log "Skipping refresh of series". [2]
- **R2** The refresh check returns true when the last info sync was over 30 days ago or an aired non-special episode has the title "TBA". [4]
- **R3** The refresh check returns false if synced less than 6 hours ago. Otherwise it returns true if the series is not ended or its last episode aired within 30 days, and false if not. It returns true on error. [4]
- **R4** Rescan after refresh: a new series is always rescanned. With setting Never the rescan is skipped. With AfterManual it is skipped unless the trigger is Manual. Each skip publishes a scan-skipped event. [5]
- **R5** Newly seen seasons are monitored only if the series monitors new items as All. A new season 0 is ignored and set to unmonitored. Existing seasons keep their monitored flag. [6]
- **R6** If the metadata source returns a different TVDB id, the series is updated to the new id and a warning notes it replaced a duplicate. [1]
- **R7** If a series fails to refresh with an ID-listed run, tags are still updated and a rescan is still done, and the result is reported Indeterminate. A not-found series reports Indeterminate without tags or rescan. [2]
- **R8** A failed rescan is logged as an error and does not stop the run. [5]

## Side effects

- **E1** At the start a series-refresh-starting event is published, flagged manual when the trigger is Manual. At the end a series-refresh-complete event is published. [2][7]
- **E2** A refreshed series is saved with the updated metadata, seasons, a new last-info-sync time and a normalised path. Its translations and episodes are also updated. [1][8]
- **E3** Episode refresh deletes episodes no longer in the source, updates existing ones, inserts new ones, then publishes an episode-info-refreshed event. [9]
- **E4** After a series refresh a series-updated event is published. It is handled by the Xem service, pending releases, media covers, and the series API controllers. [1][10][11][12][13][14]
- **E5** The refresh-starting event is handled by the scene mapping and Xem services. [15][16]
- **E6** The episode-info-refreshed event is handled by the tracked download service and the episode refreshed service. [17][18]
- **E7** Auto-tagging tags are updated for each series, and the series is saved if any tag was added or removed. [19][20]
- **E8** A rescan scans the series' disk folder. A skipped scan event is handled by the series scanned handler. [5][21]
- **E9** Reporting Indeterminate sets the result on the current command. [22][23]

## Couldn't see

- the depth limit left out 125 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Tv.RefreshSeriesService.RefreshSeriesInfo(System.Int32)`
2. `Sonarr.Core|M:NzbDrone.Core.Tv.RefreshSeriesService.Execute(NzbDrone.Core.Tv.Commands.RefreshSeriesCommand)`
3. `Sonarr.Core|M:NzbDrone.Core.MetadataSource.SkyHook.SkyHookProxy.GetSeriesInfo(System.Int32,NzbDrone.Core.Languages.Language,System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Tv.ShouldRefreshSeries.ShouldRefresh(NzbDrone.Core.Tv.Series)`
5. `Sonarr.Core|M:NzbDrone.Core.Tv.RefreshSeriesService.RescanSeries(NzbDrone.Core.Tv.Series,System.Boolean,NzbDrone.Core.Messaging.Commands.CommandTrigger)`
6. `Sonarr.Core|M:NzbDrone.Core.Tv.RefreshSeriesService.UpdateSeasons(NzbDrone.Core.Tv.Series,NzbDrone.Core.Tv.Series)`
7. `Sonarr.Core|M:NzbDrone.Core.Tv.Events.SeriesRefreshStartingEvent.#ctor(System.Boolean)`
8. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesTranslationService.UpdateTranslations(System.Int32,System.Collections.Generic.List{NzbDrone.Core.Tv.SeriesTranslation})`
9. `Sonarr.Core|M:NzbDrone.Core.Tv.RefreshEpisodeService.RefreshEpisodeInfo(NzbDrone.Core.Tv.Series,System.Collections.Generic.IEnumerable{NzbDrone.Core.Tv.Episode})`
10. [`Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Xem.XemService.Handle(NzbDrone.Core.Tv.Events.SeriesUpdatedEvent)`](../../backend/sonarr.core-1b4c77/xemservice.handle-seriesupdatedevent-df8832.md)
11. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Tv.Events.SeriesUpdatedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-seriesupdatedevent-bd32e6.md)
12. [`Sonarr.Core|M:NzbDrone.Core.MediaCover.MediaCoverService.HandleAsync(NzbDrone.Core.Tv.Events.SeriesUpdatedEvent)`](../../backend/sonarr.core-1b4c77/mediacoverservice.handleasync-seriesupdatedevent-36c0b6.md)
13. [`Sonarr.Api.V3|M:Sonarr.Api.V3.Series.SeriesController.Handle(NzbDrone.Core.Tv.Events.SeriesUpdatedEvent)`](../../backend/sonarr.api.v3-c45bd0/seriescontroller.handle-seriesupdatedevent-5b897e.md)
14. [`Sonarr.Api.V5|M:Sonarr.Api.V5.Series.SeriesController.Handle(NzbDrone.Core.Tv.Events.SeriesUpdatedEvent)`](../../backend/sonarr.api.v5-c79e7b/seriescontroller.handle-seriesupdatedevent-87546b.md)
15. [`Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.SceneMappingService.Handle(NzbDrone.Core.Tv.Events.SeriesRefreshStartingEvent)`](../../backend/sonarr.core-1b4c77/scenemappingservice.handle-seriesrefreshstartingevent-cde7fc.md)
16. [`Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Xem.XemService.Handle(NzbDrone.Core.Tv.Events.SeriesRefreshStartingEvent)`](../../backend/sonarr.core-1b4c77/xemservice.handle-seriesrefreshstartingevent-66526c.md)
17. [`Sonarr.Core|M:NzbDrone.Core.Download.TrackedDownloads.TrackedDownloadService.Handle(NzbDrone.Core.Tv.Events.EpisodeInfoRefreshedEvent)`](../../backend/sonarr.core-1b4c77/trackeddownloadservice.handle-episodeinforefreshedevent-2f14b0.md)
18. [`Sonarr.Core|M:NzbDrone.Core.Tv.EpisodeRefreshedService.Handle(NzbDrone.Core.Tv.Events.EpisodeInfoRefreshedEvent)`](../../backend/sonarr.core-1b4c77/episoderefreshedservice.handle-episodeinforefreshedevent-4be91f.md)
19. `Sonarr.Core|M:NzbDrone.Core.Tv.RefreshSeriesService.UpdateTags(NzbDrone.Core.Tv.Series)`
20. `Sonarr.Core|M:NzbDrone.Core.Tv.SeriesService.UpdateAutoTaggingTags(NzbDrone.Core.Tv.Series)`
21. [`Sonarr.Core|M:NzbDrone.Core.Tv.SeriesScannedHandler.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScanSkippedEvent)`](../../backend/sonarr.core-1b4c77/seriesscannedhandler.handle-seriesscanskippedevent-254546.md)
22. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandResultReporter.Report(NzbDrone.Core.Messaging.Commands.CommandResult)`
23. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandQueueManager.SetResult(NzbDrone.Core.Messaging.Commands.CommandModel,NzbDrone.Core.Messaging.Commands.CommandResult)`

<sub>commit 0bffefe · inputs 8f17284 · slice of 192 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
