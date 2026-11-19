# Extras creation after series scan

`Sonarr.Core|M:NzbDrone.Core.Extras.ExtraService.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScannedEvent)` · backend · Sonarr.Core

From the code: ExtraService.Handle(SeriesScannedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The handler returns nothing; it hands each extra file manager the series and its episode files after a scan. [1]
- **S2** Each episode file is loaded with the episodes whose episode file id matches that file. [2]

## Actions

- **A1** When a series scan finishes, every registered extra file manager is asked to create extras for the scanned series and its episode files. [1][3]

## Rules

- **R1** Metadata creation is skipped, with an info log, when the series folder does not exist. [4]
- **R2** Metadata is produced for each enabled metadata consumer, using only the existing metadata files recorded for that consumer. [4][5]
- **R3** Series metadata is rewritten only if its content hash differs from the stored hash. If only the path differs, the record is updated without rewriting the file. [6]
- **R4** Episode metadata is written only when its content hash differs from the stored hash. [7]
- **R5** Series, season and episode images that already exist on disk are skipped. [8][9][10]
- **R6** Other-extras and subtitle managers create nothing after a series scan; they return an empty result. [11][12]

## Side effects

- **E1** Existing metadata files for the series are cleaned before new metadata is created. [4][13]
- **E2** Changed series and episode metadata files are written to disk in the series folder. [6][7]
- **E3** Missing series, season and episode images are downloaded. [8][9][10]
- **E4** An existing episode metadata or image file whose target path changed is moved to the new path. [7][10]
- **E5** The resulting metadata file records are saved with an upsert. [4][14]

## Couldn't see

- the depth limit left out 45 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Extras.ExtraService.Handle(NzbDrone.Core.MediaFiles.Events.SeriesScannedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Extras.ExtraService.GetEpisodeFiles(System.Int32)`
3. `Sonarr.Core|M:NzbDrone.Core.Extras.Files.IManageExtraFiles.CreateAfterSeriesScan(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile})`
4. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.CreateAfterSeriesScan(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile})`
5. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.GetMetadataFilesForConsumer(NzbDrone.Core.Extras.Metadata.IMetadata,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile})`
6. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.ProcessSeriesMetadata(NzbDrone.Core.Extras.Metadata.IMetadata,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile},NzbDrone.Core.Extras.Metadata.SeriesMetadataReason)`
7. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.ProcessEpisodeMetadata(NzbDrone.Core.Extras.Metadata.IMetadata,NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile})`
8. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.ProcessSeriesImages(NzbDrone.Core.Extras.Metadata.IMetadata,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile})`
9. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.ProcessSeasonImages(NzbDrone.Core.Extras.Metadata.IMetadata,NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile})`
10. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.MetadataService.ProcessEpisodeImages(NzbDrone.Core.Extras.Metadata.IMetadata,NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile,System.Collections.Generic.List{NzbDrone.Core.Extras.Metadata.Files.MetadataFile})`
11. `Sonarr.Core|M:NzbDrone.Core.Extras.Others.OtherExtraService.CreateAfterSeriesScan(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile})`
12. `Sonarr.Core|M:NzbDrone.Core.Extras.Subtitles.SubtitleService.CreateAfterSeriesScan(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.MediaFiles.EpisodeFile})`
13. `Sonarr.Core|M:NzbDrone.Core.Extras.Metadata.Files.ICleanMetadataService.Clean(NzbDrone.Core.Tv.Series)`
14. `Sonarr.Core|M:NzbDrone.Core.Extras.Files.IExtraFileService`1.Upsert(System.Collections.Generic.List{`0})`

<sub>commit 0bffefe · inputs 06d1470 · slice of 89 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
