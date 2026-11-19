# SONB-12: Manual Import: series lookup by folder name discards its result, causing false 'Unknown Series' rejections

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
index 89b447e5a9..27178a524d 100644
--- a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
+++ b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
@@ -307,7 +307,7 @@ private ManualImportItem ProcessFile(string rootFolder, string baseFolder, strin
 
                 if (series == null)
                 {
-                    _parsingService.GetSeries(relativeFile.Split('\\', '/')[0]);
+                    series = _parsingService.GetSeries(relativeFile.Split('\\', '/')[0]);
                 }
 
                 if (series == null)

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
index 89b447e5a9..27178a524d 100644
--- a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
+++ b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
@@ -307,7 +307,7 @@ private ManualImportItem ProcessFile(string rootFolder, string baseFolder, strin
 
                 if (series == null)
                 {
-                    _parsingService.GetSeries(relativeFile.Split('\\', '/')[0]);
+                    series = _parsingService.GetSeries(relativeFile.Split('\\', '/')[0]);
                 }
 
                 if (series == null)

```
