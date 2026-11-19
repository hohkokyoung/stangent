# Scene mapping refresh on series added

`Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.SceneMappingService.Handle(NzbDrone.Core.Tv.Events.SeriesAddedEvent)` · backend · Sonarr.Core

From the code: SceneMappingService.Handle(SeriesAddedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a series is added, the scene mappings are refreshed from all providers, but only if they haven't yet been updated since startup. [1][2]

## Rules

- **R1** The update runs only when no update has happened since startup; the flag is set as soon as an update begins. [1][2]
- **R2** Mappings with a blank title or blank search term are skipped with a warning. [2][3]
- **R3** Mappings with a blank MappingId are skipped with a warning. [2]
- **R4** An empty list from a provider is not applied; a warning is logged and the existing mappings are kept. [2]
- **R5** Each mapping gets a cleaned title as its parse term and the provider's class name as its type. [2][4]
- **R6** Fetched mappings are matched to stored mappings of the same type by MappingId. Matches are updated, new ones inserted, and stored ones not fetched are deleted. [2][5]
- **R7** A failure in one provider is logged as an error and does not stop the other providers. [2]

## Side effects

- **E1** Stored scene mappings are deleted, updated and inserted per provider. [6][7][8]
- **E2** Mappings are fetched from the providers: the scene mapping service proxy and the Xem service. [9][10]
- **E3** After the update, the in-memory caches (by parse term and by TVDB id) are rebuilt from all stored mappings. [11][12]
- **E4** A SceneMappingsUpdatedEvent is published after the update. [2][13]

## Couldn't see

- the depth limit left out 7 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.SceneMappingService.Handle(NzbDrone.Core.Tv.Events.SeriesAddedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.SceneMappingService.UpdateMappings`
3. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.IsNullOrWhiteSpace(System.String)`
4. `Sonarr.Core|M:NzbDrone.Core.Parser.Parser.CleanSeriesTitle(System.String)`
5. `Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.SceneMappingRepository.GetAllByType(System.String)`
6. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.DeleteMany(System.Collections.Generic.List{`0})`
7. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.UpdateMany(System.Collections.Generic.IList{`0})`
8. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.InsertMany(System.Collections.Generic.IList{`0})`
9. `Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.ServicesProvider.GetSceneMappings`
10. `Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Xem.XemService.GetSceneMappings`
11. `Sonarr.Core|M:NzbDrone.Core.DataAugmentation.Scene.SceneMappingService.RefreshCache`
12. `Sonarr.Common|M:NzbDrone.Common.Cache.CachedDictionary`1.Update(System.Collections.Generic.IDictionary{System.String,`0})`
13. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`

<sub>commit 0bffefe · inputs 98b961f · slice of 40 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
