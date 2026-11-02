_Ayitaka wrote:_

> ![image](https://user-images.githubusercontent.com/50854322/182734510-8d1a8f34-f199-4982-b529-ddb7cfb9aeaf.png)
>
> The admin dashboard is now showing 2022.8.0 as the latest version, while bitwarden/server shows 2022.6.2 as the latest.

This is because the [Bitwarden/server](https://github.com/bitwarden/server) version was bumped to 2022.8.0, for the new month. But that is not the [Bitwarden/self-host](https://github.com/bitwarden/self-host). self-host has its own versions, which always lag behind Bitwarden/server so that a release can be tested before being pushed to self-host clients.

Having said that, maybe the admin dashboard for self-host should only show the latest version for self-host, not for server, because that can be confusing... case in point.
