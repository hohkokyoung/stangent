# UI authorization refresh on config save

`Sonarr.Http|M:NzbDrone.Http.Authentication.UiAuthorizationHandler.Handle(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)` · backend · Sonarr.Http

From the code: UiAuthorizationHandler.Handle(ConfigSavedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a config-saved event is received, the handler re-reads the authentication-required setting from the config service and stores it as the current value. [1]

## Rules

- **R1** Config values are read through a cache; on a cache miss the value comes from the config file's element with that key. [2]
- **R2** If the key is not found exactly once in the config file, the default value is returned, and it is also saved to the config file when persist is true (the default). [2]
- **R3** Enum config values are parsed from the stored text, ignoring case. [3]

## Side effects

- **E1** The handler's stored authentication-required field is overwritten with the value read from the config service. [1]
- **E2** If the authentication-required setting is missing from the config file, the default value may be written to the config file. [2]

## Couldn't see

- the depth limit left out 5 more point(s) one line further on

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on E1 (unsupported): The handler only assigns the field. The slice doesn't show where the field is read, so it can't show that later authorization decisions use it. **fixed**: Removed the claim about later authorization decisions; the statement now says only that the field is overwritten.

## Cited symbols

1. `Sonarr.Http|M:NzbDrone.Http.Authentication.UiAuthorizationHandler.Handle(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.GetValue(System.String,System.Object,System.Boolean)`
3. `Sonarr.Core|M:NzbDrone.Core.Configuration.ConfigFileProvider.GetValueEnum``1(System.String,``0,System.Boolean)`

<sub>commit 0bffefe · inputs 159a4c1 · slice of 11 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
