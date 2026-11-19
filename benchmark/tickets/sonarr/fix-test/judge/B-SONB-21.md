# SONB-21: Command-line data path causing backups to fail

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Common.Test/DiskTests/DiskTransferServiceFixture.cs b/src/NzbDrone.Common.Test/DiskTests/DiskTransferServiceFixture.cs
index cd491e68ac..24a9611293 100644
--- a/src/NzbDrone.Common.Test/DiskTests/DiskTransferServiceFixture.cs
+++ b/src/NzbDrone.Common.Test/DiskTests/DiskTransferServiceFixture.cs
@@ -579,6 +579,21 @@ public void MoveFolder_should_rename_casesensitive_folder()
             Directory.Exists(destination.FullName).Should().Be(true);
         }
 
+        [Test]
+        public void TransferFile_should_find_files_with_multiple_slashes_within_their_path()
+        {
+            WithRealDiskProvider();
+
+            var root = GetFilledTempFolder();
+            var rootDir = root.FullName;
+            var from = Path.Combine(rootDir, "source-file");
+            var toRootDir = rootDir.Replace(Path.DirectorySeparatorChar.ToString(), new string(Path.DirectorySeparatorChar, 3));
+            var to = Path.Combine(toRootDir, "destination-file");
+            File.WriteAllText(from, "Source file");
+            var mode = Subject.TransferFile(from, to, TransferMode.Copy);
+            mode.Should().Be(TransferMode.Copy);
+        }
+
         [Test]
         public void should_throw_if_destination_is_readonly()
         {
diff --git a/src/NzbDrone.Common/Disk/DiskTransferService.cs b/src/NzbDrone.Common/Disk/DiskTransferService.cs
index f89c31b212..36bcbcc3ee 100644
--- a/src/NzbDrone.Common/Disk/DiskTransferService.cs
+++ b/src/NzbDrone.Common/Disk/DiskTransferService.cs
@@ -28,15 +28,15 @@ public DiskTransferService(IDiskProvider diskProvider, Logger logger)
 
         private string ResolveRealParentPath(string path)
         {
-            var parentPath = path.GetParentPath();
-            if (!_diskProvider.FolderExists(parentPath))
+            if (!_diskProvider.FolderExists(path.GetParentPath()))
             {
                 return path;
             }
 
+            var cleanPath = path.GetCleanPath();
+            var parentPath = cleanPath.GetParentPath();
             var realParentPath = parentPath.GetActualCasing();
-
-            var partialChildPath = path.Substring(parentPath.Length);
+            var partialChildPath = cleanPath.Substring(parentPath.Length);
 
             return realParentPath + partialChildPath;
         }

```

## The agent's fix (side B)

```diff
diff --git a/src/NzbDrone.Common/Disk/DiskTransferService.cs b/src/NzbDrone.Common/Disk/DiskTransferService.cs
index f89c31b212..c8625bde0b 100644
--- a/src/NzbDrone.Common/Disk/DiskTransferService.cs
+++ b/src/NzbDrone.Common/Disk/DiskTransferService.cs
@@ -36,7 +36,9 @@ private string ResolveRealParentPath(string path)
 
             var realParentPath = parentPath.GetActualCasing();
 
-            var partialChildPath = path.Substring(parentPath.Length);
+            // The parent path is normalized (repeated slashes collapsed), so its length can't be used to find the child part.
+            var lastSeparatorIndex = path.TrimEnd('/', '\\').LastIndexOfAny(new[] { '/', '\\' });
+            var partialChildPath = path.Substring(lastSeparatorIndex);
 
             return realParentPath + partialChildPath;
         }

```
