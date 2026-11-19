# SONB-26: Local air date compared against UTC mtime causing updates every refresh

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Common/Extensions/DateTimeExtensions.cs b/src/NzbDrone.Common/Extensions/DateTimeExtensions.cs
index 38ff663ae4..1061b5680a 100644
--- a/src/NzbDrone.Common/Extensions/DateTimeExtensions.cs
+++ b/src/NzbDrone.Common/Extensions/DateTimeExtensions.cs
@@ -4,6 +4,8 @@ namespace NzbDrone.Common.Extensions
 {
     public static class DateTimeExtensions
     {
+        public static readonly DateTime EpochTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
+
         public static bool InNextDays(this DateTime dateTime, int days)
         {
             return InNext(dateTime, new TimeSpan(days, 0, 0, 0));
@@ -43,5 +45,10 @@ public static DateTime WithoutTicks(this DateTime dateTime)
         {
             return dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerSecond));
         }
+
+        public static DateTime WithTicksFrom(this DateTime dateTime, DateTime other)
+        {
+            return dateTime.WithoutTicks().AddTicks(other.Ticks % TimeSpan.TicksPerSecond);
+        }
     }
 }
diff --git a/src/NzbDrone.Core.Test/MediaFiles/UpdateEpisodeFileServiceTests/ChangeFileDateForFileFixture.cs b/src/NzbDrone.Core.Test/MediaFiles/UpdateEpisodeFileServiceTests/ChangeFileDateForFileFixture.cs
new file mode 100644
index 0000000000..67b174455d
--- /dev/null
+++ b/src/NzbDrone.Core.Test/MediaFiles/UpdateEpisodeFileServiceTests/ChangeFileDateForFileFixture.cs
@@ -0,0 +1,118 @@
+using System;
+using System.Collections.Generic;
+using System.IO;
+using System.Linq;
+using FizzWare.NBuilder;
+using FluentAssertions;
+using Moq;
+using NUnit.Framework;
+using NzbDrone.Common.Disk;
+using NzbDrone.Common.Extensions;
+using NzbDrone.Core.Configuration;
+using NzbDrone.Core.MediaFiles;
+using NzbDrone.Core.Test.Framework;
+using NzbDrone.Core.Tv;
+using NzbDrone.Test.Common;
+
+namespace NzbDrone.Core.Test.MediaFiles.UpdateEpisodeFileServiceTests
+{
+    [TestFixture]
+    public class ChangeFileDateForFileFixture : CoreTest<UpdateEpisodeFileService>
+    {
+        private readonly DateTime _veryOldAirDateUtc = new(1965, 01, 01, 0, 0, 0, 512, 512, DateTimeKind.Utc);
+        private DateTime _lastWrite = new(2025, 07, 27, 12, 0, 0, 512, 512, DateTimeKind.Utc);
+        private Series _series;
+        private EpisodeFile _episodeFile;
+        private string _seriesFolder;
+        private List<Episode> _episodes;
+
+        [SetUp]
+        public void Setup()
+        {
+            _seriesFolder = @"C:\Test\TV\Series Title".AsOsAgnostic();
+
+            _series = Builder<Series>.CreateNew()
+                                     .With(s => s.Path = _seriesFolder)
+                                     .Build();
+
+            _episodes = Builder<Episode>.CreateListOfSize(1)
+                                        .All()
+                                        .With(e => e.AirDateUtc = _lastWrite.AddDays(2))
+                                        .Build()
+                                        .ToList();
+
+            _episodeFile = Builder<EpisodeFile>.CreateNew()
+                                               .With(f => f.Path = Path.Combine(_series.Path, "Season 1", "Series Title - S01E01.mkv").AsOsAgnostic())
+                                               .With(f => f.RelativePath = @"Season 1\Series Title - S01E01.mkv".AsOsAgnostic())
+                                               .Build();
+
+            Mocker.GetMock<IDiskProvider>()
+                .Setup(x => x.FileGetLastWrite(_episodeFile.Path))
+                .Returns(() => _lastWrite);
+
+            Mocker.GetMock<IDiskProvider>()
+                .Setup(x => x.FileSetLastWriteTime(_episodeFile.Path, It.IsAny<DateTime>()))
+                .Callback<string, DateTime>((path, dateTime) =>
+                {
+                    _lastWrite = dateTime.Kind == DateTimeKind.Utc
+                        ? dateTime
+                        : dateTime.ToUniversalTime();
+                });
+
+            Mocker.GetMock<IConfigService>()
+                .Setup(x => x.FileDate)
+                .Returns(FileDateType.LocalAirDate);
+        }
+
+        [Test]
+        public void should_change_date_once_only()
+        {
+            var previousWrite = new DateTime(_lastWrite.Ticks, _lastWrite.Kind);
+
+            Subject.ChangeFileDateForFile(_episodeFile, _series, _episodes);
+            Subject.ChangeFileDateForFile(_episodeFile, _series, _episodes);
+
+            Mocker.GetMock<IDiskProvider>()
+                .Verify(v => v.FileSetLastWriteTime(_episodeFile.Path, It.IsAny<DateTime>()), Times.Once());
+
+            var actualWriteTime = Mocker.GetMock<IDiskProvider>().Object.FileGetLastWrite(_episodeFile.Path).ToLocalTime();
+            actualWriteTime.Should().Be(_episodes[0].AirDateUtc.Value.ToLocalTime().WithTicksFrom(previousWrite));
+        }
+
+        [Test]
+        public void should_clamp_mtime_on_posix()
+        {
+            PosixOnly();
+
+            var previousWrite = new DateTime(_lastWrite.Ticks, _lastWrite.Kind);
+            _episodes[0].AirDateUtc = _veryOldAirDateUtc;
+
+            Subject.ChangeFileDateForFile(_episodeFile, _series, _episodes);
+            Subject.ChangeFileDateForFile(_episodeFile, _series, _episodes);
+
+            Mocker.GetMock<IDiskProvider>()
+                .Verify(v => v.FileSetLastWriteTime(_episodeFile.Path, It.IsAny<DateTime>()), Times.Once());
+
+            var actualWriteTime = Mocker.GetMock<IDiskProvider>().Object.FileGetLastWrite(_episodeFile.Path).ToLocalTime();
+            actualWriteTime.Should().Be(DateTimeExtensions.EpochTime.ToLocalTime().WithTicksFrom(previousWrite));
+        }
+
+        [Test]
+        public void should_not_clamp_mtime_on_windows()
+        {
+            WindowsOnly();
+
+            var previousWrite = new DateTime(_lastWrite.Ticks, _lastWrite.Kind);
+            _episodes[0].AirDateUtc = _veryOldAirDateUtc;
+
+            Subject.ChangeFileDateForFile(_episodeFile, _series, _episodes);
+            Subject.ChangeFileDateForFile(_episodeFile, _series, _episodes);
+
+            Mocker.GetMock<IDiskProvider>()
+                .Verify(v => v.FileSetLastWriteTime(_episodeFile.Path, It.IsAny<DateTime>()), Times.Once());
+
+            var actualWriteTime = Mocker.GetMock<IDiskProvider>().Object.FileGetLastWrite(_episodeFile.Path).ToLocalTime();
+            actualWriteTime.Should().Be(_episodes[0].AirDateUtc.Value.ToLocalTime().WithTicksFrom(previousWrite));
+        }
+    }
+}
diff --git a/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs b/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs
index 539cc1f64e..4568fc0e0a 100644
--- a/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs
+++ b/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs
@@ -26,7 +26,6 @@ public class UpdateEpisodeFileService : IUpdateEpisodeFileService,
         private readonly IConfigService _configService;
         private readonly IEpisodeService _episodeService;
         private readonly Logger _logger;
-        private static readonly DateTime EpochTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
 
         public UpdateEpisodeFileService(IDiskProvider diskProvider,
                                         IConfigService configService,
@@ -47,90 +46,48 @@ public void ChangeFileDateForFile(EpisodeFile episodeFile, Series series, List<E
         private bool ChangeFileDate(EpisodeFile episodeFile, Series series, List<Episode> episodes)
         {
             var episodeFilePath = Path.Combine(series.Path, episodeFile.RelativePath);
+            var airDateUtc = episodes.First().AirDateUtc;
 
-            switch (_configService.FileDate)
+            if (!airDateUtc.HasValue)
             {
-                case FileDateType.LocalAirDate:
-                    {
-                        var airDate = episodes.First().AirDate;
-                        var airTime = series.AirTime;
-
-                        if (airDate.IsNullOrWhiteSpace() || airTime.IsNullOrWhiteSpace())
-                        {
-                            return false;
-                        }
-
-                        return ChangeFileDateToLocalAirDate(episodeFilePath, airDate, airTime);
-                    }
-
-                case FileDateType.UtcAirDate:
-                    {
-                        var airDateUtc = episodes.First().AirDateUtc;
-
-                        if (!airDateUtc.HasValue)
-                        {
-                            return false;
-                        }
-
-                        return ChangeFileDateToUtcAirDate(episodeFilePath, airDateUtc.Value);
-                    }
+                return false;
             }
 
-            return false;
-        }
-
-        private bool ChangeFileDateToLocalAirDate(string filePath, string fileDate, string fileTime)
-        {
-            if (DateTime.TryParse(fileDate + ' ' + fileTime, out var airDate))
+            return _configService.FileDate switch
             {
-                // avoiding false +ve checks and set date skewing by not using UTC (Windows)
-                var oldLastWrite = _diskProvider.FileGetLastWrite(filePath);
+                FileDateType.LocalAirDate =>
+                    ChangeFileDateToLocalDate(episodeFilePath, airDateUtc.Value.ToLocalTime()),
 
-                if (OsInfo.IsNotWindows && airDate < EpochTime)
-                {
-                    _logger.Debug("Setting date of file to 1970-01-01 as actual airdate is before that time and will not be set properly");
-                    airDate = EpochTime;
-                }
+                // Intentionally pass UTC as local per user preference
+                FileDateType.UtcAirDate =>
+                    ChangeFileDateToLocalDate(
+                        episodeFilePath,
+                        DateTime.SpecifyKind(airDateUtc.Value, DateTimeKind.Local)),
 
-                if (!DateTime.Equals(airDate.WithoutTicks(), oldLastWrite.WithoutTicks()))
-                {
-                    try
-                    {
-                        _diskProvider.FileSetLastWriteTime(filePath, airDate);
-                        _logger.Debug("Date of file [{0}] changed from '{1}' to '{2}'", filePath, oldLastWrite, airDate);
-
-                        return true;
-                    }
-                    catch (Exception ex)
-                    {
-                        _logger.Warn(ex, "Unable to set date of file [" + filePath + "]");
-                    }
-                }
-            }
-            else
-            {
-                _logger.Debug("Could not create valid date to change file [{0}]", filePath);
-            }
-
-            return false;
+                _ => false,
+            };
         }
 
-        private bool ChangeFileDateToUtcAirDate(string filePath, DateTime airDateUtc)
+        private bool ChangeFileDateToLocalDate(string filePath, DateTime localDate)
         {
-            var oldLastWrite = _diskProvider.FileGetLastWrite(filePath);
+            // FileGetLastWrite returns UTC; convert to local to compare
+            var oldLastWrite = _diskProvider.FileGetLastWrite(filePath).ToLocalTime();
 
-            if (OsInfo.IsNotWindows && airDateUtc < EpochTime)
+            if (OsInfo.IsNotWindows && localDate.ToUniversalTime() < DateTimeExtensions.EpochTime)
             {
                 _logger.Debug("Setting date of file to 1970-01-01 as actual airdate is before that time and will not be set properly");
-                airDateUtc = EpochTime;
+                localDate = DateTimeExtensions.EpochTime.ToLocalTime();
             }
 
-            if (!DateTime.Equals(airDateUtc.WithoutTicks(), oldLastWrite.WithoutTicks()))
+            if (!DateTime.Equals(localDate.WithoutTicks(), oldLastWrite.WithoutTicks()))
             {
                 try
                 {
-                    _diskProvider.FileSetLastWriteTime(filePath, airDateUtc.AddMilliseconds(oldLastWrite.Millisecond));
-                    _logger.Debug("Date of file [{0}] changed from '{1}' to '{2}'", filePath, oldLastWrite, airDateUtc);
+                    // Preserve prior mtime subseconds per https://github.com/Sonarr/Sonarr/issues/7228
+                    var mtime = localDate.WithTicksFrom(oldLastWrite);
+
+                    _diskProvider.FileSetLastWriteTime(filePath, mtime);
+                    _logger.Debug("Date of file [{0}] changed from '{1}' to '{2}'", filePath, oldLastWrite, mtime);
 
                     return true;
                 }

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs b/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs
index 539cc1f64e..db891f09e4 100644
--- a/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs
+++ b/src/NzbDrone.Core/MediaFiles/UpdateEpisodeFileService.cs
@@ -84,7 +84,8 @@ private bool ChangeFileDateToLocalAirDate(string filePath, string fileDate, stri
             if (DateTime.TryParse(fileDate + ' ' + fileTime, out var airDate))
             {
                 // avoiding false +ve checks and set date skewing by not using UTC (Windows)
-                var oldLastWrite = _diskProvider.FileGetLastWrite(filePath);
+                // FileGetLastWrite returns UTC, the air date is local
+                var oldLastWrite = _diskProvider.FileGetLastWrite(filePath).ToLocalTime();
 
                 if (OsInfo.IsNotWindows && airDate < EpochTime)
                 {

```
