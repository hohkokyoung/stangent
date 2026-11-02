### Is there an existing issue for this?

- [x] I have searched the existing open and closed issues

### Current Behavior

was trying to figure out why sonarr fails to get generate metadata files, and found this error

```
System.InvalidOperationException: Sequence contains more than one element
   at System.Linq.ThrowHelper.ThrowMoreThanOneElementException()
   at System.Linq.Enumerable.TryGetSingle[TSource](IEnumerable`1 source, Boolean& found)
   at System.Linq.Enumerable.SingleOrDefault[TSource](IEnumerable`1 source)
   at NzbDrone.Core.Extras.Others.OtherExtraFileRenamer.RemoveOtherExtraFile(Series series, String path) in ./Sonarr.Core/Extras/Others/OtherExtraFileRenamer.cs:line 69
   at NzbDrone.Core.Extras.Others.OtherExtraFileRenamer.RenameOtherExtraFile(Series series, String path) in ./Sonarr.Core/Extras/Others/OtherExtraFileRenamer.cs:line 38
   at NzbDrone.Core.Extras.Metadata.MetadataService.ProcessEpisodeMetadata(IMetadata consumer, Series series, EpisodeFile episodeFile, List`1 existingMetadataFiles)
   at NzbDrone.Core.Extras.Metadata.MetadataService.CreateAfterSeriesScan(Series series, List`1 episodeFiles) in ./Sonarr.Core/Extras/Metadata/MetadataService.cs:line 108
   at NzbDrone.Core.Extras.ExtraService.Handle(SeriesScannedEvent message) in ./Sonarr.Core/Extras/ExtraService.cs:line 138
   at NzbDrone.Core.Messaging.Events.EventAggregator.PublishEvent[TEvent](TEvent event)
```

### Expected Behavior

metadata is generated

### Steps To Reproduce

happens when i click on a series and click "Refresh and Scan", however its selective on what fails and what doesn't

### Environment

```markdown
- OS: archlinux
- Sonarr: sonarr-bin-4.0.14.2939-1
- Docker Install: No
- Using Reverse Proxy: (not that i know of)
- Browser: Firefox
- Database: Sqlite 3.49.1
```

### What branch are you running?

Main

### Trace Logs?

```
[v4.0.14.2939] System.InvalidOperationException: Sequence contains more than one element
   at System.Linq.ThrowHelper.ThrowMoreThanOneElementException()
   at System.Linq.Enumerable.TryGetSingle[TSource](IEnumerable`1 source, Boolean& found)
   at System.Linq.Enumerable.SingleOrDefault[TSource](IEnumerable`1 source)
   at NzbDrone.Core.Extras.Others.OtherExtraFileRenamer.RemoveOtherExtraFile(Series series, String path) in ./Sonarr.Core/Extras/Others/OtherExtraFileRenamer.cs:line 69
   at NzbDrone.Core.Extras.Others.OtherExtraFileRenamer.RenameOtherExtraFile(Series series, String path) in ./Sonarr.Core/Extras/Others/OtherExtraFileRenamer.cs:line 38
   at NzbDrone.Core.Extras.Metadata.MetadataService.ProcessEpisodeMetadata(IMetadata consumer, Series series, EpisodeFile episodeFile, List`1 existingMetadataFiles)
   at NzbDrone.Core.Extras.Metadata.MetadataService.CreateAfterSeriesScan(Series series, List`1 episodeFiles) in ./Sonarr.Core/Extras/Metadata/MetadataService.cs:line 108
   at NzbDrone.Core.Extras.ExtraService.Handle(SeriesScannedEvent message) in ./Sonarr.Core/Extras/ExtraService.cs:line 138
   at NzbDrone.Core.Messaging.Events.EventAggregator.PublishEvent[TEvent](TEvent event)
```

### Anything else?

honestly first thing i thought when metadata failed to generate was a permissions issue, so i went to the logs to confirm, and saw this instead
