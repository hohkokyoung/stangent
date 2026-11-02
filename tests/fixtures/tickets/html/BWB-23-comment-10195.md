_JensSpanier wrote:_

I have the same problem so I tried setting these entries:

```
globalSettings__attachment__baseDirectory=/etc/bitwarden/bwdata/core/attachments
globalSettings__attachment__baseUrl=https://bitwarden.XXXX.de/attachments
globalSettings__send__baseDirectory=/etc/bitwarden/bwdata/core/attachments/send
globalSettings__send__baseUrl=https://bitwarden.XXXX.de/attachments/send
```

First thing I noticed after uploading a file was that no attachment and no send file appears in `/etc/bitwarden/bwdata/core/attachments` or `/etc/bitwarden/bwdata/core/attachments/send`. I couldn't find theme anywhere.

When I try to download a file this error pops up:

![image](https://user-images.githubusercontent.com/42373861/120381241-78ec7980-c322-11eb-9268-ba35e6b24fe7.png)

By the way - are these variables documented somewhere? I just found [this](https://bitwarden.com/help/article/environment-variables/) and [this](https://bitwarden.com/help/article/send-hosting/).
