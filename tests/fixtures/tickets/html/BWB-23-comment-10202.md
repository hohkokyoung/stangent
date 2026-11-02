_dlrudie wrote:_

Thanks. That worked now.
So for mine to work it really needed all four of those lines as @c4539 called out.

```
globalSettings__attachment__baseDirectory=/etc/bitwarden/core/attachments
globalSettings__attachment__baseUrl=https://site.com/attachments
globalSettings__send__baseDirectory=/etc/bitwarden/core/attachments/send
globalSettings__send__baseUrl=https://site.com/attachments/send
```

I just got confused about the /etc/bitwarden path and was trying to change it to /root/bwdata instead.

Will these settings be future-proof or if the typo gets fixed in the source will I need to undo these?

Thanks, @c4539 @Virinum
