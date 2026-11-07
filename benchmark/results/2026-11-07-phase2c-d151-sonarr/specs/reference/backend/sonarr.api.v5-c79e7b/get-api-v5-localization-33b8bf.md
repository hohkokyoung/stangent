# Get Localization strings

`endpoint:Sonarr.Api.V5|GET /api/v5/Localization` · backend · Sonarr.Api.V5

From the code: GET /api/v5/Localization

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a JSON localization object whose Strings field holds a dictionary of string keys to translated text. [1][2]

## Actions

- **A1** A caller sends GET /api/v5/Localization and receives an Ok response with the localization dictionary. [3][2] → `endpoint:Sonarr.Api.V5|GET /api/v5/Localization`

## Rules

- **R1** The dictionary comes from the localization service for the language file name it determines; the request takes no parameters. [4][2]
- **R2** The response content type is application/json. [2]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 3 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V5|M:Sonarr.Api.V5.Localization.LocalizationResourceMapper.ToResource(System.Collections.Generic.Dictionary{System.String,System.String})`
2. `Sonarr.Api.V5|M:Sonarr.Api.V5.Localization.LocalizationController.GetLocalization`
3. `endpoint:Sonarr.Api.V5|GET /api/v5/Localization`
4. `Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.GetLocalizationDictionary`

<sub>commit 0bffefe · inputs 93bfcd3 · slice of 9 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
