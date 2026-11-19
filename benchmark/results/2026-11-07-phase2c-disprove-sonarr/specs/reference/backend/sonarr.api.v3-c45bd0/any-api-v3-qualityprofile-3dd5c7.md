# Quality Profile API (create, update, delete, get)

`endpoint:Sonarr.Api.V3|ANY /api/v3/QualityProfile` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/QualityProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Getting a quality profile by id returns it as JSON with 200 OK, or 404 Not Found when the profile does not exist. [1]
- **S2** A quality profile carries an id, name, upgrade-allowed flag, cutoff, quality items, minimum format score, cutoff format score, minimum upgrade format score and custom format items (format id and score). [2][3]
- **S3** Each quality item has an id, name, quality, nested items, allowed flag, min size, max size and preferred size. [4]

## Actions

- **A1** Create a quality profile with a JSON body. The saved profile is returned as a created response pointing to the get-by-id action. [5][6] → `endpoint:Sonarr.Api.V3|ANY /api/v3/QualityProfile`
- **A2** Update a quality profile with a JSON body. The updated profile is returned as an accepted response pointing to the get-by-id action. [7][8] → `endpoint:Sonarr.Api.V3|ANY /api/v3/QualityProfile`
- **A3** Delete a quality profile by id. [9] → `endpoint:Sonarr.Api.V3|ANY /api/v3/QualityProfile`

## Rules

- **R1** A profile cannot be deleted while any series or import list uses it. The call fails with 400 Bad Request and the message "QualityProfile [name] is in use." [10][11]
- **R2** On PUT, if the body resource id is 0 and the route has an id, the route id is used. [12]
- **R3** POST and PUT bodies are validated before the action runs, and the id argument is validated for actions that require it unless validation is skipped. [12]
- **R4** Create and update accept only application/json request bodies. [5][7]

## Side effects

- **E1** Creating a profile inserts it and then updates its ranks. [13]
- **E2** Updating a profile saves it, updates its ranks and publishes a QualityProfileUpdatedEvent. [14]
- **E3** The pending release service handles the QualityProfileUpdatedEvent. [15]
- **E4** Deleting a profile removes it and then deletes its ranks. [10]
- **E5** A call to a deprecated endpoint logs a warning and sets the response header Deprecation: true. [12]

## Couldn't see

- the depth limit left out 12 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper.ToModel(Sonarr.Api.V3.Profiles.Quality.QualityProfileResource)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper.ToModel(Sonarr.Api.V3.Profiles.Quality.ProfileFormatItemResource)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper.ToModel(Sonarr.Api.V3.Profiles.Quality.QualityProfileQualityItemResource)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.QualityProfileController.Create(Sonarr.Api.V3.Profiles.Quality.QualityProfileResource)`
6. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
7. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.QualityProfileController.Update(Sonarr.Api.V3.Profiles.Quality.QualityProfileResource)`
8. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
9. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Quality.QualityProfileController.DeleteProfile(System.Int32)`
10. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Delete(System.Int32)`
11. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileInUseException.#ctor(System.String)`
12. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
13. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Add(NzbDrone.Core.Profiles.Qualities.QualityProfile)`
14. `Sonarr.Core|M:NzbDrone.Core.Profiles.Qualities.QualityProfileService.Update(NzbDrone.Core.Profiles.Qualities.QualityProfile)`
15. [`Sonarr.Core|M:NzbDrone.Core.Download.Pending.PendingReleaseService.Handle(NzbDrone.Core.Profiles.Qualities.QualityProfileUpdatedEvent)`](../../backend/sonarr.core-1b4c77/pendingreleaseservice.handle-qualityprofileupdatedevent-da3b5f.md)

<sub>commit 0bffefe · inputs 6fc2ce4 · slice of 92 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
