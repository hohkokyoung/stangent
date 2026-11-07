# Missing Episode Search (backend command)

`Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.Execute(NzbDrone.Core.IndexerSearch.MissingEpisodeSearchCommand)` · backend · Sonarr.Core

From the code: EpisodeSearchService.Execute(MissingEpisodeSearchCommand)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns nothing; it runs a search for missing episodes and logs progress: "Performing search for {0} episodes" at the start and "Completed search for {0} episodes. {1} reports downloaded." at the end. [1]

## Actions

- **A1** Runs a missing-episode search for the command: it selects missing episodes, drops those already in the download queue, and searches indexers for the rest. [2]
- **A2** Groups the episodes by series and season. A group with more than one episode gets a season search; a group with one episode gets an episode search. [1]

## Rules

- **R1** When the command has a series id, episodes come from that series. They must have the command's monitored value, have no file, have an air date, and have aired (air date is now or earlier). [2][3]
- **R2** Without a series id, episodes without files are loaded across all series, specials included, sorted by Id ascending in a single page of 1,000,000. [2]
- **R3** If monitored is true, only episodes that are monitored and whose series is monitored are included. Otherwise, episodes that are unmonitored or belong to an unmonitored series are included. [2]
- **R4** Optional filters narrow the episodes by series ids, quality profile ids and series type when given. Series tags are passed to the missing-episode query. [2]
- **R5** Episodes already in the download queue are excluded from the search. [2][4]
- **R6** The search counts as user-invoked only when the command trigger is Manual. [2]
- **R7** Groups are searched in order of their oldest last-search time, with never-searched episodes first. [1]
- **R8** If a search for a group fails, the error is logged and that group is skipped. The remaining groups continue. [1]
- **R9** A daily series episode with no air date fails the search with "Air date is missing". [5]
- **R10** Indexers used are those enabled for automatic search (or interactive search, if interactive). Only untagged indexers, or ones sharing a tag with the series, are used. [6]
- **R11** Duplicate releases are removed by guid, keeping the one with the fewest rejections and then the better indexer priority. [7]

## Side effects

- **E1** After each group's search, the resulting decisions are processed for download, and the number of grabbed releases is added to the reported total. [1][8]
- **E2** When at least one indexer is searched, the last search time of the searched episodes is set to now and saved. [6]
- **E3** Releases that are delayed or can't be sent because the download client is unavailable are added to the pending releases. [8]
- **E4** Each search calls the Fetch method of every selected indexer. [6]

## Couldn't see

- the depth limit left out 90 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on E4 (unsupported): The slice shows indexers being called through IIndexer.Fetch. It doesn't show that this goes over the network, because the indexer implementations are outside the slice. **fixed**: Replaced the network claim with a statement that each selected indexer's Fetch is called, which the slice shows.

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.SearchForBulkEpisodes(System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},System.Boolean,System.Boolean)`
2. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.Execute(NzbDrone.Core.IndexerSearch.MissingEpisodeSearchCommand)`
3. `Sonarr.Common|M:NzbDrone.Common.Extensions.DateTimeExtensions.Before(System.DateTime,System.DateTime)`
4. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.EpisodeSearchService.GetQueuedEpisodeIds`
5. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.EpisodeSearch(NzbDrone.Core.Tv.Episode,System.Boolean,System.Boolean)`
6. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.Dispatch(System.Func{NzbDrone.Core.Indexers.IIndexer,System.Threading.Tasks.Task{System.Collections.Generic.IList{NzbDrone.Core.Parser.Model.ReleaseInfo}}},NzbDrone.Core.IndexerSearch.Definitions.SearchCriteriaBase)`
7. `Sonarr.Core|M:NzbDrone.Core.IndexerSearch.ReleaseSearchService.DeDupeDecisions(System.Collections.Generic.List{NzbDrone.Core.DecisionEngine.DownloadDecision})`
8. `Sonarr.Core|M:NzbDrone.Core.Download.ProcessDownloadDecisions.ProcessDecisions(System.Collections.Generic.List{NzbDrone.Core.DecisionEngine.DownloadDecision})`

<sub>commit 0bffefe · inputs fb06207 · slice of 137 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
