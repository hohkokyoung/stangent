# Untracked download completed: import-complete notifications

`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.UntrackedDownloadCompletedEvent)` · backend · Sonarr.Core

From the code: NotificationService.Handle(UntrackedDownloadCompletedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The import-complete message text for a full-season download is "<series title> - Season <n> [<quality>]". [1]
- **S2** For other downloads with no episodes the message is "<series title> - [<quality>]". [2]
- **S3** For daily series the message is "<series title> - <air date> - <episode title> [<quality>]", using the first episode. [2]
- **S4** For other series the message is "<series title> - <season>x<nn>... - <titles joined by ' + '> [<quality>]". [2]
- **S5** The quality text is the quality name. When the revision version is above 1, anime series add " v<version>" and other series add " Proper". [3]
- **S6** The message carries series, episodes, episode files, source path, release title, release group, release quality, and a destination path. [4]
- **S7** The destination path is the longest common folder path of the imported episode files, each built from the series path plus the file's relative path. [4][5]

## Actions

- **A1** When an untracked download completes, the handler builds an import-complete message and sends it to every enabled notification that is configured for import complete. [4][6]

## Rules

- **R1** A notification is sent only if it has the OnImportComplete option on and passes the series tag check. [4][6]
- **R2** A notification with no tags always handles the series. [7]
- **R3** A notification with tags is skipped when the series is unknown or shares no tag with the notification. [7]
- **R4** Notifications that are temporarily blocked because of recent failures are skipped. [8][6]
- **R5** A failure in one notification is logged as a warning and does not stop the others. [4]
- **R6** Synology Indexer updates the series folder only when its UpdateLibrary setting is on. [9]
- **R7** Emby/Jellyfin sends a notification only if Notify is on, and schedules a library update only if UpdateLibrary is on. [10][11]

## Side effects

- **E1** After a notification is sent, success is recorded for it. On an exception, a failure is recorded. [4]
- **E2** Message-based notification types send the import-complete text through their own channel, such as Apprise, Join, Mailgun, Ntfy, Prowl, PushBullet, Pushover, SendGrid, Signal, Simplepush, Twitter, Telegram, Pushcut and Gotify. [12][13][14][15][16]
- **E3** Email sends a message with the body "All expected episode files in <message> downloaded and sorted.". [17]
- **E4** Discord posts an embed titled with the series and episodes, with description "Import Complete" and optional fields from its import field settings. [18]
- **E5** Slack posts "Imported all expected episodes: <message>" with a green attachment. [19]
- **E6** Custom Script runs the configured script with environment variables describing the series, episodes, files, release, download and paths, with event type "Download". [20][21]
- **E7** Webhook and Notifiarr send an import-complete payload built from the message. [22][23][24]
- **E8** Trakt refreshes its token if needed and adds each imported episode file to the Trakt collection. [25][26]
- **E9** Plex Server pings Plex.tv and schedules a library update for the series when UpdateLibrary is on. [27]
- **E10** Kodi (Xbmc) shows a "Sonarr - Imported" notice if Notify is on, and schedules a library update and clean if UpdateLibrary or CleanLibrary is on. [28][29][30]
- **E11** Synology Indexer asks the indexer to update the series folder when UpdateLibrary is on. [9]

## Couldn't see

- the depth limit left out 174 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetFullSeasonMessage(NzbDrone.Core.Tv.Series,System.Int32,NzbDrone.Core.Qualities.QualityModel)`
2. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetMessage(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},NzbDrone.Core.Qualities.QualityModel)`
3. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetQualityString(NzbDrone.Core.Tv.Series,NzbDrone.Core.Qualities.QualityModel)`
4. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.UntrackedDownloadCompletedEvent)`
5. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.GetLongestCommonPath(System.Collections.Generic.List{System.String})`
6. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.OnImportCompleteEnabled(System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.ShouldHandleSeries(NzbDrone.Core.ThingiProvider.ProviderDefinition,NzbDrone.Core.Tv.Series)`
8. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.FilterBlockedNotifications(System.Collections.Generic.IEnumerable{NzbDrone.Core.Notifications.INotification})`
9. `Sonarr.Core|M:NzbDrone.Core.Notifications.Synology.SynologyIndexer.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
10. `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowser.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
11. `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowser.UpdateIfEnabled(NzbDrone.Core.Tv.Series,System.String)`
12. `Sonarr.Core|M:NzbDrone.Core.Notifications.Apprise.Apprise.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
13. `Sonarr.Core|M:NzbDrone.Core.Notifications.Join.Join.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
14. `Sonarr.Core|M:NzbDrone.Core.Notifications.Telegram.Telegram.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
15. `Sonarr.Core|M:NzbDrone.Core.Notifications.Twitter.Twitter.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
16. `Sonarr.Core|M:NzbDrone.Core.Notifications.Gotify.Gotify.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
17. `Sonarr.Core|M:NzbDrone.Core.Notifications.Email.Email.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
18. `Sonarr.Core|M:NzbDrone.Core.Notifications.Discord.Discord.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
19. `Sonarr.Core|M:NzbDrone.Core.Notifications.Slack.Slack.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
20. `Sonarr.Core|M:NzbDrone.Core.Notifications.CustomScript.CustomScript.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
21. `Sonarr.Core|M:NzbDrone.Core.Notifications.CustomScript.CustomScript.ExecuteScript(System.Collections.Specialized.StringDictionary)`
22. `Sonarr.Core|M:NzbDrone.Core.Notifications.Webhook.Webhook.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
23. `Sonarr.Core|M:NzbDrone.Core.Notifications.Notifiarr.Notifiarr.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
24. `Sonarr.Core|M:NzbDrone.Core.Notifications.Webhook.WebhookBase`1.BuildOnImportCompletePayload(NzbDrone.Core.Notifications.ImportCompleteMessage)`
25. `Sonarr.Core|M:NzbDrone.Core.Notifications.Trakt.Trakt.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
26. `Sonarr.Core|M:NzbDrone.Core.Notifications.Trakt.Trakt.AddEpisodeToCollection(NzbDrone.Core.Notifications.Trakt.TraktSettings,NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)`
27. `Sonarr.Core|M:NzbDrone.Core.Notifications.Plex.Server.PlexServer.UpdateIfEnabled(NzbDrone.Core.Tv.Series)`
28. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
29. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.Notify(NzbDrone.Core.Notifications.Xbmc.XbmcSettings,System.String,System.String)`
30. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.UpdateAndClean(NzbDrone.Core.Tv.Series,System.Boolean)`

<sub>commit 0bffefe · inputs 9c39fc5 · slice of 291 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
