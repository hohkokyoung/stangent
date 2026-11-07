# Tag list refresh on auto-tag update

`Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.Handle(NzbDrone.Core.AutoTagging.AutoTagsUpdatedEvent)` · backend · Sonarr.Api.V3

From the code: TagController.Handle(AutoTagsUpdatedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When an auto-tags-updated event is received, the tag controller broadcasts a Sync resource change for tags. [1]

## Rules

- **R1** Nothing is broadcast if the real-time (SignalR) broadcaster is not connected. [2]
- **R2** The message is only sent when the controller's namespace contains "V3" or "V5". [2]
- **R3** The handler is not exposed as an HTTP action. [1]

## Side effects

- **E1** A real-time message is sent to connected clients with the resource name, the action Sync, a resource change body and the controller version. [2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Tags.TagController.Handle(NzbDrone.Core.AutoTagging.AutoTagsUpdatedEvent)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestControllerWithSignalR`2.BroadcastResourceChange(NzbDrone.Core.Datastore.Events.ModelAction)`

<sub>commit 0bffefe · inputs b1df34d · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
