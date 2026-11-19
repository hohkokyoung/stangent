# Progress Bar

`ts|frontend/src/Components/ProgressBar.tsx#ProgressBar` · component · sonarr

From the code: ProgressBar

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Shows a horizontal bar filled to the given progress percentage. [1]
- **S2** When showText is on, the bar shows text over it: the supplied text, or otherwise the progress percentage. [1]
- **S3** The text in the unfilled background part is shown only when both showText is on and a width is given. [1]
- **S4** Shows the supplied title as a hover tooltip on the container. [1]
- **S5** The bar is exposed as a meter with minimum 0, maximum 100 and current value equal to the progress rounded down. [1]

## Actions

Nothing found in the code.

## Rules

- **R1** The percentage is formatted with 1 decimal place by default, or the given precision. [1]
- **R2** Width defaults to 100% of the container; if a width is given it is used in pixels. [1]
- **R3** Defaults are kind 'primary', size 'medium', and showText off. [1]
- **R4** The accessible label is the given ariaLabel, or otherwise the translated 'ProgressBarProgress' text with progress rounded to a whole number. [1][2]
- **R5** If a translation key is missing, the key itself is used as the text; tokens like {progress} are replaced, and the appName token is always set to 'Sonarr'. [2]
- **R6** A console warning for a missing translation is logged only when not in production. [2]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Components/ProgressBar.tsx#ProgressBar`
2. `ts|frontend/src/Utilities/String/translate.ts#translate`

<sub>commit 0bffefe · inputs 1527333 · slice of 16 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
