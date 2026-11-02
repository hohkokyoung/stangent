_c4539 wrote:_

Have you checked the `./bwdata/env/global.override.env` for these entries?

```
globalSettings__attachment__baseDirectory=/etc/bitwarden/core/attachments
globalSettings__attachment__baseUrl=https://vault.example.net/attachments
globalSettings__send__baseDirectory=/etc/bitwarden/core/attachments/send
globalSettings__send__baseUrl=https://vault.example.net/attachments/send
```

That should override the typo you found in `server/src/Core/Settings/GlobalSettings.cs`.
