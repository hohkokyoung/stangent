# Command updated event handler (client updates)

`Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandController.Handle(NzbDrone.Core.ProgressMessaging.CommandUpdatedEvent)` · backend · Sonarr.Api.V5

From the code: CommandController.Handle(CommandUpdatedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The command resource built for a client update carries id, name, a spaced-out command name, message, body, priority, status, result, queued/started/ended times, duration, exception and trigger. [1]
- **S2** The resource also carries the body's completion message and last execution time. [1]
- **S3** The command name is shown with spaces inserted before each camel-case match (SplitCamelCase of the name). [2][1]
- **S4** The client user agent is shown as-is, except it is empty when it is null or starts with "Mozilla/5.0". [3][1]

## Actions

- **A1** On a CommandUpdatedEvent, the handler records the command's resource as a pending update keyed by command id, then triggers the debouncer. [4]

## Rules

- **R1** Only commands whose body has SendUpdatesToClient set are handled; others are ignored. [4]
- **R2** A newer update for the same command id replaces the pending one. [4]
- **R3** The debouncer starts its timer only when it is not paused; the trigger is remembered either way. [5]

## Side effects

- **E1** Pending command updates are stored in a lock-protected collection and the debouncer is triggered, which starts its timer when not paused. [4][5]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandResourceMapper.ToResource(NzbDrone.Core.Messaging.Commands.CommandModel)`
2. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.SplitCamelCase(System.String)`
3. `Sonarr.Common|M:NzbDrone.Common.Http.UserAgentParser.SimplifyUserAgent(System.String)`
4. `Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandController.Handle(NzbDrone.Core.ProgressMessaging.CommandUpdatedEvent)`
5. `Sonarr.Common|M:NzbDrone.Common.TPL.Debouncer.Execute`

<sub>commit 0bffefe · inputs 3746e1a · slice of 85 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
