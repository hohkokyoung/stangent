# Blocklist release on download failure

`Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)` · backend · Sonarr.Core

From the code: BlocklistService.Handle(DownloadFailedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

Nothing found in the code.

## Rules

- **R1** The published date is required: it is parsed from the event data key "publishedDate" and the handler fails if it is missing or invalid. [1]
- **R2** Size defaults to 0 when the event data has no "size" value. [1]
- **R3** The torrent info hash is the tracked download's download id when the tracked download uses the torrent protocol. Otherwise it comes from the event data key "torrentInfoHash". [1]
- **R4** Indexer flags are set only when the event data "indexerFlags" value parses (case-insensitive) as an indexer flags value. Release type is set only when "releaseType" parses as a release type. [1]
- **R5** The blocklist date is the current UTC time. [1]

## Side effects

- **E1** On a download failed event, a blocklist entry is built from the event's series, episodes, source title, quality, message, source, languages and data (indexer, protocol, size, published date), then inserted into the blocklist repository. [1][2]

## Couldn't see

- the depth limit left out 2 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Blocklisting.BlocklistService.Handle(NzbDrone.Core.Download.DownloadFailedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`

<sub>commit 0bffefe · inputs f4042aa · slice of 64 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
