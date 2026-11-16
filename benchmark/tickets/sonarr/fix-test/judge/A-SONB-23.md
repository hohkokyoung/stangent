# SONB-23: Testing Qbit connection reuses auth cookie after credentials change

Both diffs make the bug's failing test pass. Is the agent's a fix of the cause (`clean`), or a patch over the symptom that leaves the bug (`symptom`)? Judge the agent's against the developer's; a different fix can still be clean.

## The developer's fix

```diff
diff --git a/src/NzbDrone.Common/Http/HttpRequestBuilder.cs b/src/NzbDrone.Common/Http/HttpRequestBuilder.cs
index 03f84411c0..7a1d70fea2 100644
--- a/src/NzbDrone.Common/Http/HttpRequestBuilder.cs
+++ b/src/NzbDrone.Common/Http/HttpRequestBuilder.cs
@@ -28,6 +28,7 @@ public class HttpRequestBuilder
         public TimeSpan RateLimit { get; set; }
         public bool LogResponseContent { get; set; }
         public ICredentials NetworkCredential { get; set; }
+        public bool StoreRequestCookie { get; set; }
         public Dictionary<string, string> Cookies { get; private set; }
         public List<HttpFormData> FormData { get; private set; }
         public Action<HttpRequest> PostProcess { get; set; }
@@ -44,6 +45,7 @@ public HttpRequestBuilder(string baseUrl)
             Cookies = new Dictionary<string, string>();
             FormData = new List<HttpFormData>();
             LogHttpError = true;
+            StoreRequestCookie = true;
         }
 
         public HttpRequestBuilder(bool useHttps, string host, int port, string urlBase = null)
@@ -111,6 +113,7 @@ protected virtual void Apply(HttpRequest request)
             request.RateLimit = RateLimit;
             request.LogResponseContent = LogResponseContent;
             request.Credentials = NetworkCredential;
+            request.StoreRequestCookie = StoreRequestCookie;
 
             foreach (var header in Headers)
             {
diff --git a/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs b/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs
index 89bdc40e04..e8ec30d355 100644
--- a/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs
+++ b/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs
@@ -352,7 +352,7 @@ private HttpRequestBuilder BuildRequest(QBittorrentSettings settings)
             var requestBuilder = new HttpRequestBuilder(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase)
             {
                 LogResponseContent = true,
-                NetworkCredential = new BasicNetworkCredential(settings.Username, settings.Password)
+                StoreRequestCookie = false
             };
             return requestBuilder;
         }
@@ -413,7 +413,7 @@ private void AuthenticateClient(HttpRequestBuilder requestBuilder, QBittorrentSe
                 return;
             }
 
-            var authKey = string.Format("{0}:{1}", requestBuilder.BaseUrl, settings.Password);
+            var authKey = $"{requestBuilder.BaseUrl}:{settings.Username}:{settings.Password}";
 
             var cookies = _authCookieCache.Find(authKey);
 
@@ -421,7 +421,10 @@ private void AuthenticateClient(HttpRequestBuilder requestBuilder, QBittorrentSe
             {
                 _authCookieCache.Remove(authKey);
 
-                var authLoginRequest = BuildRequest(settings).Resource("/api/v2/auth/login")
+                var authRequestBuilder = BuildRequest(settings);
+                authRequestBuilder.NetworkCredential = new BasicNetworkCredential(settings.Username, settings.Password);
+
+                var authLoginRequest = authRequestBuilder.Resource("/api/v2/auth/login")
                                                              .Post()
                                                              .AddFormParameter("username", settings.Username ?? string.Empty)
                                                              .AddFormParameter("password", settings.Password ?? string.Empty)

```

## The agent's fix (side A)

```diff
diff --git a/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs b/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs
index 89bdc40e04..179ac66052 100644
--- a/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs
+++ b/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs
@@ -413,7 +413,7 @@ private void AuthenticateClient(HttpRequestBuilder requestBuilder, QBittorrentSe
                 return;
             }
 
-            var authKey = string.Format("{0}:{1}", requestBuilder.BaseUrl, settings.Password);
+            var authKey = string.Format("{0}:{1}:{2}", requestBuilder.BaseUrl, settings.Username, settings.Password);
 
             var cookies = _authCookieCache.Find(authKey);
 

```
