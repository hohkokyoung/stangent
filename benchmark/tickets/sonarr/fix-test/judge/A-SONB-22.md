# SONB-22: NFO metadata writer for multi-episode files might be broken

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs b/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs
index d0fd3c23a2..1041dfec9f 100644
--- a/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs
+++ b/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs
@@ -286,20 +286,20 @@ public override MetadataFileResult EpisodeMetadata(Series series, EpisodeFile ep
 
             var watched = GetExistingWatchedStatus(series, episodeFile.RelativePath);
 
-            var xmlResult = string.Empty;
             var xws = new XmlWriterSettings
             {
                 Encoding = Encoding.UTF8,
-                Indent = true
+                Indent = true,
+                ConformanceLevel =  ConformanceLevel.Fragment
             };
 
+            using var sw = new Utf8StringWriter();
+            using var xw = XmlWriter.Create(sw, xws);
+
+            xw.WriteProcessingInstruction("xml", "version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"");
+
             foreach (var episode in episodeFile.Episodes.Value)
             {
-                var doc = new XDocument
-                {
-                    Declaration = new XDeclaration("1.0", "UTF-8", "yes")
-                };
-
                 var image = episode.Images.SingleOrDefault(i => i.CoverType == MediaCoverTypes.Screenshot);
 
                 var details = new XElement("episodedetails");
@@ -408,17 +408,12 @@ public override MetadataFileResult EpisodeMetadata(Series series, EpisodeFile ep
                 // details.Add(new XElement("credits", tvdbEpisode.Writer.FirstOrDefault()));
                 // details.Add(new XElement("director", tvdbEpisode.Directors.FirstOrDefault()));
 
-                using var sw = new Utf8StringWriter();
-                using var xw = XmlWriter.Create(sw, xws);
-
-                doc.Add(details);
-                doc.Save(xw);
-                xw.Flush();
-
-                xmlResult += sw.ToString();
-                xmlResult += Environment.NewLine;
+                details.WriteTo(xw);
             }
 
+            xw.Flush();
+            var xmlResult = sw.ToString();
+
             return new MetadataFileResult(GetEpisodeMetadataFilename(episodeFile.RelativePath), xmlResult.Trim(Environment.NewLine.ToCharArray()));
         }
 

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs b/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs
index d0fd3c23a2..f856ac434a 100644
--- a/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs
+++ b/src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs
@@ -417,6 +417,9 @@ public override MetadataFileResult EpisodeMetadata(Series series, EpisodeFile ep
 
                 xmlResult += sw.ToString();
                 xmlResult += Environment.NewLine;
+
+                // Only the first document should carry the XML declaration
+                xws.OmitXmlDeclaration = true;
             }
 
             return new MetadataFileResult(GetEpisodeMetadataFilename(episodeFile.RelativePath), xmlResult.Trim(Environment.NewLine.ToCharArray()));

```
