# Disk Space (GET /api/v3/diskspace)

`endpoint:Sonarr.Api.V3|GET /api/v3/diskspace` · backend · Sonarr.Api.V3

From the code: GET /api/v3/diskspace

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of disk space entries, each with Path, Label, FreeSpace and TotalSpace. [1][2]
- **S2** The list covers the path roots of folders holding series, plus the root directories of fixed or network drives that are not already included. [3][4]
- **S3** Entries are sorted by path, ignoring case. [3]

## Actions

- **A1** A caller sends GET /api/v3/diskspace to get free and total space for the relevant disks. [5][1] → `endpoint:Sonarr.Api.V3|GET /api/v3/diskspace`

## Rules

- **R1** Series root paths are found by taking each series path that is valid for the current OS, finding its best root folder, keeping only root folders that exist, and using their path roots, with duplicates removed. [6]
- **R2** Additional disks are only those mounts of type Fixed or Network whose root directory does not match the special-drive pattern, and which are not already series roots. [4][3]
- **R3** A path is skipped if its available space or total size cannot be determined. [7]
- **R4** If reading a path fails, that path is omitted. A warning "Unable to get free space for: <path>" is logged for series roots but not for the additional fixed disks. [7][3]

## Side effects

- **E1** Failures reading a series root path write a warning to the log. Otherwise the call only reads data. [7]

## Couldn't see

- the depth limit left out 17 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.DiskSpace.DiskSpaceController.GetFreeSpace`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.DiskSpace.DiskSpaceResourceMapper.MapToResource(NzbDrone.Core.DiskSpace.DiskSpace)`
3. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetFreeSpace`
4. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetFixedDisksRootPaths`
5. `endpoint:Sonarr.Api.V3|GET /api/v3/diskspace`
6. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetSeriesRootPaths`
7. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetDiskSpace(System.Collections.Generic.IEnumerable{System.String},System.Boolean)`

<sub>commit 0bffefe · inputs 11be270 · slice of 20 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
