# Health checks run at application start

`Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheckService.HandleAsync(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)` · backend · Sonarr.Core

From the code: HealthCheckService.HandleAsync(ApplicationStartedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Each health check returns a result with a source, a type (Ok, Notice, Warning or Error), a reason, a message and a wiki link. A check with no problem returns an Ok result. [1][2]

## Actions

- **A1** When the application starts, all startup health checks are queued and run immediately. [3][4]

## Rules

- **R1** Pending checks are taken from the queue and the queue is cleared before they run. The debouncer is paused while they run and resumed afterwards, even if a check fails. [4][5][6]
- **R2** A failing result (any type other than Ok) is stored by the check's source name. A failed event is published only if no result was already stored for that source. [4][7][8]
- **R3** An Ok result removes the stored result for that source. A restored event is published only if a previous result existed. [4][9]
- **R4** Failed and restored events carry a flag that says whether health checks have not yet run after the startup grace period. [4][10][11]
- **R5** The API key check warns when the API key is shorter than the minimum length. [12]
- **R6** The system time check reports an error when the system time differs from the service time by one day or more. [13]
- **R7** The indexer status check only counts indexers whose initial failure was within the last 6 hours. The long-term check only counts those whose initial failure was more than 6 hours ago. [14][15]
- **R8** A package global message starting with "Error:" becomes an Error and one starting with "Warn:" becomes a Warning. Any other message becomes a Notice. [16]

## Side effects

- **E1** A failed event is published for each newly failing check, and a restored event for each check that recovers. [4][17]
- **E2** After all checks run, a HealthCheckCompleteEvent is published. [4][17]
- **E3** The notification service handles failed, restored and complete health check events. [18][19][20]
- **E4** The V3 and V5 health controllers handle the health check complete event. [21][22]
- **E5** Some checks call external services. The system time check and the proxy check call the cloud service, and the server notification check fetches notifications from it. [13][23][24]

## Couldn't see

- the depth limit left out 192 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheck.#ctor(System.Type)`
2. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheck.#ctor(System.Type,NzbDrone.Core.HealthCheck.HealthCheckResult,NzbDrone.Core.HealthCheck.HealthCheckReason,System.String,System.String)`
3. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheckService.HandleAsync(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)`
4. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheckService.ProcessHealthChecks`
5. `Sonarr.Common|M:NzbDrone.Common.TPL.Debouncer.Pause`
6. `Sonarr.Common|M:NzbDrone.Common.TPL.Debouncer.Resume`
7. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached`1.Set(System.String,`0,System.Nullable{System.TimeSpan})`
8. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached`1.Find(System.String)`
9. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached.Remove(System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheckFailedEvent.#ctor(NzbDrone.Core.HealthCheck.HealthCheck,System.Boolean)`
11. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.HealthCheckRestoredEvent.#ctor(NzbDrone.Core.HealthCheck.HealthCheck,System.Boolean)`
12. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.Checks.ApiKeyValidationCheck.Check`
13. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.Checks.SystemTimeCheck.Check`
14. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.Checks.IndexerStatusCheck.Check`
15. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.Checks.IndexerLongTermStatusCheck.Check`
16. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.Checks.PackageGlobalMessageCheck.Check`
17. `Sonarr.Core|M:NzbDrone.Core.Messaging.Events.IEventAggregator.PublishEvent``1(``0)`
18. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.HealthCheck.HealthCheckFailedEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-healthcheckfailedevent-c02089.md)
19. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.Handle(NzbDrone.Core.HealthCheck.HealthCheckRestoredEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handle-healthcheckrestoredevent-5280e9.md)
20. [`Sonarr.Core|M:NzbDrone.Core.Notifications.NotificationService.HandleAsync(NzbDrone.Core.HealthCheck.HealthCheckCompleteEvent)`](../../backend/sonarr.core-1b4c77/notificationservice.handleasync-healthcheckcompleteevent-a4f33f.md)
21. [`Sonarr.Api.V3|M:Sonarr.Api.V3.Health.HealthController.Handle(NzbDrone.Core.HealthCheck.HealthCheckCompleteEvent)`](../../backend/sonarr.api.v3-c45bd0/healthcontroller.handle-healthcheckcompleteevent-0f0a3d.md)
22. [`Sonarr.Api.V5|M:Sonarr.Api.V5.Health.HealthController.Handle(NzbDrone.Core.HealthCheck.HealthCheckCompleteEvent)`](../../backend/sonarr.api.v5-c79e7b/healthcontroller.handle-healthcheckcompleteevent-06645a.md)
23. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.Checks.ProxyCheck.Check`
24. `Sonarr.Core|M:NzbDrone.Core.HealthCheck.ServerSideNotificationService.Check`

<sub>commit 0bffefe · inputs c098abc · slice of 101 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
