# Delay Profile API

`endpoint:Sonarr.Api.V3|ANY /api/v3/DelayProfile` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/DelayProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Fetching a delay profile by id returns the profile as JSON, or a not-found result when no such profile exists. [1]
- **S2** Create returns the newly created delay profile, with a link to fetch it by id. [2][3]
- **S3** Update returns the updated delay profile as an accepted result, with a link to fetch it by id. [4][5]

## Actions

- **A1** Create a delay profile from a JSON body; the profile is added and returned. [3]
- **A2** Update a delay profile from a JSON body; the profile is saved and returned. [5]
- **A3** Delete a delay profile by id. [6]

## Rules

- **R1** The profile with id 1 (the global delay profile) cannot be deleted; the call fails with a Method Not Allowed error saying "Cannot delete global delay profile". [6][7]
- **R2** A new profile's Order is set to the current number of stored delay profiles. [8]
- **R3** After a delete, remaining profiles are renumbered by their existing order starting at 1 for the first position, except profile id 1, whose order is left unchanged. [9]
- **R4** On PUT, if the body has no id (0) and the route has an id, the route id is used. [10]
- **R5** POST and PUT bodies are validated before the action runs, and the id is validated for id-based actions unless validation is skipped. [10]
- **R6** The profile carries usenet/torrent enablement, preferred protocol, usenet and torrent delays, bypass settings, minimum custom format score, order and a set of tags. [11]
- **R7** Calls to endpoints marked deprecated are logged as a warning and get a "Deprecation: true" response header. [10]

## Side effects

- **E1** Creating a profile inserts it in the data store and clears the best-for-tags cache. [8][12][13]
- **E2** Updating a profile saves it in the data store and clears the best-for-tags cache. [14][15][13]
- **E3** Deleting a profile removes it from the data store, saves the renumbered order of the remaining profiles and clears the best-for-tags cache. [9][16][17][13]

## Couldn't see

- the depth limit left out 8 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Delay.DelayProfileController.Create(Sonarr.Api.V3.Profiles.Delay.DelayProfileResource)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Accepted(System.Int32)`
5. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Delay.DelayProfileController.Update(Sonarr.Api.V3.Profiles.Delay.DelayProfileResource)`
6. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Delay.DelayProfileController.DeleteProfile(System.Int32)`
7. `Sonarr.Http|M:Sonarr.Http.REST.MethodNotAllowedException.#ctor(System.Object)`
8. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.Add(NzbDrone.Core.Profiles.Delay.DelayProfile)`
9. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.Delete(System.Int32)`
10. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
11. `Sonarr.Api.V3|M:Sonarr.Api.V3.Profiles.Delay.DelayProfileResourceMapper.ToModel(Sonarr.Api.V3.Profiles.Delay.DelayProfileResource)`
12. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`
13. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached.Clear`
14. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.Update(NzbDrone.Core.Profiles.Delay.DelayProfile)`
15. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Update(`0)`
16. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Delete(System.Int32)`
17. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.UpdateMany(System.Collections.Generic.IList{`0})`

<sub>commit 0bffefe · inputs 146adaa · slice of 56 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
