# System restart (POST /api/v5/System/restart)

`endpoint:Sonarr.Api.V5|POST /api/v5/System/restart` · backend · Sonarr.Api.V5

From the code: POST /api/v5/System/restart

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns an OK response with a Restarting flag set to true. [1]

## Actions

- **A1** A caller can request an application restart. The restart runs in a background task, so the response is returned without waiting for it. [1][2] → `endpoint:Sonarr.Api.V5|POST /api/v5/System/restart`

## Rules

- **R1** The restart event is created with the restarting flag set to true. [2][3]

## Side effects

- **E1** The restart logs "Restart requested." and marks the runtime info as restart pending. [2]
- **E2** The restart publishes an ApplicationShutdownRequested event with restarting set to true. [2][4]
- **E3** The shutdown event is handled by the database log target, the job scheduler, the command executor and the host application lifetime. [5][6][7][8]

## Couldn't see

- the depth limit left out 4 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.System.SystemController.Restart`
2. `Sonarr.Core|M:NzbDrone.Core.Lifecycle.LifecycleService.Restart`
3. `Sonarr.Core|M:NzbDrone.Core.Lifecycle.ApplicationShutdownRequested.#ctor(System.Boolean)`
4. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
5. [`Sonarr.Core|M:NzbDrone.Core.Instrumentation.DatabaseTarget.Handle(NzbDrone.Core.Lifecycle.ApplicationShutdownRequested)`](../../backend/sonarr.core-1b4c77/databasetarget.handle-applicationshutdownrequested-48f451.md)
6. [`Sonarr.Core|M:NzbDrone.Core.Jobs.Scheduler.Handle(NzbDrone.Core.Lifecycle.ApplicationShutdownRequested)`](../../backend/sonarr.core-1b4c77/scheduler.handle-applicationshutdownrequested-a7d504.md)
7. [`Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandExecutor.Handle(NzbDrone.Core.Lifecycle.ApplicationShutdownRequested)`](../../backend/sonarr.core-1b4c77/commandexecutor.handle-applicationshutdownrequested-f214e1.md)
8. [`Sonarr.Host|M:NzbDrone.Host.AppLifetime.Handle(NzbDrone.Core.Lifecycle.ApplicationShutdownRequested)`](../../backend/sonarr.host-0475e6/applifetime.handle-applicationshutdownrequested-ca2be5.md)

<sub>commit 0bffefe · inputs cc41124 · slice of 18 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
