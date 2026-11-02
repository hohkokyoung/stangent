_JensSpanier wrote:_

> In docker-compose.yml it specificies either "../core/attachments" or "/etc/bitwarden/core/attachments".

You are talking about this line in `docker-compose.yml`, right?
`../core/attachments:/etc/bitwarden/core/attachments`

`../core/attachments` is the source path on your host relative to your `docker-compose.yml` (so in your case it's `/root/bwdata/core/attachments`)
`/etc/bitwarden/core/attachments` is the target path inside the docker container

So you do have `/etc/bitwarden`, but only inside your containers (it's like a separate virtual linux host).

The only thing you can change with `globalSettings__attachment__baseDirectory` and `globalSettings__send__baseDirectory` is the path where it gets mounted inside your docker container. Which doesn't make much sense for me. But because there is this typo, you have to set this.
