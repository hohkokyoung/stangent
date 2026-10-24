# Provider added event handler (live update broadcast)

`Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Handle(NzbDrone.Core.ThingiProvider.Events.ProviderAddedEvent{`2})` · backend · Sonarr.Api.V5

From the code: ProviderControllerBase.Handle(ProviderAddedEvent{`2})

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a provider is added, the handler broadcasts a Created change for the new provider's definition id. [1]

## Rules

- **R1** Nothing is broadcast when the real-time (SignalR) broadcaster is not connected. [2]
- **R2** For any action other than Deleted, the resource is loaded by id and that resource is broadcast with the action. [2]
- **R3** For a Deleted action, a new resource carrying only the id is broadcast instead of loading it. [2]

## Side effects

- **E1** Connected clients receive a real-time Created notification for the newly added provider resource. [1][2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Provider.ProviderControllerBase`4.Handle(NzbDrone.Core.ThingiProvider.Events.ProviderAddedEvent{`2})`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestControllerWithSignalR`2.BroadcastResourceChange(NzbDrone.Core.Datastore.Events.ModelAction,System.Int32)`

<sub>commit 0bffefe · inputs 6c9e82e · slice of 5 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
