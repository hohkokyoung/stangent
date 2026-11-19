# List quality profiles (GET /api/v3/QualityProfile)

`endpoint:Sonarr.Api.V3|GET /api/v3/QualityProfile` · backend · Sonarr.Api.V3

From the code: GET /api/v3/QualityProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all quality profiles. [1]
- **S2** Each profile has id, name, upgrade allowed flag, cutoff, items, minimum format score, cutoff format score, minimum upgrade format score and format items. [2]
- **S3** Each quality item shows id, name, quality, nested items, allowed flag, min size, max size and preferred size. [3]
- **S4** Each format item shows the custom format id, its name and its score. [4]

## Actions

- **A1** A caller sends GET to /api/v3/QualityProfile and receives all stored quality profiles converted to API resources. [5][1] → `endpoint:Sonarr.Api.V3|GET /api/v3/QualityProfile`

## Rules

- **R1** The profiles are read from the quality profile repository with no filtering, and returned as a list. [6]
- **R2** A null profile or quality item maps to null. [2][3]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 29 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.QualityProfileController.GetAll`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.Qualities.QualityProfile)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.Qualities.QualityProfileQualityItem)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper.ToResource(NzbDrone.Core.Profiles.ProfileFormatItem)`
5. `endpoint:Sonarr.Api.V3|GET /api/v3/QualityProfile`
6. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.All`

<sub>commit 0bffefe · inputs 91c6303 · slice of 30 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
