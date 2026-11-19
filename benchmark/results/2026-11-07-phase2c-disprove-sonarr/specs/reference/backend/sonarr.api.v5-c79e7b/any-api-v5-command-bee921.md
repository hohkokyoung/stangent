# Command API (start and cancel commands)

`endpoint:Sonarr.Api.V5|ANY /api/v5/Command` · backend · Sonarr.Api.V5

From the code: ANY /api/v5/Command

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Starting a command returns a 201 Created response with the new tracked command as a CommandResource, located at the get-by-id URL; NotFound is the alternative result. [1][2]
- **S2** Cancelling a command returns 204 No Content. [3]
- **S3** Getting a command by id returns it as JSON, or NotFound when no such resource exists. [4]

## Actions

- **A1** Start a command: POST a JSON body whose name selects the command type. The command is pushed to the queue with the requested priority and a Manual trigger. [1][5]
- **A2** Cancel a command by id (DELETE); it is removed from the queue if it is still queued. [3][6]
- **A3** Fetch a command by id (GET). [4]

## Rules

- **R1** The command type is the known Command implementation whose class name, with "Command" removed, equals the request name, ignoring case. Exactly one must match. [1][7]
- **R2** Only concrete (non-interface, non-abstract) types assignable to Command are candidates. [7]
- **R3** If the body cannot be deserialized into the command type, the request fails with 400 Bad Request "Invalid command body". [1][8]
- **R4** The command's SuppressMessages is set to the opposite of the submitted SendUpdatesToClient, and SendUpdatesToClient is then forced to true. [1]
- **R5** Only commands with status Queued can be cancelled; otherwise the request fails with 409 Conflict "Unable to cancel task". [6][9][10]
- **R6** For POST and PUT requests, resources in the arguments are validated before the action runs; for PUT, a zero body Id is filled from the route id. [11]
- **R7** Calls to endpoints marked deprecated are logged as a warning and the response gets a Deprecation: true header. [11]
- **R8** The start endpoint accepts and returns application/json. [1]

## Side effects

- **E1** Starting a command adds it to the command queue with the requested priority and a Manual trigger, and records the client user agent on it. [1][5]
- **E2** Cancelling a queued command removes it from the queue and wakes any waiting queue consumers. [9]

## Couldn't see

- the depth limit left out 5 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandController.StartCommand(Sonarr.Api.V5.Commands.CommandResource)`
2. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.TypedCreated(System.Int32)`
3. `Sonarr.Api.V5|M:Sonarr.Api.V5.Commands.CommandController.CancelCommand(System.Int32)`
4. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
5. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.IManageCommandQueue.Push``1(``0,NzbDrone.Core.Messaging.Commands.CommandPriority,NzbDrone.Core.Messaging.Commands.CommandTrigger)`
6. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandQueueManager.Cancel(System.Int32)`
7. `Sonarr.Common|M:NzbDrone.Common.Composition.KnownTypes.GetImplementations(System.Type)`
8. `Sonarr.Http|M:Sonarr.Http.REST.BadRequestException.#ctor(System.Object)`
9. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandQueue.RemoveIfQueued(System.Int32)`
10. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
11. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs 30b053d · slice of 62 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
