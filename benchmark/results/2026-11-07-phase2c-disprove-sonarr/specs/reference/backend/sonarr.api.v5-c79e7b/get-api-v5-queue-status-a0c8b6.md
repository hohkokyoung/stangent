# Queue status (GET /api/v5/queue/status)

`endpoint:Sonarr.Api.V5|GET /api/v5/queue/status` · backend · Sonarr.Api.V5

From the code: GET /api/v5/queue/status

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a queue status object with TotalCount, Count, UnknownCount, Errors, Warnings, UnknownErrors and UnknownWarnings. [1]
- **S2** TotalCount is the number of queue items plus the number of pending releases. [1]
- **S3** Count is the number of queue items that have a series plus the number of pending releases. [1]
- **S4** UnknownCount is the number of queue items with no series. [1]
- **S5** Errors is true when any queue item with a series has tracked download status Error; Warnings is true likewise for status Warning. [1]
- **S6** UnknownErrors and UnknownWarnings are true when any queue item with no series has tracked download status Error or Warning respectively. [1]

## Actions

- **A1** A caller requests the queue status with GET and receives the JSON status summary. [2][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/queue/status`

## Rules

- **R1** Queue items come from the queue service's current queue list, and pending releases come from the pending release service's pending queue. [1][3][4]
- **R2** Pending releases are counted only in TotalCount and Count; they do not affect the error or warning flags. [1]

## Side effects

- **E1** While the status is computed, the broadcast debouncer is paused (its timer stopped) and then resumed afterwards. [1][5]
- **E2** On resume, the debouncer timer restarts only when the pause count reaches zero and a trigger is pending. [6]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Queue.QueueStatusController.GetQueueStatus`
2. `endpoint:Sonarr.Api.V5|GET /api/v5/queue/status`
3. `Sonarr.Core|M:NzbDrone.Core.Queue.QueueService.GetQueue`
4. `Sonarr.Core|M:NzbDrone.Core.Download.Pending.IPendingReleaseService.GetPendingQueue`
5. `Sonarr.Common|M:NzbDrone.Common.TPL.Debouncer.Pause`
6. `Sonarr.Common|M:NzbDrone.Common.TPL.Debouncer.Resume`

<sub>commit 0bffefe · inputs 483855c · slice of 25 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
