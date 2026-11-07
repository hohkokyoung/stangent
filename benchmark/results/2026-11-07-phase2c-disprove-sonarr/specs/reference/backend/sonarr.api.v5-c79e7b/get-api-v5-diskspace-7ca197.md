# Disk space list (GET /api/v5/diskspace)

`endpoint:Sonarr.Api.V5|GET /api/v5/diskspace` · backend · Sonarr.Api.V5

From the code: GET /api/v5/diskspace

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of disk space entries, each with Path, Label, FreeSpace and TotalSpace. [1][2]
- **S2** The list covers the root paths of drives holding series, plus other fixed or network drives. [3][4]

## Actions

- **A1** A caller requests the free space of all relevant disks and receives the list as JSON. [5][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/diskspace`

## Rules

- **R1** Series disks are found by taking each series path that is valid for the current OS, mapping it to its best root folder, keeping only existing folders, and using the distinct path roots. [6]
- **R2** Additional disks are mounts of type Fixed or Network whose root directory does not match the special-drive pattern, excluding those already in the series list. [4][3]
- **R3** The result is sorted by path, case-insensitively. [3]
- **R4** A path is skipped if its free space or total size cannot be determined. [7]
- **R5** If reading a path throws an error, that path is left out. A warning is logged for series paths but not for the additional fixed or network disks. [7][3]

## Side effects

- **E1** Writes a warning log "Unable to get free space for: <path>" when a series disk cannot be read. [7]

## Couldn't see

- the depth limit left out 17 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.DiskSpace.DiskSpaceController.GetFreeSpace`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.DiskSpace.DiskSpaceResourceMapper.MapToResource(NzbDrone.Core.DiskSpace.DiskSpace)`
3. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetFreeSpace`
4. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetFixedDisksRootPaths`
5. `endpoint:Sonarr.Api.V5|GET /api/v5/diskspace`
6. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetSeriesRootPaths`
7. `Sonarr.Core|M:NzbDrone.Core.DiskSpace.DiskSpaceService.GetDiskSpace(System.Collections.Generic.IEnumerable{System.String},System.Boolean)`

<sub>commit 0bffefe · inputs b9a68f5 · slice of 20 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
