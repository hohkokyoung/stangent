## Describe the Bug

Self-hosted Send files cannot be downloaded.

## Steps To Reproduce

1. Create a new send to share a file.
2. Open link to the file.
3. Click download.
4. The button will change to a spinning icon but nothing happens.

## Expected Result

File should download.

## Actual Result

File does not download.

## Screenshots or Videos

N/A

## Environment

- Operating system: Ubuntu 18.04.5 LTS
- Build Version: 1.41.3
- Self Hosted: true
- Environment: Cloud
- Hardware: Hosted VPS

## Additional Context

Doing some digging through various logs led to nothing.
After browsing the bwdata directory I stumbled upon something that may or may not have anything to do with the problem.
In _~/bwdata/core_ I see two directories. One is named **_attachments_** while the other is named **_attchments_**. The file I uploaded is stored within _attchments_ while the _attachments_ directory is empty. In _~/bwdata/nginx/default.conf_ the location there points to _attachments_.
I see that in server/src/Core/Settings/GlobalSettings.cs on lines 13 and 14 it mentions _attchments_.
I don't know if this is intentional, by design, or a typo.
