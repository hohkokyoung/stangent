# List delay profiles (GET /api/v5/DelayProfile)

`endpoint:Sonarr.Api.V5|GET /api/v5/DelayProfile` · backend · Sonarr.Api.V5

From the code: GET /api/v5/DelayProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all delay profiles. [1]
- **S2** Each profile shows Id, EnableUsenet, EnableTorrent, PreferredProtocol, UsenetDelay, TorrentDelay, BypassIfHighestQuality, BypassIfAboveCustomFormatScore, MinimumCustomFormatScore, Order and Tags. [2]
- **S3** Tags is returned as a set of integer tag ids. [2]

## Actions

- **A1** A caller sends GET to retrieve every stored delay profile, with no parameters. [1][3] → `endpoint:Sonarr.Api.V5|GET /api/v5/DelayProfile`

## Rules

- **R1** No filtering or paging is applied; all profiles from the repository are returned. [4][1]
- **R2** The response is application/json with an Ok result. [1]

## Side effects

- **E1** Read-only: it loads all delay profiles from the data store through the repository and changes nothing. [4][5]

## Couldn't see

- the depth limit left out 6 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Delay.DelayProfileController.GetAll`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Delay.DelayProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.Delay.DelayProfile)`
3. `endpoint:Sonarr.Api.V5|GET /api/v5/DelayProfile`
4. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.All`
5. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.All`

<sub>commit 0bffefe · inputs 8cf0286 · slice of 32 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
