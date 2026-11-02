_JensSpanier wrote:_

I got it working with these settings:

```
globalSettings__attachment__baseDirectory=/etc/bitwarden/core/attachments
globalSettings__attachment__baseUrl=https://bitwarden.XXXX.de/attachments
globalSettings__send__baseDirectory=/etc/bitwarden/bwdata/core/attachments/send
globalSettings__send__baseUrl=https://bitwarden.XXXX.de/attachments/send
```

Don't change `globalSettings__attachment__baseDirectory` and `globalSettings__send__baseDirectory`. You just need to adjust `globalSettings__attachment__baseUrl` and `globalSettings__send__baseUrl`.

`globalSettings__attachment__baseDirectory` and `globalSettings__send__baseDirectory` are the paths inside the docker container.

So your paths must start with `/etc/bitwarden/core` because this volume gets mapped to `../core` (see `docker-compose.yml`)
