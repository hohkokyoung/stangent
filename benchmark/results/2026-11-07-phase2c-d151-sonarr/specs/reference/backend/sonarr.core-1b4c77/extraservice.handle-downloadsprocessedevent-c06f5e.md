# Create extra files after downloads processed

`Sonarr.Core|M:NzbDrone.Core.Extras.ExtraService.Handle(NzbDrone.Core.Download.DownloadsProcessedEvent)` · backend · Sonarr.Core

From the code: ExtraService.Handle(DownloadsProcessedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When downloads have been processed, every series that had files imported since the last run is handed to each extra file manager to create extra files after episodes were imported. [1][2]

## Rules

- **R1** The list of series with imported files is copied and then cleared under a lock, so each series is processed only once per event. [1]
- **R2** If the series folder does not exist, metadata creation is skipped and no files are returned. [3][4]
- **R3** Metadata is generated only for enabled metadata consumers, using only the existing metadata files that belong to that consumer. [3][5]
- **R4** A series metadata file is written only when its content hash differs from the stored hash. If only the relative path changed, the record is updated without rewriting the file. If nothing changed, nothing is returned. [6]
- **R5** A consumer that returns no series metadata produces no file. [6][7]
- **R6** The other-extras and subtitle managers create nothing after episodes are imported; they return empty results. [8][9]

## Side effects

- **E1** For each series, existing metadata files are cleaned before new metadata is created. [3][10]
- **E2** Changed series metadata is written to disk under the series path at the consumer's relative path. [6]
- **E3** The new or changed metadata file records are saved with an upsert. [3][11]

## Couldn't see

- the depth limit left out 27 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Extras.ExtraService.Handle(NzbDrone.Core.Download.DownloadsProcessedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Extras.Files.IManageExtraFiles.CreateAfterEpisodesImported(NzbDrone.Core.Tv.Series)`
3. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.CreateAfterEpisodesImported(NzbDrone.Core.Tv.Series)`
4. `Sonarr.Common|M:NzbDrone.Common.Disk.IDiskProvider.FolderExists(System.String)`
5. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.GetMetadataFilesForConsumer(NzbDrone.Core.Extras.Metadata.IMetadata,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile})`
6. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.ProcessSeriesMetadata(NzbDrone.Core.Extras.Metadata.IMetadata,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile},NzbDrone.Core.Extras.Metadata.SeriesMetadataReason)`
7. `Sonarr.Common|M:NzbDrone.Common.Extensions.EnumerableExtensions.AddIfNotNull``1(System.Collections.Generic.List{``0},``0)`
8. `Sonarr.Core|M:NzbDrone.Core.Extras.Others.OtherExtraService.CreateAfterEpisodesImported(NzbDrone.Core.Tv.Series)`
9. `Sonarr.Core|M:NzbDrone.Core.Extras.Subtitles.SubtitleService.CreateAfterEpisodesImported(NzbDrone.Core.Tv.Series)`
10. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.Files.ICleanMetadataService.Clean(NzbDrone.Core.Tv.Series)`
11. `Sonarr.Core|M:NzbDrone.Core.Extras.Files.IExtraFileService`1.Upsert(System.Collections.Generic.List{`0})`

<sub>commit 0bffefe · inputs 8a09082 · slice of 78 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
