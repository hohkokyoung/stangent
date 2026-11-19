# Episode search command handler

`Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.Execute(NzbDrone.Core.IndexerSearch.EpisodeSearchCommand)` · backend · Sonarr.Core

From the code: EpisodeSearchService.Execute(EpisodeSearchCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** After each episode is searched, a progress message is logged: "Episode search completed. {0} reports downloaded." with the number of grabbed releases. [1][2]

## Actions

- **A1** Runs an episode search for every episode id in the command, one at a time, then processes the resulting download decisions for each. [1][3]

## Rules

- **R1** The search is flagged as user-invoked only when the command trigger is Manual; it is never an interactive search. [1]
- **R2** A Daily series episode with no air date fails with SearchFailedException "Air date is missing"; otherwise it gets a daily search by air date. [4][5][6]
- **R3** For Anime series, a season 0 episode with no scene absolute or absolute number gets a special search; other anime episodes get an anime search. [4][7][8]
- **R4** For other series, season 0 episodes get a special search and all remaining episodes get a single-episode search. [4][9]
- **R5** Searches started from this command are not limited to monitored episodes (monitoredOnly is false). [4]
- **R6** Search results are de-duplicated before being returned. [9]
- **R7** Within processing, rejected decisions are set aside; releases are prioritized, and an episode already grabbed is skipped. [10]
- **R8** Temporarily rejected releases, and releases whose protocol (Usenet or Torrent) already failed in this run, are made pending instead of being grabbed. [10]

## Side effects

- **E1** Releases that qualify are grabbed, and pending releases are added to the pending release service in one batch. [10]
- **E2** Searches query the indexers for each episode; a missing air date on a daily episode is logged as an error. [9][4]

## Couldn't see

- the depth limit left out 54 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.Execute(NzbDrone.Core.IndexerSearch.EpisodeSearchCommand)`
2. `Sonarr.Common|M:NzbDrone.Common.Instrumentation.Extensions.LoggerExtensions.ProgressInfo(NLog.Logger,System.String,System.Object[])`
3. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.EpisodeSearch(System.Int32,System.Boolean,System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.EpisodeSearch(NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean)`
5. `Sonarr.Core|M:NzbDrone.Core.Exceptions.SearchFailedException.#ctor(System.String)`
6. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.SearchDaily(NzbDrone.Core.Tv.Series,NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean,System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.SearchSpecial(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean,System.Boolean)`
8. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.SearchAnime(NzbDrone.Core.Tv.Series,NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean,System.Boolean,System.Boolean)`
9. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.SearchSingle(NzbDrone.Core.Tv.Series,NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean,System.Boolean)`
10. `Sonarr.Core|M:NzbDrone.Core.Download.ProcessDownloadDecisions.ProcessDecisions(System.Collections.Generic.List{NzbDrone.Core.DecisionEngine.DownloadDecision})`

<sub>commit 0bffefe · inputs e6b05b4 · slice of 52 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
