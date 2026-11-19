# SONB-27: Double notifications triggered on manual import from activity

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
index aee6b97f4b..5e605e5bdc 100644
--- a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
+++ b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
@@ -483,6 +483,7 @@ public void Execute(ManualImportCommand message)
 
             var imported = new List<ImportResult>();
             var importedTrackedDownload = new List<ManuallyImportedFile>();
+            var importedUntrackedDownload = new List<ImportResult>();
 
             for (var i = 0; i < message.Files.Count; i++)
             {
@@ -545,7 +546,10 @@ public void Execute(ManualImportCommand message)
 
                 if (trackedDownload == null)
                 {
-                    imported.AddRange(_importApprovedEpisodes.Import(new List<ImportDecision> { importDecision }, !existingFile, null, message.ImportMode));
+                    var importResult = _importApprovedEpisodes.Import(new List<ImportDecision> { importDecision }, !existingFile, null, message.ImportMode);
+
+                    imported.AddRange(importResult);
+                    importedUntrackedDownload.AddRange(importResult);
                 }
                 else
                 {
@@ -566,7 +570,7 @@ public void Execute(ManualImportCommand message)
                 _logger.ProgressTrace("Manually imported {0} files", imported.Count);
             }
 
-            var untrackedImports = imported.Where(i => i.Result == ImportResultType.Imported && importedTrackedDownload.FirstOrDefault(t => t.ImportResult != i) == null).ToList();
+            var untrackedImports = importedUntrackedDownload.Where(i => i.Result == ImportResultType.Imported).ToList();
 
             if (untrackedImports.Any())
             {

```

## The agent's fix (side B)

```diff
diff --git a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
index aee6b97f4b..c06c2b6b21 100644
--- a/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
+++ b/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs
@@ -566,7 +566,7 @@ public void Execute(ManualImportCommand message)
                 _logger.ProgressTrace("Manually imported {0} files", imported.Count);
             }
 
-            var untrackedImports = imported.Where(i => i.Result == ImportResultType.Imported && importedTrackedDownload.FirstOrDefault(t => t.ImportResult != i) == null).ToList();
+            var untrackedImports = imported.Where(i => i.Result == ImportResultType.Imported && importedTrackedDownload.All(t => t.ImportResult != i)).ToList();
 
             if (untrackedImports.Any())
             {

```
