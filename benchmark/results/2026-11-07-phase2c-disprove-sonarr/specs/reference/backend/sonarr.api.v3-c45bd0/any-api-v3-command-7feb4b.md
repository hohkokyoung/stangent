# Command API (start and cancel commands)

`endpoint:Sonarr.Api.V3|ANY /api/v3/Command` · backend · Sonarr.Api.V3

From the code: ANY /api/v3/Command

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Starting a command returns a created-at-action result pointing to the new command's resource, looked up by the tracked command's id. [1][2]
- **S2** Getting a command by id returns it as JSON, or Not Found when the model is not found. [3]
- **S3** Cancelling a command returns no body. [4]

## Actions

- **A1** Start a command with a JSON body: the command type is matched by the resource name, the body is deserialized into that command, and it is pushed to the command queue as a manual trigger. [2][5][6] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Command`
- **A2** Cancel a command by id, which removes it from the queue if it is still queued. [4][7] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Command`
- **A3** Get a command by id. [3] → `endpoint:Sonarr.Api.V3|ANY /api/v3/Command`

## Rules

- **R1** The command type is the single known Command implementation whose class name, with "Command" removed, equals the requested name, ignoring case. Zero or multiple matches cause an error. [2][8]
- **R2** ManualImportCommand is queued with High priority. All other commands are queued with Normal priority. [2]
- **R3** SuppressMessages is set to the opposite of the submitted SendUpdatesToClient, and SendUpdatesToClient is then forced to true. [2]
- **R4** The command's ClientUserAgent is taken from the request's UserAgent header. [2]
- **R5** A command can be cancelled only when it is in the queue with status Queued. If it is not removed (including when no such command is found), a 409 Conflict error "Unable to cancel task" is raised. [7][9][10]
- **R6** For POST and PUT requests, resources are validated before the action runs. The id argument is validated when the action is marked for id validation and validation is not skipped. [11]
- **R7** Calls to endpoints marked deprecated are logged as a warning and get a "Deprecation: true" response header. [11]

## Side effects

- **E1** Starting a command adds it to the command queue as a manual-trigger command. [2][5]
- **E2** Cancelling a queued command removes it from the queue and wakes up waiting threads on the queue. [9]

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on S1 (unsupported): Source shows CreatedAtAction but does not show the 201 status code explicitly; mild. The lookup by id is supported. **fixed**: Removed the explicit 201 status claim; the source only shows CreatedAtAction.
- F2 on R5 (unsupported): Mostly supported, but 'Otherwise' covers non-existent commands too; supported by code (RemoveIfQueued returns false). No real issue. **fixed**: Clarified that the 409 applies whenever the command is not removed, including when no such command is found.

## Cited symbols

1. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.Created(System.Int32)`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Commands.CommandController.StartCommand(Sonarr.Api.V3.Commands.CommandResource)`
3. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.GetResourceByIdWithErrorHandler(System.Int32)`
4. `Sonarr.Api.V3|M:Sonarr.Api.V3.Commands.CommandController.CancelCommand(System.Int32)`
5. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.IManageCommandQueue.Push``1(``0,NzbDrone.Core.Messaging.Commands.CommandPriority,NzbDrone.Core.Messaging.Commands.CommandTrigger)`
6. `Sonarr.Common|M:NzbDrone.Common.Serializer.STJson.Deserialize(System.String,System.Type)`
7. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandQueueManager.Cancel(System.Int32)`
8. `Sonarr.Common|M:NzbDrone.Common.Composition.KnownTypes.GetImplementations(System.Type)`
9. `Sonarr.Core|M:NzbDrone.Core.Messaging.Commands.CommandQueue.RemoveIfQueued(System.Int32)`
10. `Sonarr.Core|M:NzbDrone.Core.Exceptions.NzbDroneClientException.#ctor(System.Net.HttpStatusCode,System.String)`
11. `Sonarr.Http|M:Sonarr.Http.REST.RestController`1.OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext)`

<sub>commit 0bffefe · inputs 1f23e7f · slice of 65 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
