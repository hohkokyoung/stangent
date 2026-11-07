# Get Localization Strings

`endpoint:Sonarr.Api.V3|GET /api/v3/Localization` · backend · Sonarr.Api.V3

From the code: GET /api/v3/Localization

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Returns a localization object whose Strings field is a dictionary of translation keys to translated text. [1][2]
- **S2** The response is JSON. [2]

## Actions

- **A1** A caller requests GET /api/v3/Localization to receive the localization dictionary for the current language. [3][2] → `endpoint:Sonarr.Api.V3|GET /api/v3/Localization`

## Rules

- **R1** The language file name is derived from the language identifier by replacing "-" with "_" and lowercasing. [4]
- **R2** An empty or missing language name causes an ArgumentNullException. [5]
- **R3** Dictionary files are read from the Localization/Core folder under the application startup folder, using the default culture's .json file as a further argument. [5]
- **R4** In debug builds the dictionary is loaded fresh on every request; otherwise it is cached under the key "localization". [5]
- **R5** If the dictionary is null, the resource returned is null. [1]

## Side effects

Nothing found in the code.

## Couldn't see

- the depth limit left out 9 more point(s) one line further on

## Cited symbols

1. `Sonarr.Api.V3|M:Sonarr.Api.V3.Localization.LocalizationResourceMapper.ToResource(System.Collections.Generic.Dictionary{System.String,System.String})`
2. `Sonarr.Api.V3|M:Sonarr.Api.V3.Localization.LocalizationController.GetLocalization`
3. `endpoint:Sonarr.Api.V3|GET /api/v3/Localization`
4. `Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.GetLanguageFileName`
5. `Sonarr.Core|M:NzbDrone.Core.Localization.LocalizationService.GetLocalizationDictionary(System.String)`

<sub>commit 0bffefe · inputs 675d420 · slice of 11 points, 4 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
