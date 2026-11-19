# List Commands (GET /api/v3/Command)

`endpoint:Sonarr.Api.V3|GET /api/v3/Command` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Command

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a list of commands from the command queue, each as a command resource. [1][2]
- **S2** Each command shows id, name, message, body, priority, status, result, queued, started and ended times, duration, exception, trigger, completion message and last execution time. [3]
- **S3** Each command also has a command name, which is the command name with spaces inserted at camel-case boundaries. [3][4]
- **S4** The client user agent is shown as sent, except it is empty when it is missing or starts with "Mozilla/5.0". [5][3]

## Actions

- **A1** A caller requests the list of commands and receives it as JSON. [1] → `endpoint:Sonarr.Api.V3|GET /api/v3/Command`

## Rules

- **R1** Commands are ordered by status using the command priority comparer, then by priority from highest to lowest. [1]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 13 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Commands.CommandController.GetStartedCommands`
2. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.IManageCommandQueue.All`
3. `Sonarr.Api.V3|M:Sonarr.Api.V3.Commands.CommandResourceMapper.ToResource(NzbDrone.Core.Messaging.Commands.CommandModel)`
4. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.SplitCamelCase(System.String)`
5. `Sonarr.Common|M:NzbDrone.Common.Http.UserAgentParser.SimplifyUserAgent(System.String)`

<sub>commit 0bffefe · inputs a738290 · slice of 46 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
