# Quality Profile API (v5)

`endpoint:Sonarr.Api.V5|ANY /api/v5/QualityProfile` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/QualityProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Create returns the newly created quality profile (reloaded by its id) with a location pointing to the get-by-id action, or Not Found. [1][2]
- **S2** Update returns the updated quality profile (reloaded by its id) as Accepted with a location to the get-by-id action, or Not Found. [3][4]
- **S3** Get by id returns the profile as JSON, or Not Found when the profile does not exist. [5]
- **S4** Delete returns No Content. [6]
- **S5** A profile carries name, upgrade allowed flag, cutoff, quality items (with allowed flag, min, max and preferred size, nested items), minimum, cutoff and minimum upgrade format scores, and format items (custom format id and score). [7][8][9]

## Actions

- **A1** Create a quality profile from a JSON body; the profile is saved and its ranks are updated. [1][10] → `endpoint:Sonarr.Api.V5|ANY /api/v5/QualityProfile`
- **A2** Update an existing quality profile from a JSON body. [3][11] → `endpoint:Sonarr.Api.V5|ANY /api/v5/QualityProfile`
- **A3** Delete a quality profile by id. [6][12] → `endpoint:Sonarr.Api.V5|ANY /api/v5/QualityProfile`
- **A4** Fetch a quality profile by id. [5] → `endpoint:Sonarr.Api.V5|ANY /api/v5/QualityProfile`

## Rules

- **R1** A profile cannot be deleted while any series or import list uses it; the call fails with HTTP 400 "QualityProfile [name] is in use." [12][13]
- **R2** Create and Update accept only application/json bodies. [1][3]
- **R3** On PUT, if the body id is 0 and the route has an id, the route id is used. Resources are validated on POST and PUT, and the id is validated on id-based actions unless validation is skipped. [14]
- **R4** Calls to deprecated endpoints are logged as a warning and get a "Deprecation: true" response header. [14]

## Side effects

- **E1** Create inserts the profile in the repository and updates the profile's ranks. [10]
- **E2** Update saves the profile, updates its ranks, and publishes a QualityProfileUpdatedEvent. [11]
- **E3** The pending release service handles the QualityProfileUpdatedEvent. [15]
- **E4** Delete removes the profile from the repository and deletes its ranks. [12]

## Couldn't see

- the depth limit left out 12 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.QualityProfileController.Create(Sonarr.Api.V5.Profiles.Quality.QualityProfileResource)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedCreated(System.Int32)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.QualityProfileController.Update(Sonarr.Api.V5.Profiles.Quality.QualityProfileResource)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
5. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.QualityProfileController.DeleteProfile(System.Int32)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper.ToModel(Sonarr.Api.V5.Profiles.Quality.QualityProfileResource)`
8. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper.ToModel(Sonarr.Api.V5.Profiles.Quality.QualityProfileQualityItemResource)`
9. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper.ToModel(Sonarr.Api.V5.Profiles.Quality.ProfileFormatItemResource)`
10. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Add(NzbDrone.Core.Profiles.Qualities.QualityProfile)`
11. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Update(NzbDrone.Core.Profiles.Qualities.QualityProfile)`
12. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Delete(System.Int32)`
13. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileInUseException.#ctor(System.String)`
14. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
15. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Profiles.Qualities.QualityProfileUpdatedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-qualityprofileupdatedevent-da3b5f.md)

<sub>commit 0bffefe · inputs 7b32eae · slice of 92 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
