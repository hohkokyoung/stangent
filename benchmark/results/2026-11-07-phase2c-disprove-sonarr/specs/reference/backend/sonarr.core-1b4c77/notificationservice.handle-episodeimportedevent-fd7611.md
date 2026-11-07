# Episode imported: send "On Download" notifications

`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.MediaFiles.Events.EpisodeImportedEvent)` · backend · Sonarr.Core

From the code: NotificationService.Handle(EpisodeImportedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Builds a download message containing a summary text, the series, episode info, imported episode file, old files, source path, download client info, download id and release. [1]
- **S2** The summary text is "Series - [quality]" when there are no episodes. [2]
- **S3** For daily series the summary text is "Series - air date - episode title [quality]" using the first episode. [2]
- **S4** For other series the summary text is "Series - season + xNN episode numbers - titles joined by ' + ' [quality]". [2]
- **S5** The quality string is the quality name; when the revision version is above 1 it appends " v<version>" for anime series and " Proper" for others. [3]

## Actions

- **A1** On an episode imported event, sends the download message to each enabled On Download notification connection that passes the tag and upgrade checks. [1][4]

## Rules

- **R1** Nothing is sent unless the import is a new download. [1]
- **R2** Only notifications with On Download enabled are used, and those temporarily blocked because of recent failures are skipped. [4][5]
- **R3** A notification with no tags handles every series. With tags, it requires a known series sharing at least one tag; otherwise it is not sent. [6]
- **R4** When the import replaced old files, the notification is sent only if its On Upgrade option is enabled; with no old files it is always sent. [1]
- **R5** A failure in one notification is logged as a warning and does not stop the others. [1]

## Side effects

- **E1** Records a success for the notification after a send, and records a failure when sending throws. [1][7][8]
- **E2** The Webhook notification sends a download webhook payload. [9]
- **E3** The Email notification sends an email with the message followed by "Downloaded and sorted." [10]
- **E4** The Custom Script notification runs the configured script with Sonarr_* environment variables describing the series, episode file, release and deleted files. [11][12]
- **E5** The Discord notification posts an embed whose description is "Episode Imported" or "Episode Upgraded" and whose title is built from the series and episode titles, with the selected import fields. [13][14]
- **E6** The Synology Indexer, when Update Library is on, deletes old file paths and adds the new file path. [15]
- **E7** Plex Server, Emby and Kodi notifications schedule a library update when their update options are enabled. [16][17][18]
- **E8** The Trakt notification refreshes its token if needed and adds the imported episodes to the Trakt collection. [19][20]
- **E9** Other connections (Apprise, Gotify, Join, Mailgun, Notifiarr, Ntfy, Prowl, PushBullet, Pushcut, Pushover, SendGrid, Signal, Simplepush, Slack, Telegram, Twitter) send the message to their service. [21][22][23][24][25]

## Couldn't see

- the depth limit left out 199 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on E5 (contradicted): "Episode Imported"/"Episode Upgraded" is the embed description. The embed title comes from GetTitle (series and episode title). **fixed**: Reworded: "Episode Imported"/"Episode Upgraded" is the embed description; the title comes from GetTitle (series and episode titles). Added GetTitle citation.

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.MediaFiles.Events.EpisodeImportedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetMessage(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode},NzbDrone.Core.Qualities.QualityModel)`
3. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.GetQualityString(NzbDrone.Core.Tv.Series,NzbDrone.Core.Qualities.QualityModel)`
4. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.OnDownloadEnabled(System.Boolean)`
5. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationFactory.FilterBlockedNotifications(System.Collections.Generic.IEnumerable{NzbDrone.Core.Notifications.INotification})`
6. `Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.ShouldHandleSeries(NzbDrone.Core.ThingiProvider.ProviderDefinition,NzbDrone.Core.Tv.Series)`
7. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.IProviderStatusServiceBase`1.RecordSuccess(System.Int32)`
8. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.IProviderStatusServiceBase`1.RecordFailure(System.Int32,System.TimeSpan)`
9. `Sonarr.Core|M:NzbDrone.Core.Notifications.Webhook.Webhook.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
10. `Sonarr.Core|M:NzbDrone.Core.Notifications.Email.Email.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
11. `Sonarr.Core|M:NzbDrone.Core.Notifications.CustomScript.CustomScript.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
12. `Sonarr.Core|M:NzbDrone.Core.Notifications.CustomScript.CustomScript.ExecuteScript(System.Collections.Specialized.StringDictionary)`
13. `Sonarr.Core|M:NzbDrone.Core.Notifications.Discord.Discord.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
14. `Sonarr.Core|M:NzbDrone.Core.Notifications.Discord.Discord.GetTitle(NzbDrone.Core.Tv.Series,System.Collections.Generic.List{NzbDrone.Core.Tv.Episode})`
15. `Sonarr.Core|M:NzbDrone.Core.Notifications.Synology.SynologyIndexer.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
16. `Sonarr.Core|M:NzbDrone.Core.Notifications.Plex.Server.PlexServer.UpdateIfEnabled(NzbDrone.Core.Tv.Series)`
17. `Sonarr.Core|M:NzbDrone.Core.Notifications.Emby.MediaBrowser.UpdateIfEnabled(NzbDrone.Core.Tv.Series,System.String)`
18. `Sonarr.Core|M:NzbDrone.Core.Notifications.Xbmc.Xbmc.UpdateAndClean(NzbDrone.Core.Tv.Series,System.Boolean)`
19. `Sonarr.Core|M:NzbDrone.Core.Notifications.Trakt.Trakt.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
20. `Sonarr.Core|M:NzbDrone.Core.Notifications.Trakt.Trakt.AddEpisodeToCollection(NzbDrone.Core.Notifications.Trakt.TraktSettings,NzbDrone.Core.Tv.Series,NzbDrone.Core.MediaFiles.EpisodeFile)`
21. `Sonarr.Core|M:NzbDrone.Core.Notifications.Apprise.Apprise.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
22. `Sonarr.Core|M:NzbDrone.Core.Notifications.Gotify.Gotify.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
23. `Sonarr.Core|M:NzbDrone.Core.Notifications.Slack.Slack.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
24. `Sonarr.Core|M:NzbDrone.Core.Notifications.Telegram.Telegram.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`
25. `Sonarr.Core|M:NzbDrone.Core.Notifications.Twitter.Twitter.OnDownload(NzbDrone.Core.Notifications.DownloadMessage)`

<sub>commit 0bffefe · inputs d4e0c39 · slice of 309 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
