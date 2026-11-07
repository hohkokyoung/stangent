# Delay Profile API

`endpoint:Sonarr.Api.V5|ANY /api/v5/DelayProfile` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/DelayProfile

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Fetching a delay profile by id returns the profile, or a not-found result when no such profile exists. [1]
- **S2** A delay profile carries: enable Usenet, enable torrent, preferred protocol, Usenet delay, torrent delay, bypass if highest quality, bypass if above custom format score, minimum custom format score, order, and tags. [2]
- **S3** Create returns the created profile, and update returns the accepted profile, each with a link to fetch it by id. [3][4]

## Actions

- **A1** Create a delay profile from a JSON body; the profile is saved and returned as created. [5] → `endpoint:Sonarr.Api.V5|ANY /api/v5/DelayProfile`
- **A2** Update a delay profile from a JSON body; the profile is saved and returned as accepted. [6] → `endpoint:Sonarr.Api.V5|ANY /api/v5/DelayProfile`
- **A3** Delete a delay profile by id; returns no content. [7] → `endpoint:Sonarr.Api.V5|ANY /api/v5/DelayProfile`

## Rules

- **R1** The profile with id 1 cannot be deleted; the call fails with Method Not Allowed and the message "Cannot delete global delay profile". [7][8]
- **R2** A new delay profile's order is set to the current number of delay profiles. [9][10]
- **R3** After a delete, the remaining profiles are renumbered by their existing order as 1, 2, 3 and so on, skipping the profile with id 1, which keeps its order. [11]
- **R4** On PUT, if the body id is 0 and the route has an id, the route id is used. POST and PUT bodies are validated. [12]
- **R5** Calls to endpoints marked deprecated are logged as a warning and the response gets a "Deprecation: true" header. [12]
- **R6** A profile with no tags given starts with an empty tag set; the mapper always builds the tag set from the resource's tags. [13][2]

## Side effects

- **E1** Creating a profile inserts it in the data store and clears the best-for-tags cache. [9][14][15]
- **E2** Updating a profile saves it in the data store and clears the best-for-tags cache. [16][17][15]
- **E3** Deleting a profile removes it from the data store, saves the renumbered order of the remaining profiles, and clears the best-for-tags cache. [11][18][19][15]

## Couldn't see

- the depth limit left out 8 more point(s) one line further on

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Delay.DelayProfileResourceMapper.ToModel(Sonarr.Api.V5.Profiles.Delay.DelayProfileResource)`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedCreated(System.Int32)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedAccepted(System.Int32)`
5. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Delay.DelayProfileController.Create(Sonarr.Api.V5.Profiles.Delay.DelayProfileResource)`
6. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Delay.DelayProfileController.Update(Sonarr.Api.V5.Profiles.Delay.DelayProfileResource)`
7. `Sonarr.Api.V5|M:Sonarr.Api.V5.Profiles.Delay.DelayProfileController.DeleteProfile(System.Int32)`
8. `Sonarr.Http|M:Sonarr.Http.REST.MethodNotAllowedException.#ctor(System.Object)`
9. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.Add(NzbDrone.Core.Profiles.Delay.DelayProfile)`
10. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Count`
11. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.Delete(System.Int32)`
12. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`
13. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfile.#ctor`
14. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`
15. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached.Clear`
16. `Sonarr.Core|M:NzbDrone.Core.Profiles.Delay.DelayProfileService.Update(NzbDrone.Core.Profiles.Delay.DelayProfile)`
17. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Update(`0)`
18. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Delete(System.Int32)`
19. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.UpdateMany(System.Collections.Generic.IList{`0})`

<sub>commit 0bffefe · inputs db78668 · slice of 56 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
