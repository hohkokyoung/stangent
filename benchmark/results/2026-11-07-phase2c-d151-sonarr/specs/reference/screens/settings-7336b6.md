# Settings

`page:/settings` · screen · tsconfig.json

From the code: /settings · Settings

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** The page is titled "Settings" and shows a heading "Settings" above a grid of cards, one per settings section. [1]
- **S2** Each card shows an icon, a section title and a short summary text. [1]
- **S3** The cards are, in order: MediaManagement, Profiles, Quality, CustomFormats, Indexers, DownloadClients, ImportLists, Connect, Metadata, MetadataSource, Tags, General, Ui. [2]
- **S4** Titles and summaries are translated text; an untranslated key is shown as the key itself. [2][3]

## Actions

- **A1** Clicking a card opens that section: /settings/mediamanagement, /profiles, /quality, /customformats, /indexers, /downloadclients, /importlists, /connect, /metadata, /metadatasource, /tags, /general or /ui (all under /settings). [2][1]

## Rules

- **R1** The page is reached at the route /settings. [4]
- **R2** The settings page wrapper is rendered with the save option turned off. [1]
- **R3** Translation replaces {token} placeholders with supplied values, and {appName} is always "Sonarr". [3]
- **R4** In non-production mode, a missing translation key logs a console warning. [3]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Settings/Settings.tsx#Settings`
2. `ts|frontend/src/Settings/Settings.tsx#buildSections`
3. `ts|frontend/src/Utilities/String/translate.ts#translate`
4. `page:tsconfig.json|/settings`

<sub>commit 0bffefe · inputs 04f7cb2 · slice of 34 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
