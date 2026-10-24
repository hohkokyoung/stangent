# Get download queue (GET /api/v3/Queue)

`endpoint:Sonarr.Api.V3|GET /api/v3/Queue` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Queue

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a paged list of queue items with page, page size, sort key, sort direction, total records and the records. [1]
- **S2** Each queue record shows ids (queue item, series, episode), season number, languages, quality, custom formats, custom format score, size, title, estimated completion time, added time, status, tracked download status and state, status messages, error message, download id, protocol, download client, indexer, output path and whether the episode has a file. [2]
- **S3** Each record also carries the obsolete sizeleft and timeleft values. [2]
- **S4** Series details are included in a record only when includeSeries is true and the item has a series; episode details only when includeEpisode is true and the item has an episode. [2]
- **S5** The custom format score is the sum of profile scores for the item's custom formats, or 0 if there is no series quality profile. [2][3]
- **S6** The list combines the current download queue with pending releases. [4]

## Actions

- **A1** A caller requests the queue with optional paging, includeUnknownSeriesItems, includeSeries, includeEpisode, seriesIds, protocol, languages, quality and status parameters. [5] → `endpoint:Sonarr.Api.V3|GET /api/v3/Queue`

## Rules

- **R1** Defaults: page 1, page size 10, sort direction descending when none is given. [6]
- **R2** Allowed sort keys are added, downloadClient, episode, episode.airDateUtc, episode.title, episodes.airDateUtc, episodes.title, estimatedCompletionTime, indexer, language, languages, progress, protocol, quality, series.sortTitle, size, status, timeleft and title (case-insensitive). [5]
- **R3** If the sort key is missing or not allowed, it defaults to timeleft; a default sort direction becomes ascending. [5][7]
- **R4** Queue items without a series are excluded unless includeUnknownSeriesItems is true; includeUnknownSeriesItems, includeSeries and includeEpisode default to false. [4][5]
- **R5** Items can be filtered by series ids, protocol, languages (any match), quality id and status; all given filters must match. [4]
- **R6** Ties in sorting are broken by download progress, highest first. [4]
- **R7** If the requested page is empty and greater than 1, the page is reset to the last page (minimum 1) and records are re-read. [4][8]
- **R8** Pending releases other than fallback ones are listed per episode, and only the best quality release per episode is kept. Pending releases with no matching episode are dropped from the result. [9]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 158 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on R8 (contradicted): Pending releases without matching episodes get the error message but have a null episode. They are then dropped by the dedupe step, so they never appear in the returned list. **fixed**: Removed the error message claim; the statement now says pending releases without a matching episode are dropped by the dedupe step.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.Extensions.RequestExtensions.ApplyToPage``2(NzbDrone.Core.Datastore.PagingSpec{``1},System.Func{NzbDrone.Core.Datastore.PagingSpec{``1},NzbDrone.Core.Datastore.PagingSpec{``1}},System.Converter{``1,``0})`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueResourceMapper.ToResource(NzbDrone.Core.Queue.Queue,System.Boolean,System.Boolean)`
3. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfile.CalculateCustomFormatScore(System.Collections.Generic.List{NzbDrone.Core.CustomFormats.CustomFormat})`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueController.GetQueue(NzbDrone.Core.Datastore.PagingSpec{NzbDrone.Core.Queue.Queue},System.Collections.Generic.HashSet{System.Int32},System.Nullable{NzbDrone.Core.Indexers.DownloadProtocol},System.Collections.Generic.HashSet{System.Int32},System.Collections.Generic.HashSet{System.Int32},System.Collections.Generic.HashSet{NzbDrone.Core.Queue.QueueStatus},System.Boolean)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Queue.QueueController.GetQueue(Sonarr.Http.PagingRequestResource,System.Boolean,System.Boolean,System.Boolean,System.Int32[],System.Nullable{NzbDrone.Core.Indexers.DownloadProtocol},System.Int32[],System.Int32[],NzbDrone.Core.Queue.QueueStatus[])`
6. `Sonarr.Http|M:Sonarr.Http.PagingResource`1.#ctor(Sonarr.Http.PagingRequestResource)`
7. `Sonarr.Http|M:Sonarr.Http.PagingResourceMapper.MapToPagingSpec``2(Sonarr.Http.PagingResource{``0},System.Collections.Generic.HashSet{System.String},System.String,NzbDrone.Core.Datastore.SortDirection)`
8. `Sonarr.Common|M:NzbDrone.Common.Extensions.EnumerableExtensions.Empty``1(System.Collections.Generic.IEnumerable{``0})`
9. `Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.GetPendingQueueObsolete`

<sub>commit 0bffefe · inputs 6fd22bc · slice of 142 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
