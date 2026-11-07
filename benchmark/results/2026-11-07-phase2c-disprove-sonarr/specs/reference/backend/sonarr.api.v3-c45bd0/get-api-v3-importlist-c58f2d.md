# List import lists (GET /api/v3/ImportList)

`endpoint:Sonarr.Api.V3|GET /api/v3/ImportList` · backend · Sonarr.Api.V3

From the code: GET /api/v3/ImportList

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of all configured import list definitions as resources. [1]
- **S2** Each item shows id, name, implementation name, implementation, config contract, message, tags and an info link built from the lowercased implementation name. [2]
- **S3** Each item includes a list of settings fields built from the definition's settings, each with its current value. [2][3]
- **S4** Each import list also shows: automatic add, search for missing episodes, should monitor, monitor new items, root folder path, quality profile id, series type, language, season folder, list type, list order, minimum refresh interval and tag existing. [4]
- **S5** The list order of an item is the numeric value of its list type. [4]

## Actions

- **A1** A caller requests all import lists with no parameters and receives the full list. [1] → `endpoint:Sonarr.Api.V3|GET /api/v3/ImportList`

## Rules

- **R1** Definitions are processed in order of implementation name, and the final result is sorted by name. [1]
- **R2** Field values with API key or password privacy are replaced by a placeholder value when they are not null or empty. [3]
- **R3** Fields are returned ordered by their order, renumbered sequentially from 0 because nested settings can have duplicate orders. [3][5]
- **R4** A null settings model is rejected with a not-null validation error. [3][6]

## Side effects

- **E1** For each definition, provider characteristics are set by creating the provider instance and assigning its definition. [7][8]

## Couldn't see

- the depth limit left out 27 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderControllerBase`4.GetAll`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.ProviderResourceMapper`2.ToResource(`1)`
3. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.ToSchema(System.Object)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.ImportLists.ImportListResourceMapper.ToResource(NzbDrone.Core.ImportLists.ImportListDefinition)`
5. `Sonarr.Http|M:Sonarr.Http.ClientSchema.SchemaBuilder.GetFieldMappings(System.Type)`
6. `Sonarr.Common|M:NzbDrone.Common.EnsureThat.EnsureObjectExtensions.IsNotNull``1(NzbDrone.Common.EnsureThat.Param{``0})`
7. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.SetProviderCharacteristics(`1)`
8. `Sonarr.Core|M:NzbDrone.Core.ThingiProvider.ProviderFactory`2.GetInstance(`1)`

<sub>commit 0bffefe · inputs 4702a2f · slice of 168 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
