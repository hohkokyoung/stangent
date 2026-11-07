# Health check complete broadcast

`Sonarr.Api.V3|M:Sonarr.Api.V3.Health.HealthController.Handle(NzbDrone.Core.HealthCheck.HealthCheckCompleteEvent)` · backend · Sonarr.Api.V3

From the code: HealthController.Handle(HealthCheckCompleteEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a health check completes event is received, the controller broadcasts a resource change with action Sync. [1]

## Rules

- **R1** Nothing is broadcast if the SignalR broadcaster is not connected. [2]
- **R2** The message is only sent when the controller's namespace contains "V3" or "V5". [2]

## Side effects

- **E1** A SignalR message is broadcast carrying the resource name, a resource change body, the action and the API version. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Health.HealthController.Handle(NzbDrone.Core.HealthCheck.HealthCheckCompleteEvent)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestControllerWithSignalR`2.BroadcastResourceChange(NzbDrone.Core.Datastore.Events.ModelAction)`

<sub>commit 0bffefe · inputs db64a17 · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
