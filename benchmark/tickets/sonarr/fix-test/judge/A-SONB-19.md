# SONB-19: PMS library update sometimes causes TV show removal

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs b/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs
index ceff608658..4d2a9f226d 100644
--- a/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs
+++ b/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs
@@ -141,8 +141,9 @@ private void UpdateSectionPath(string seriesRelativePath, PlexSection section, P
             var separator = location.Path.Contains('\\') ? "\\" : "/";
             var locationRelativePath = seriesRelativePath.Replace("\\", separator).Replace("/", separator);
 
-            // Plex location paths trim trailing extraneous separator characters, so it doesn't need to be trimmed
-            var pathToUpdate = $"{location.Path}{separator}{locationRelativePath}";
+            // Plex location paths trim trailing extraneous separator characters,
+            // unless it's a Windows drive letter (S:\) that needs to be trimmed.
+            var pathToUpdate = $"{location.Path.TrimEnd(separator)}{separator}{locationRelativePath}";
 
             _logger.Debug("Updating section location, {0}", location.Path);
             _plexServerProxy.Update(section.Id, pathToUpdate, settings);

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs b/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs
index ceff608658..537eba0fc3 100644
--- a/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs
+++ b/src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs
@@ -142,7 +142,7 @@ private void UpdateSectionPath(string seriesRelativePath, PlexSection section, P
             var locationRelativePath = seriesRelativePath.Replace("\\", separator).Replace("/", separator);
 
             // Plex location paths trim trailing extraneous separator characters, so it doesn't need to be trimmed
-            var pathToUpdate = $"{location.Path}{separator}{locationRelativePath}";
+            var pathToUpdate = $"{location.Path.TrimEnd('\\', '/')}{separator}{locationRelativePath}";
 
             _logger.Debug("Updating section location, {0}", location.Path);
             _plexServerProxy.Update(section.Id, pathToUpdate, settings);

```
