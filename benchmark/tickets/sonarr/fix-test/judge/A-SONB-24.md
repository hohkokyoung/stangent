# SONB-24: NotificationService fails processing ManualInteractionRequiredEvent when Episode cannot be determined

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/Notifications/Discord/Discord.cs b/src/NzbDrone.Core/Notifications/Discord/Discord.cs
index 237ed56bcf..dbc54697d6 100644
--- a/src/NzbDrone.Core/Notifications/Discord/Discord.cs
+++ b/src/NzbDrone.Core/Notifications/Discord/Discord.cs
@@ -715,6 +715,11 @@ private string GetTitle(Series series, List<Episode> episodes)
                 return null;
             }
 
+            if (episodes.Empty())
+            {
+                return series.Title.Replace("`", "\\`");
+            }
+
             if (series.SeriesType == SeriesTypes.Daily)
             {
                 var episode = episodes.First();
@@ -722,8 +727,7 @@ private string GetTitle(Series series, List<Episode> episodes)
                 return $"{series.Title} - {episode.AirDate} - {episode.Title}".Replace("`", "\\`");
             }
 
-            var episodeNumbers = string.Concat(episodes.Select(e => e.EpisodeNumber)
-                                                       .Select(i => string.Format("x{0:00}", i)));
+            var episodeNumbers = string.Concat(episodes.Select(e => $"x{e.EpisodeNumber:00}"));
 
             var episodeTitles = string.Join(" + ", episodes.Select(e => e.Title));
 
diff --git a/src/NzbDrone.Core/Notifications/NotificationService.cs b/src/NzbDrone.Core/Notifications/NotificationService.cs
index 94741adc8d..90541bcaf7 100644
--- a/src/NzbDrone.Core/Notifications/NotificationService.cs
+++ b/src/NzbDrone.Core/Notifications/NotificationService.cs
@@ -49,38 +49,30 @@ private string GetMessage(Series series, List<Episode> episodes, QualityModel qu
         {
             var qualityString = GetQualityString(series, quality);
 
+            if (episodes.Empty())
+            {
+                return $"{series.Title} - [{qualityString}]";
+            }
+
             if (series.SeriesType == SeriesTypes.Daily)
             {
                 var episode = episodes.First();
 
-                return string.Format("{0} - {1} - {2} [{3}]",
-                                         series.Title,
-                                         episode.AirDate,
-                                         episode.Title,
-                                         qualityString);
+                return $"{series.Title} - {episode.AirDate} - {episode.Title} [{qualityString}]";
             }
 
-            var episodeNumbers = string.Concat(episodes.Select(e => e.EpisodeNumber)
-                                                       .Select(i => string.Format("x{0:00}", i)));
+            var episodeNumbers = string.Concat(episodes.Select(e => $"x{e.EpisodeNumber:00}"));
 
             var episodeTitles = string.Join(" + ", episodes.Select(e => e.Title));
 
-            return string.Format("{0} - {1}{2} - {3} [{4}]",
-                                    series.Title,
-                                    episodes.First().SeasonNumber,
-                                    episodeNumbers,
-                                    episodeTitles,
-                                    qualityString);
+            return $"{series.Title} - {episodes.First().SeasonNumber}{episodeNumbers} - {episodeTitles} [{qualityString}]";
         }
 
         private string GetFullSeasonMessage(Series series, int seasonNumber, QualityModel quality)
         {
             var qualityString = GetQualityString(series, quality);
 
-            return string.Format("{0} - Season {1} [{2}]",
-                series.Title,
-                seasonNumber,
-                qualityString);
+            return $"{series.Title} - Season {seasonNumber} [{qualityString}]";
         }
 
         private string GetQualityString(Series series, QualityModel quality)
diff --git a/src/NzbDrone.Core/Notifications/Slack/Slack.cs b/src/NzbDrone.Core/Notifications/Slack/Slack.cs
index a1b3a49237..cb321fc958 100644
--- a/src/NzbDrone.Core/Notifications/Slack/Slack.cs
+++ b/src/NzbDrone.Core/Notifications/Slack/Slack.cs
@@ -266,6 +266,16 @@ private SlackPayload CreatePayload(string message, List<Attachment> attachments
 
         private string GetTitle(Series series, List<Episode> episodes)
         {
+            if (series == null)
+            {
+                return null;
+            }
+
+            if (episodes.Empty())
+            {
+                return series.Title;
+            }
+
             if (series.SeriesType == SeriesTypes.Daily)
             {
                 var episode = episodes.First();
@@ -273,8 +283,7 @@ private string GetTitle(Series series, List<Episode> episodes)
                 return $"{series.Title} - {episode.AirDate} - {episode.Title}";
             }
 
-            var episodeNumbers = string.Concat(episodes.Select(e => e.EpisodeNumber)
-                                                       .Select(i => string.Format("x{0:00}", i)));
+            var episodeNumbers = string.Concat(episodes.Select(e => $"x{e.EpisodeNumber:00}"));
 
             var episodeTitles = string.Join(" + ", episodes.Select(e => e.Title));
 

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/Notifications/NotificationService.cs b/src/NzbDrone.Core/Notifications/NotificationService.cs
index 94741adc8d..f7de7a2943 100644
--- a/src/NzbDrone.Core/Notifications/NotificationService.cs
+++ b/src/NzbDrone.Core/Notifications/NotificationService.cs
@@ -342,7 +342,7 @@ public void Handle(ManualInteractionRequiredEvent message)
             var series = message.Episode?.Series;
             var mess = "";
 
-            if (series != null)
+            if (series != null && message.Episode.Episodes.Any())
             {
                 mess = GetMessage(series, message.Episode.Episodes, message.Episode.ParsedEpisodeInfo.Quality);
             }

```
