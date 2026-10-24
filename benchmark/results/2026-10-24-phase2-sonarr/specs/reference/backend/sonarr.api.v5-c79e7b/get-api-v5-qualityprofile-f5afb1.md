# List quality profiles (GET /api/v5/QualityProfile)

`endpoint:Sonarr.Api.V5|GET /api/v5/QualityProfile` · backend · Sonarr.Api.V5

From the code: GET /api/v5/QualityProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all quality profiles, each with id, name, upgrade-allowed flag, cutoff, quality items, minimum format score, cutoff format score, minimum upgrade format score and format items. [1][2]
- **S2** Each quality item shows id, name, quality, nested items, allowed flag, min size, max size and preferred size. [3]
- **S3** Each format item shows the custom format id (as Format), its name and its score. [4]

## Actions

- **A1** A caller sends GET to /api/v5/QualityProfile and receives all quality profiles with an OK response. [5][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/QualityProfile`

## Rules

- **R1** No filtering or paging is applied; every stored quality profile is returned. [6][1]

## Side effects

- **E1** Read-only: the profiles are read from the quality profile repository and nothing is changed. [6][7]

## Couldn't see

- the depth limit left out 28 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.QualityProfileController.GetAll`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.Qualities.QualityProfile)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.Qualities.QualityProfileQualityItem)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.ProfileFormatItem)`
5. `endpoint:Sonarr.Api.V5|GET /api/v5/QualityProfile`
6. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.All`
7. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.All`

<sub>commit 0bffefe · inputs 231a462 · slice of 30 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
