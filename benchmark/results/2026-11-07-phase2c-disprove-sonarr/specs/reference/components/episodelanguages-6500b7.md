# Episode Languages

`ts|frontend/src/Episode/EpisodeLanguages.tsx#EpisodeLanguages` · component · sonarr

From the code: EpisodeLanguages

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** When the episode has no languages (missing or empty list), nothing is shown. [1]
- **S2** When the episode has exactly one language, a label with that language's name is shown. [1]
- **S3** When the episode has more than one language, a label with the translated text 'MultiLanguages' is shown as the anchor of a popover. [1][2]
- **S4** The popover is titled with the translated text 'Languages' and its body is a list with one item per language name. [1][2]
- **S5** The popover is positioned to the left of its anchor. [1]

## Actions

Nothing found in the code.

## Rules

- **R1** The label uses the inverse style when the cutoff is not met and the default style otherwise; isCutoffNotMet defaults to true. [1]
- **R2** Translation falls back to the key itself when no translation exists, and {token} placeholders are replaced from the supplied tokens, with appName always set to 'Sonarr'. [2]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Episode/EpisodeLanguages.tsx#EpisodeLanguages`
2. `ts|frontend/src/Utilities/String/translate.ts#translate`

<sub>commit 0bffefe · inputs f3117a7 · slice of 26 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
