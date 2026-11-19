# SONB-13: Import Notification queue on Jellyfin 12+ produces 401 Unauthorized

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs b/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs
index b5e6cd9d74..47a1d7abae 100644
--- a/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs
+++ b/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs
@@ -22,9 +22,7 @@ public MediaBrowserProxy(IHttpClient httpClient, Logger logger)
         public void TestConnection(MediaBrowserSettings settings)
         {
             var path = "/System/Configuration";
-            var request = BuildRequest(path, settings);
-            request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);
-            request.Headers.Add("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
+            var request = BuildRequest(path, settings).Build();
 
             var response = _httpClient.Get(request);
             _logger.Trace("Response: {0}", response.Content);
@@ -33,7 +31,7 @@ public void TestConnection(MediaBrowserSettings settings)
         public void Notify(MediaBrowserSettings settings, string title, string message)
         {
             var path = "/Notifications/Admin";
-            var request = BuildRequest(path, settings);
+            var request = BuildRequest(path, settings).Build();
             request.Headers.ContentType = "application/json";
             request.LogHttpError = false;
 
@@ -44,17 +42,14 @@ public void Notify(MediaBrowserSettings settings, string title, string message)
                                ImageUrl = "https://raw.github.com/Sonarr/Sonarr/develop/Logo/64.png"
                            }.ToJson());
 
-            ProcessRequest(request, settings);
+            ProcessRequest(request);
         }
 
         public HashSet<string> GetPaths(MediaBrowserSettings settings, Series series)
         {
             var path = "/Items";
-            var url = GetUrl(settings);
 
-            // NameStartsWith uses the sort title, which is not the series title
-            var request = new HttpRequestBuilder(url)
-                .Resource(path)
+            var request = BuildRequest(path, settings)
                 .AddQueryParam("recursive", "true")
                 .AddQueryParam("includeItemTypes", "Series")
                 .AddQueryParam("fields", "Path,ProviderIds")
@@ -63,7 +58,7 @@ public HashSet<string> GetPaths(MediaBrowserSettings settings, Series series)
 
             try
             {
-                var paths = ProcessGetRequest<MediaBrowserItems>(request, settings).Items.GroupBy(item =>
+                var paths = ProcessGetRequest<MediaBrowserItems>(request).Items.GroupBy(item =>
                 {
                     if (item is { ProviderIds.Tvdb: int tvdbid } && tvdbid != 0 && tvdbid == series.TvdbId)
                     {
@@ -121,7 +116,7 @@ public HashSet<string> GetPaths(MediaBrowserSettings settings, Series series)
         public void Update(MediaBrowserSettings settings, string seriesPath, string updateType)
         {
             var path = "/Library/Media/Updated";
-            var request = BuildRequest(path, settings);
+            var request = BuildRequest(path, settings).Build();
             request.Headers.ContentType = "application/json";
 
             request.SetContent(new
@@ -136,14 +131,12 @@ public void Update(MediaBrowserSettings settings, string seriesPath, string upda
                 }
             }.ToJson());
 
-            ProcessRequest(request, settings);
+            ProcessRequest(request);
         }
 
-        private T ProcessGetRequest<T>(HttpRequest request, MediaBrowserSettings settings)
+        private T ProcessGetRequest<T>(HttpRequest request)
             where T : new()
         {
-            request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);
-
             var response = _httpClient.Get<T>(request);
             _logger.Trace("Response: {0}", response.Content);
 
@@ -152,10 +145,8 @@ private T ProcessGetRequest<T>(HttpRequest request, MediaBrowserSettings setting
             return response.Resource;
         }
 
-        private string ProcessRequest(HttpRequest request, MediaBrowserSettings settings)
+        private string ProcessRequest(HttpRequest request)
         {
-            request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);
-
             var response = _httpClient.Post(request);
             _logger.Trace("Response: {0}", response.Content);
 
@@ -170,11 +161,15 @@ private string GetUrl(MediaBrowserSettings settings)
             return $@"{scheme}://{settings.Address}";
         }
 
-        private HttpRequest BuildRequest(string path, MediaBrowserSettings settings)
+        private HttpRequestBuilder BuildRequest(string path, MediaBrowserSettings settings)
         {
             var url = GetUrl(settings);
+            var request = new HttpRequestBuilder(url).Resource(path);
+
+            request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);
+            request.Headers.Add("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
 
-            return new HttpRequestBuilder(url).Resource(path).Build();
+            return request;
         }
 
         private void CheckForError(HttpResponse response)

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs b/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs
index b5e6cd9d74..cb9712f9bb 100644
--- a/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs
+++ b/src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs
@@ -143,6 +143,7 @@ private T ProcessGetRequest<T>(HttpRequest request, MediaBrowserSettings setting
             where T : new()
         {
             request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);
+            request.Headers.Add("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
 
             var response = _httpClient.Get<T>(request);
             _logger.Trace("Response: {0}", response.Content);
@@ -155,6 +156,7 @@ private T ProcessGetRequest<T>(HttpRequest request, MediaBrowserSettings setting
         private string ProcessRequest(HttpRequest request, MediaBrowserSettings settings)
         {
             request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);
+            request.Headers.Add("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
 
             var response = _httpClient.Post(request);
             _logger.Trace("Response: {0}", response.Content);

```
