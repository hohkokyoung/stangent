# List delay profiles (GET /api/v3/DelayProfile)

`endpoint:Sonarr.Api.V3|GET /api/v3/DelayProfile` · backend · Sonarr.Api.V3

From the code: GET /api/v3/DelayProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all delay profiles. [1]
- **S2** Each delay profile in the list has: Id, EnableUsenet, EnableTorrent, PreferredProtocol, UsenetDelay, TorrentDelay, BypassIfHighestQuality, BypassIfAboveCustomFormatScore, MinimumCustomFormatScore, Order and Tags (a set of tag ids). [2]

## Actions

- **A1** A caller sends GET /api/v3/DelayProfile and receives every stored delay profile, with no filtering or paging. [1][3] → `endpoint:Sonarr.Api.V3|GET /api/v3/DelayProfile`

## Rules

- **R1** If a delay profile is null, its mapped resource is null. [2]
- **R2** The list is read from the repository in the order the repository returns it. The code applies no sorting. [3][4]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 6 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Delay.DelayProfileController.GetAll`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Delay.DelayProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.Delay.DelayProfile)`
3. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.All`
4. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.All`

<sub>commit 0bffefe · inputs e0285a8 · slice of 32 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
