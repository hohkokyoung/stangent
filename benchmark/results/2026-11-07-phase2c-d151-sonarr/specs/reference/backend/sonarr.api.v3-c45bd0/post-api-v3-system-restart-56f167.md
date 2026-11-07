# Restart application (System API)

`endpoint:Sonarr.Api.V3|POST /api/v3/System/restart` · backend · Sonarr.Api.V3

From the code: POST /api/v3/System/restart

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an object with Restarting set to true immediately, without waiting for the restart to happen. [1]

## Actions

- **A1** A POST to /api/v3/System/restart requests an application restart, which runs in a background task. [2][1] → `endpoint:Sonarr.Api.V3|POST /api/v3/System/restart`

## Rules

- **R1** The restart runs in the background, so the response does not depend on the restart completing. [1]
- **R2** The shutdown event is published with Restarting set to true. [3][4]

## Side effects

- **E1** Logs the message "Restart requested." at info level. [3]
- **E2** Marks the runtime info as restart pending (RestartPending = true). [3]
- **E3** Publishes an ApplicationShutdownRequested event with the restarting flag set. [3][5]

## Couldn't see

- the depth limit left out 4 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on E4 (unsupported): The slice shows only the Handle signatures and a guessed (🟡) trigger link. The handler bodies aren't shown, so what each one does with the event isn't supported. The claim that the event is handled by these four is only a detector guess. **fixed**: Removed the statement naming the four handlers, since the slice shows only their signatures and a guessed link.

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.System.SystemController.Restart`
2. `endpoint:Sonarr.Api.V3|POST /api/v3/System/restart`
3. `Sonarr.Core|M:NzbDrone.Core.Lifecycle.LifecycleService.Restart`
4. `Sonarr.Core|M:NzbDrone.Core.Lifecycle.ApplicationShutdownRequested.#ctor(System.Boolean)`
5. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`

<sub>commit 0bffefe · inputs 58d12ac · slice of 16 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
