# Process notification queues after downloads processed

`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.HandleAsync(NzbDrone.Core.Download.DownloadsProcessedEvent)` · backend · Sonarr.Core

From the code: NotificationService.HandleAsync(DownloadsProcessedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a downloads-processed event arrives, the service processes the queued updates of every available notification provider. [1][2][3]

## Rules

- **R1** If processing one notification's queue throws, a warning 'Unable to process notification queue for <name>' is logged and the remaining notifications are still processed. [2]
- **R2** By default a notification's ProcessQueue does nothing; only providers that override it act. [4]
- **R3** Emby/MediaBrowser updates the library only when UpdateLibrary is enabled in its settings. [5]
- **R4** For Emby, if a series has exactly one update type that type is sent; otherwise no update type is sent and Emby decides. [5]
- **R5** Plex Server updates the library only when UpdateLibrary is enabled in its settings. [6]
- **R6** Kodi (Xbmc) updates the library for each queued series only when UpdateLibrary is enabled. [7]
- **R7** Kodi cleans the library only when the queued item's info contains true and CleanLibrary is enabled. [7]
- **R8** If Kodi cannot be reached (socket error), a debug message 'Unable to connect to Kodi Host: host:port' is logged and processing continues. [7]
- **R9** Queued items are processed per configured host (Settings.Host) for Emby, Plex and Kodi. [5][6][7]

## Side effects

- **E1** Emby receives a library update call for each queued series. [5]
- **E2** Plex receives a library update for the queued series. [6]
- **E3** Kodi receives a library update per queued series and, when applicable, a library clean. [7]

## Couldn't see

- the depth limit left out 51 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.HandleAsync(NzbDrone.Core.Download.DownloadsProcessedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.ProcessQueue`
3. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProviderFactory`2.GetAvailableProviders`
4. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationBase`1.ProcessQueue`
5. `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowser.ProcessQueue`
6. `Sonarr.Core|M:NzbDrone.Core.Notifications.Plex.Server.PlexServer.ProcessQueue`
7. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.ProcessQueue`

<sub>commit 0bffefe · inputs a670b10 · slice of 55 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
