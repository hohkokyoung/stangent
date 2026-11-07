# Test all import lists

`endpoint:Sonarr.Api.V3|POST /api/v3/ImportList/testall` · backend · Sonarr.Api.V3

From the code: POST /api/v3/ImportList/testall

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list with one result per tested import list, each holding the list's Id and its validation failures. [1][2]
- **S2** Responds with HTTP 400 and the result list if any tested item has failures; otherwise responds with 200 and the result list. [1]

## Actions

- **A1** Tests every enabled import list with valid settings in one call and returns the combined results. [1][3][4] → `endpoint:Sonarr.Api.V3|POST /api/v3/ImportList/testall`

## Rules

- **R1** Only definitions that are enabled and whose settings validate are tested. [1][5]
- **R2** Each result's failures combine the settings validation errors with the errors from the provider's connection test. [1][4]
- **R3** The provider test runs on an instance built from the definition by the factory. [6][7][8]

## Side effects

- **E1** For a saved import list (Id not 0), a passing test records a success in the import list status service. [9][10]
- **E2** For a saved import list (Id not 0), a failing test records a failure in the import list status service. [9][11]

## Couldn't see

- the depth limit left out 11 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.TestAll`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderTestAllResult.#ctor`
3. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProviderFactory`2.All`
4. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProviderFactory`2.Test(`1)`
5. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProviderConfig.Validate`
6. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.Test(`1)`
7. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.GetInstance(`1)`
8. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.IProvider.Test`
9. `Sonarr.Core|M:NzbDrone.Core.ImportLists.ImportListFactory.Test(NzbDrone.Core.ImportLists.ImportListDefinition)`
10. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.IProviderStatusServiceBase`1.RecordSuccess(System.Int32)`
11. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.Status.IProviderStatusServiceBase`1.RecordFailure(System.Int32,System.TimeSpan)`

<sub>commit 0bffefe · inputs f621ccc · slice of 52 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
