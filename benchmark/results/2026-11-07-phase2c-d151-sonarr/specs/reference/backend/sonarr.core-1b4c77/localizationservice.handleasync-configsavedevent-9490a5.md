# Localization cache reset on config save

`Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)` · backend · Sonarr.Core

From the code: LocalizationService.HandleAsync(ConfigSavedEvent)

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

Nothing found in the code.

## Actions

- **A1** When a configuration-saved event is received, the localization service handles it by clearing its cache. [1]

## Rules

Nothing found in the code.

## Side effects

- **E1** All entries in the localization service's cache are removed whenever the configuration is saved. [1][2]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.HandleAsync(NzbDrone.Core.Configuration.Events.ConfigSavedEvent)`
2. `Sonarr.Common|M:NzbDrone.Common.Cache.ICached.Clear`

<sub>commit 0bffefe · inputs f9a990a · slice of 3 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
