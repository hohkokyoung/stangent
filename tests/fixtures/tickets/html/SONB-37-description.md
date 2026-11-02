### Is there an existing issue for this?

- [x] I have searched the existing open and closed issues

### Current Behavior

user reported this on discord and i confirmed

the season search icon (which says it should only search monitored).. implies that it should limit its scope to episodes monitored within the season

but testing

![Image](https://github.com/user-attachments/assets/79bf436c-2820-4c19-a3e0-e7c0dd1aa4c9)

I see it doing a search for each episode... which of course can be very costly and unexpected behavior.. especially for season0/specials.

### Expected Behavior

only do ep search for monitored episodes to align with tooltip

### Steps To Reproduce

add series, monitor season and unmonitor some episodes. use season search.

### Environment

```markdown
- OS: Unraid
- Sonarr: 4.0.12.2866
- Docker Install: yes
- Using Reverse Proxy: yes
- Browser: Firefox 134
- Database: sqlite
```

### What branch are you running?

Develop

### Trace Logs?

```
2025-01-22 13:13:06.9|Trace|CommandQueueManager|Marking command as started: SeasonSearch
2025-01-22 13:13:06.9|Trace|Http|Res: 1057 [POST] /api/v3/command: 201.Created (26 ms)
2025-01-22 13:13:06.9|Debug|Api|[POST] /api/v3/command: 201.Created (26 ms)
2025-01-22 13:13:06.9|Trace|EventAggregator|Publishing CommandUpdatedEvent
2025-01-22 13:13:06.9|Trace|EventAggregator|CommandUpdatedEvent -> CommandController
2025-01-22 13:13:06.9|Trace|EventAggregator|CommandUpdatedEvent <- CommandController
2025-01-22 13:13:07.0|Info|ReleaseSearchService|Searching indexers for [3 Body Problem] Specials. 1 active indexers
2025-01-22 13:13:07.0|Trace|EventAggregator|Publishing CommandUpdatedEvent
2025-01-22 13:13:07.0|Trace|EventAggregator|CommandUpdatedEvent -> CommandController
2025-01-22 13:13:07.0|Trace|EventAggregator|CommandUpdatedEvent <- CommandController
2025-01-22 13:13:07.0|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+1
2025-01-22 13:13:07.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+1
2025-01-22 13:13:07.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:07.4|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+1: 200.OK (436 bytes) (455 ms)
2025-01-22 13:13:07.4|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+1
2025-01-22 13:13:07.4|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.543 sec
2025-01-22 13:13:09.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+1
2025-01-22 13:13:09.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:09.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+1: 200.OK (436 bytes) (197 ms)
2025-01-22 13:13:09.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+1
2025-01-22 13:13:09.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.801 sec
2025-01-22 13:13:11.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+1
2025-01-22 13:13:11.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:11.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+1: 200.OK (436 bytes) (188 ms)
2025-01-22 13:13:11.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+1
2025-01-22 13:13:11.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.809 sec
2025-01-22 13:13:13.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+1
2025-01-22 13:13:13.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:13.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+1: 200.OK (436 bytes) (242 ms)
2025-01-22 13:13:13.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+2
2025-01-22 13:13:13.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.756 sec
2025-01-22 13:13:15.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+2
2025-01-22 13:13:15.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:15.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+2: 200.OK (436 bytes) (195 ms)
2025-01-22 13:13:15.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+2
2025-01-22 13:13:15.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.803 sec
2025-01-22 13:13:17.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+2
2025-01-22 13:13:17.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:17.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+2: 200.OK (436 bytes) (200 ms)
2025-01-22 13:13:17.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+2
2025-01-22 13:13:17.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.799 sec
2025-01-22 13:13:19.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+2
2025-01-22 13:13:19.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:19.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+2: 200.OK (436 bytes) (193 ms)
2025-01-22 13:13:19.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+2
2025-01-22 13:13:19.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.804 sec
2025-01-22 13:13:21.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+2
2025-01-22 13:13:21.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:21.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+2: 200.OK (436 bytes) (250 ms)
2025-01-22 13:13:21.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+3
2025-01-22 13:13:21.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.749 sec
2025-01-22 13:13:23.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+3
2025-01-22 13:13:23.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:23.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+3: 200.OK (436 bytes) (180 ms)
2025-01-22 13:13:23.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+3
2025-01-22 13:13:23.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.819 sec
2025-01-22 13:13:24.2|Trace|Scheduler|Pending Tasks: 0
2025-01-22 13:13:25.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+3
2025-01-22 13:13:25.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:25.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+3: 200.OK (436 bytes) (217 ms)
2025-01-22 13:13:25.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+3
2025-01-22 13:13:25.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.781 sec
2025-01-22 13:13:27.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+3
2025-01-22 13:13:27.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:27.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+3: 200.OK (436 bytes) (190 ms)
2025-01-22 13:13:27.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+3
2025-01-22 13:13:27.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.808 sec
2025-01-22 13:13:29.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+3
2025-01-22 13:13:29.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:29.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+3: 200.OK (436 bytes) (246 ms)
2025-01-22 13:13:29.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+4
2025-01-22 13:13:29.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.752 sec
2025-01-22 13:13:31.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+4
2025-01-22 13:13:31.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:31.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+4: 200.OK (436 bytes) (190 ms)
2025-01-22 13:13:31.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+4
2025-01-22 13:13:31.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.808 sec
2025-01-22 13:13:33.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+4
2025-01-22 13:13:33.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:33.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+4: 200.OK (436 bytes) (202 ms)
2025-01-22 13:13:33.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+4
2025-01-22 13:13:33.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.795 sec
2025-01-22 13:13:35.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+4
2025-01-22 13:13:35.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:35.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+4: 200.OK (436 bytes) (196 ms)
2025-01-22 13:13:35.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+4
2025-01-22 13:13:35.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.804 sec
2025-01-22 13:13:37.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+4
2025-01-22 13:13:37.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:37.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+4: 200.OK (436 bytes) (251 ms)
2025-01-22 13:13:37.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+5
2025-01-22 13:13:37.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.747 sec
2025-01-22 13:13:39.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+5
2025-01-22 13:13:39.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:39.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+5: 200.OK (436 bytes) (180 ms)
2025-01-22 13:13:39.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+5
2025-01-22 13:13:39.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.816 sec
2025-01-22 13:13:41.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+5
2025-01-22 13:13:41.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:41.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+5: 200.OK (436 bytes) (217 ms)
2025-01-22 13:13:41.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+5
2025-01-22 13:13:41.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.782 sec
2025-01-22 13:13:43.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+5
2025-01-22 13:13:43.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:43.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+5: 200.OK (436 bytes) (188 ms)
2025-01-22 13:13:43.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+5
2025-01-22 13:13:43.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.811 sec
2025-01-22 13:13:45.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+5
2025-01-22 13:13:45.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:45.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+5: 200.OK (436 bytes) (240 ms)
2025-01-22 13:13:45.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+6
2025-01-22 13:13:45.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.759 sec
2025-01-22 13:13:47.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+6
2025-01-22 13:13:47.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:47.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+6: 200.OK (436 bytes) (178 ms)
2025-01-22 13:13:47.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+6
2025-01-22 13:13:47.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.821 sec
2025-01-22 13:13:49.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+6
2025-01-22 13:13:49.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:49.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+6: 200.OK (436 bytes) (202 ms)
2025-01-22 13:13:49.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+6
2025-01-22 13:13:49.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.796 sec
2025-01-22 13:13:51.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+6
2025-01-22 13:13:51.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:51.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+6: 200.OK (436 bytes) (192 ms)
2025-01-22 13:13:51.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+6
2025-01-22 13:13:51.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.806 sec
2025-01-22 13:13:53.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+6
2025-01-22 13:13:53.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:53.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+6: 200.OK (436 bytes) (241 ms)
2025-01-22 13:13:53.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+7
2025-01-22 13:13:53.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.758 sec
2025-01-22 13:13:54.3|Debug|Api|[GET] /api/v3/command: 200.OK (1 ms)
2025-01-22 13:13:55.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+7
2025-01-22 13:13:55.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:55.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+7: 200.OK (436 bytes) (196 ms)
2025-01-22 13:13:55.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+7
2025-01-22 13:13:55.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.802 sec
2025-01-22 13:13:56.5|Trace|Http|Req: 1062 [GET] /api/v3/log/file (from 47.*.*.206 Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:134.0) Gecko/20100101 Firefox/134.0)
2025-01-22 13:13:56.5|Debug|Sonarr.Http.Authentication.ApiKeyAuthenticationHandler|AuthenticationScheme: API was successfully authenticated.
2025-01-22 13:13:56.5|Trace|Http|Res: 1062 [GET] /api/v3/log/file: 200.OK (14 ms)
2025-01-22 13:13:56.5|Debug|Api|[GET] /api/v3/log/file: 200.OK (14 ms)
2025-01-22 13:13:57.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+7
2025-01-22 13:13:57.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:57.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+7: 200.OK (436 bytes) (201 ms)
2025-01-22 13:13:57.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+7
2025-01-22 13:13:57.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.798 sec
2025-01-22 13:13:58.1|Trace|Http|Req: 1063 [GET] /logfile/sonarr.trace.txt (from 47.*.*.206 Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:134.0) Gecko/20100101 Firefox/134.0)
2025-01-22 13:13:58.1|Trace|Http|Res: 1063 [GET] /logfile/sonarr.trace.txt: 200.OK (0 ms)
2025-01-22 13:13:58.3|Trace|Http|Req: 1064 [GET] /favicon.ico (from 47.*.*.206 Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:134.0) Gecko/20100101 Firefox/134.0)
2025-01-22 13:13:58.3|Trace|Http|Res: 1064 [GET] /favicon.ico: 200.OK (10 ms)
2025-01-22 13:13:59.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+7
2025-01-22 13:13:59.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:13:59.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+7: 200.OK (436 bytes) (193 ms)
2025-01-22 13:13:59.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+7
2025-01-22 13:13:59.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.805 sec
2025-01-22 13:14:01.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+7
2025-01-22 13:14:01.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:14:01.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+7: 200.OK (436 bytes) (267 ms)
2025-01-22 13:14:01.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+8
2025-01-22 13:14:01.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.731 sec
2025-01-22 13:14:03.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+8
2025-01-22 13:14:03.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:14:03.1|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=El+problema+de+los+3+cuerpos+Inside+the+Episodes+Episode+8: 200.OK (436 bytes) (173 ms)
2025-01-22 13:14:03.1|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+8
2025-01-22 13:14:03.1|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.826 sec
2025-01-22 13:14:05.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+8
2025-01-22 13:14:05.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:14:05.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Il+problema+dei+3+corpi+Inside+the+Episodes+Episode+8: 200.OK (436 bytes) (201 ms)
2025-01-22 13:14:05.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+8
2025-01-22 13:14:05.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.796 sec
2025-01-22 13:14:07.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+8
2025-01-22 13:14:07.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:14:07.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=Le+Probleme+a+trois+corps+Inside+the+Episodes+Episode+8: 200.OK (436 bytes) (195 ms)
2025-01-22 13:14:07.2|Debug|Newznab|Downloading Feed https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+8
2025-01-22 13:14:07.2|Trace|RateLimitService|Rate Limit triggered, delaying 'api.removed.tld' for 1.799 sec
2025-01-22 13:14:09.0|Trace|HttpClient|Req: [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+8
2025-01-22 13:14:09.0|Trace|ConfigService|Using default config value for 'proxyenabled' defaultValue:'False'
2025-01-22 13:14:09.2|Trace|HttpClient|Res: HTTP/2.0 [GET] https://api.removed.tld/api?t=search&cat=5040,5045&extended=1&apikey=(removed)&offset=0&limit=100&q=3+Body+Problem+Inside+the+Episodes+Episode+8: 200.OK (436 bytes) (249 ms)
2025-01-22 13:14:09.2|Debug|ReleaseSearchService|Total of 0 reports were found for [3 Body Problem] Specials from 1 indexers
```

### Anything else?

_No response_
