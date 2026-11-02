### Steps To Reproduce

1. Login in with wrong credentials
2. Look at `logs/identity/Identity/20230505.txt`

### Expected Result

Expected to find my real IP logged in the identity logs.

### Actual Result

Instead the following is logged:

```
2023-05-05 11:26:02.733 +00:00 [Warning] Failed login attempt. ::ffff:172.29.60.10
```

But my reverse proxy and nginx are properly configured, since I see in `logs/nginx/access.log`:

```
xxx.xxx.xxx.xxx - - [05/May/2023:11:28:20 +0000] "POST /identity/accounts/prelogin HTTP/1.1" 200 86 "https://url/" "Mozilla/5.0 (Android 13; Mobile; rv:109.0) Gecko/112.0 Firefox/112.0" "xxx.xxx.xxx.xxx "
```

where `xxx.xxx.xxx.xxx` is my actual IP.

### Screenshots or Videos

_No response_

### Additional Context

I run Bitwarden behind a Traefik server that correctly sets the headers and I configured bitwardens nginx to trust the reverse proxy in the `config.yml`:

```
real_ips:
- 172.16.0.0/12
```

### Build Version

2023.4.3

### Environment

Self-Hosted

### Environment Details

- Linux
- Docker & Docker Compose via the official `bitwarden.sh`

### Issue Tracking Info

- [x] I understand that work is tracked outside of Github. A PR will be linked to this issue should one be opened to address it, but Bitwarden doesn't use fields like "assigned", "milestone", or "project" to track progress.
