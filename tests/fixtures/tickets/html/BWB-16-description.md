### Steps To Reproduce

Export Vault from Standard Self-Hosted Deployment, File format ".json"
Import Data to Unified Deployment, File format "Bitwarden (json)"

![before](https://user-images.githubusercontent.com/8851893/208418565-cf13e55a-6ba1-4fd2-8ca0-62fffc963511.PNG)

![after](https://user-images.githubusercontent.com/8851893/208418583-c2a4190d-196c-4c01-997d-dd805b7d473d.PNG)

### Expected Result

Item binding to Folder

### Actual Result

Item not binding to Folder

### Screenshots or Videos

_No response_

### Additional Context

_No response_

### Githash Version

a791f930-dirty

### Environment Details

_No response_

### Database Image

"postgres:14" doesn't work.
"mcr.microsoft.com/mssql/server:2019-latest" and "mariadb:10" working fine.

### Issue-Link

https://github.com/bitwarden/server/issues/2480

### Issue Tracking Info

- [x] I understand that work is tracked outside of Github. A PR will be linked to this issue should one be opened to address it, but Bitwarden doesn't use fields like "assigned", "milestone", or "project" to track progress.
