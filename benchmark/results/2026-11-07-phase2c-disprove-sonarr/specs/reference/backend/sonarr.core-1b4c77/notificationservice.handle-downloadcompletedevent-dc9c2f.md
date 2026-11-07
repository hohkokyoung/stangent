# Import-complete notifications on download completed

`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)` · backend · Sonarr.Core

From the code: NotificationService.Handle(DownloadCompletedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Builds an import-complete message containing the series, episodes, episode files, download client info, download id, release, source path, destination path, release group and release quality. [1]
- **S2** For a full-season download the message reads "<series> - Season <n> [<quality>]". [2]
- **S3** For daily series the message reads "<series> - <air date> - <episode title> [<quality>]", using the first episode. [3]
- **S4** For other series the message reads "<series> - <season>x<nn>... - <titles joined with ' + '> [<quality>]". With no episodes it is "<series> - [<quality>]". [3]
- **S5** The quality text is the quality name, followed by " v<version>" for anime or " Proper" for other series when the revision version is above 1. [4]
- **S6** The destination path is the longest common folder path of the imported episode files (series path joined with each file's relative path). [1][5]

## Actions

- **A1** On a completed download, sends the import-complete message to every notification that has On Import Complete enabled and is not temporarily blocked. [1][6][7]
- **A2** Email sends an email with the body "All expected episode files in <message> downloaded and sorted.". [8]
- **A3** Slack posts a payload "Imported all expected episodes: <message>" with a good-coloured attachment titled with the series title. [9]
- **A4** Twitter sends "Imported: <message>". [10]
- **A5** Discord sends an embed with description "Import Complete", a series/episode title and a link to the series on TheTVDB. Fields are added according to the configured import fields. [11]
- **A6** Custom Script runs the configured script with environment variables describing the instance, series, episode files, episodes, download, release, source and destination paths. [12][13]
- **A7** Webhook and Notifiarr send an import-complete payload with event type Download, series, episodes, episode files, release, download client, download id, source and destination paths. [14][15][16]
- **A8** Trakt refreshes its token if needed and adds each imported episode file to the Trakt collection. [17][18]
- **A9** Synology Indexer updates the series folder when Update Library is on. [19]
- **A10** Kodi (Xbmc) shows a "Sonarr - Imported" notification and schedules a library update/clean. [20][21]
- **A11** Emby/MediaBrowser notifies when Notify is on, and schedules a library update when Update Library is on. Plex Server schedules a library update when Update Library is on. [22][23][24]
- **A12** Apprise, Gotify, Join, Mailgun, Ntfy, Prowl, PushBullet, Pushcut, Pushover, SendGrid, Signal, Simplepush and Telegram each send the message text through their own service. [25][26][27][28][29][30][31][32][33][34][35][36][37]

## Rules

- **R1** A notification with no tags always receives the message. With tags, the series must share at least one tag; if the series is unknown it is not sent. [38]
- **R2** A notification is only sent when its On Import Complete flag is set. [1][6]
- **R3** Notifications temporarily blocked due to recent failures are skipped. [7]
- **R4** A failure in one notification is logged as a warning and does not stop the others. [1]
- **R5** Discord titles longer than 256 characters are truncated with "...", and Overview values over 300 characters are cut to 300 plus "...". [39][11]

## Side effects

- **E1** Records a success for a notification after it is sent, and a failure when sending throws. [1][40][41]
- **E2** Sends messages to external services, runs a custom script, and queues media-server library updates, as each configured notification does. [13][21][42]

## Couldn't see

- the depth limit left out 174 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.Download.DownloadCompletedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetFullSeasonMessage(NzbDrone.Core.Tv.Series,System.Int32,NzbDrone.Core.Qualities.QualityModel)`
3. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetMessage(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},NzbDrone.Core.Qualities.QualityModel)`
4. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetQualityString(NzbDrone.Core.Tv.Series,NzbDrone.Core.Qualities.QualityModel)`
5. `Sonarr.Common|M:NzbDrone.Common.Extensions.PathExtensions.GetLongestCommonPath(System.Collections.Generic.List{System.String})`
6. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.OnImportCompleteEnabled(System.Boolean)`
7. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.FilterBlockedNotifications(System.Collections.Generic.IEnumerable{NzbDrone.Core.Notifications.INotification})`
8. `Sonarr.Core|M:NzbDrone.Core.Notifications.Email.Email.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
9. `Sonarr.Core|M:NzbDrone.Core.Notifications.Slack.Slack.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
10. `Sonarr.Core|M:NzbDrone.Core.Notifications.Twitter.Twitter.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
11. `Sonarr.Core|M:NzbDrone.Core.Notifications.Discord.Discord.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
12. `Sonarr.Core|M:NzbDrone.Core.Notifications.CustomScript.CustomScript.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
13. `Sonarr.Core|M:NzbDrone.Core.Notifications.CustomScript.CustomScript.ExecuteScript(System.Collections.Specialized.StringDictionary)`
14. `Sonarr.Core|M:NzbDrone.Core.Notifications.Webhook.Webhook.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
15. `Sonarr.Core|M:NzbDrone.Core.Notifications.Notifiarr.Notifiarr.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
16. `Sonarr.Core|M:NzbDrone.Core.Notifications.Webhook.WebhookBase`1.BuildOnImportCompletePayload(NzbDrone.Core.Notifications.ImportCompleteMessage)`
17. `Sonarr.Core|M:NzbDrone.Core.Notifications.Trakt.Trakt.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
18. `Sonarr.Core|M:NzbDrone.Core.Notifications.Trakt.Trakt.AddEpisodeToCollection(NzbDrone.Core.Notifications.Trakt.TraktSettings,NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)`
19. `Sonarr.Core|M:NzbDrone.Core.Notifications.Synology.SynologyIndexer.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
20. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
21. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.UpdateAndClean(NzbDrone.Core.Tv.Series,System.Boolean)`
22. `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowser.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
23. `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowser.UpdateIfEnabled(NzbDrone.Core.Tv.Series,System.String)`
24. `Sonarr.Core|M:NzbDrone.Core.Notifications.Plex.Server.PlexServer.UpdateIfEnabled(NzbDrone.Core.Tv.Series)`
25. `Sonarr.Core|M:NzbDrone.Core.Notifications.Apprise.Apprise.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
26. `Sonarr.Core|M:NzbDrone.Core.Notifications.Gotify.Gotify.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
27. `Sonarr.Core|M:NzbDrone.Core.Notifications.Join.Join.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
28. `Sonarr.Core|M:NzbDrone.Core.Notifications.Mailgun.MailGun.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
29. `Sonarr.Core|M:NzbDrone.Core.Notifications.Ntfy.Ntfy.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
30. `Sonarr.Core|M:NzbDrone.Core.Notifications.Prowl.Prowl.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
31. `Sonarr.Core|M:NzbDrone.Core.Notifications.PushBullet.PushBullet.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
32. `Sonarr.Core|M:NzbDrone.Core.Notifications.Pushcut.Pushcut.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
33. `Sonarr.Core|M:NzbDrone.Core.Notifications.Pushover.Pushover.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
34. `Sonarr.Core|M:NzbDrone.Core.Notifications.SendGrid.SendGrid.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
35. `Sonarr.Core|M:NzbDrone.Core.Notifications.Signal.Signal.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
36. `Sonarr.Core|M:NzbDrone.Core.Notifications.Simplepush.Simplepush.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
37. `Sonarr.Core|M:NzbDrone.Core.Notifications.Telegram.Telegram.OnImportComplete(NzbDrone.Core.Notifications.ImportCompleteMessage)`
38. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.ShouldHandleSeries(NzbDrone.Core.ThingiProvider.ProviderDefinition,NzbDrone.Core.Tv.Series)`
39. `Sonarr.Core|M:NzbDrone.Core.Notifications.Discord.Discord.GetTitle(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode})`
40. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.IProviderStatusServiceBase`1.RecordSuccess(System.Int32)`
41. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.IProviderStatusServiceBase`1.RecordFailure(System.Int32,System.TimeSpan)`
42. `Sonarr.Core|M:NzbDrone.Core.Notifications.Email.Email.SendEmail(NzbDrone.Core.Notifications.Email.EmailSettings,System.String,System.String,System.Boolean)`

<sub>commit 0bffefe · inputs 3f53bce · slice of 298 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
