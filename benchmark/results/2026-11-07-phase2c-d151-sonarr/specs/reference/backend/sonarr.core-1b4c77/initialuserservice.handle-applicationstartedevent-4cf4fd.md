# Initial user creation on application start

`Sonarr.Core|M:NzbDrone.Core.Authentication.InitialUserService.Handle(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)` · backend · Sonarr.Core

From the code: InitialUserService.Handle(ApplicationStartedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When the application has started, it tries to create an initial user so that sign-in is possible. [1][2]

## Rules

- **R1** If a user already exists, nothing is created. [2][3]
- **R2** If the config file exists and has a non-blank Username and Password, a user is created from them and no further steps run. [4][2]
- **R3** If the config file is missing, or its username or password is blank, no user is migrated from it. [4]
- **R4** A user is created from the configured initial credentials only when the authentication method is Forms. [2]
- **R5** The username is the configured initial username, or a default username when that is blank. [2][5]
- **R6** The password is the configured initial password. When that is blank, a random password is generated. [2][6][7]
- **R7** The stored username is converted to lower case. The user gets a new unique identifier. [8]
- **R8** The password is stored as a salted hash with an iteration count, not as plain text. [9]

## Side effects

- **E1** A new user record is inserted into the datastore. [8][10]
- **E2** When the password was generated, two warnings are logged to the auth log. One gives the created username and the generated password. The other tells the user to change it from Settings: General. [2]
- **E3** When the configured password is used, an info message is logged saying the user was created with the configured password. [2]
- **E4** If creating the user fails, the error is logged and startup continues. The message says sign-in will not be possible until a user is created. [1]

## Couldn't see

- the depth limit left out 12 more point(s) one line further on

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Authentication.InitialUserService.Handle(NzbDrone.Core.Lifecycle.ApplicationStartedEvent)`
2. `Sonarr.Core|M:NzbDrone.Core.Authentication.InitialUserService.CreateInitialUser`
3. `Sonarr.Core|M:NzbDrone.Core.Authentication.UserService.FindUser`
4. `Sonarr.Core|M:NzbDrone.Core.Authentication.InitialUserService.MigrateFromConfigFile`
5. `config:Sonarr.Host|Sonarr:Auth:InitialUsername`
6. `Sonarr.Common|M:NzbDrone.Common.SecretGenerator.Generate(System.Int32)`
7. `config:Sonarr.Host|Sonarr:Auth:InitialPassword`
8. `Sonarr.Core|M:NzbDrone.Core.Authentication.UserService.Add(System.String,System.String)`
9. `Sonarr.Core|M:NzbDrone.Core.Authentication.UserService.SetUserHashedPassword(NzbDrone.Core.Authentication.User,System.String)`
10. `Sonarr.Core|M:NzbDrone.Core.Datastore.IBasicRepository`1.Insert(`0)`

<sub>commit 0bffefe · inputs a6384bf · slice of 42 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
