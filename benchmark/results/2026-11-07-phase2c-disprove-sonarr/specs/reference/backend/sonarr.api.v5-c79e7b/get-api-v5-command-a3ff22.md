# List Commands (GET /api/v5/Command)

`endpoint:Sonarr.Api.V5|GET /api/v5/Command` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Command

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON list of all commands currently held by the command queue manager. [1][2]
- **S2** Each command shows Id, Name, Message, Body, Priority, Status, Result, Exception and Trigger, with queued, started and ended times (Queued, Started, Ended) and Duration. [3]
- **S3** Each command also has a CommandName, which is the Name with spaces inserted at camel-case word boundaries. [3][4]
- **S4** CompletionMessage and LastExecutionTime are taken from the command body. [3]
- **S5** ClientUserAgent is the client user agent from the command body, or null when it is null or starts with "Mozilla/5.0". [3][5]

## Actions

- **A1** A caller requests the list of commands with GET and receives them all with no filter. [6][1] → `endpoint:Sonarr.Api.V5|GET /api/v5/Command`

## Rules

- **R1** Commands are ordered by Status using the command priority comparer, then by Priority from highest to lowest. [1]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 13 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandController.GetStartedCommands`
2. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.IManageCommandQueue.All`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandResourceMapper.ToResource(NzbDrone.Core.Messaging.Commands.CommandModel)`
4. `Sonarr.Common|M:NzbDrone.Common.Extensions.StringExtensions.SplitCamelCase(System.String)`
5. `Sonarr.Common|M:NzbDrone.Common.Http.UserAgentParser.SimplifyUserAgent(System.String)`
6. `endpoint:Sonarr.Api.V5|GET /api/v5/Command`

<sub>commit 0bffefe · inputs 30b3027 · slice of 46 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
