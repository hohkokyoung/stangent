_dlrudie wrote:_

They don't have to start with /etc/bitwarden/*. In docker-compose.yml it specificies either "../core/attachments" or "/etc/bitwarden/core/attachments". I don't have a /etc/bitwarden at all. Everything is contained within /root/bwdata and the rest of the directories work just fine.

The ../core/attachments is valid on mine. I think my problem comes from the fact that the source has a typo for the directory and it's messing with everything else. The URL's are fine on mine and I don't see any reason to change the baseUrl settings.

If I don't add those four lines, whenever I create a file send it will end up in bwdata/core/attchments with the typo. I can delete the send and it will properly delete the file too. I just can't download it. I've tried renaming bwdata/core/attachments and creating a symlink in its place that points to attchments but it still didn't work.
