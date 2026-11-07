# Select input (dropdown)

`ts|frontend/src/Components/Form/SelectInput.tsx#SelectInput` · component · sonarr

From the code: SelectInput

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Displays a dropdown list with one option per entry in the supplied values list. [1]
- **S2** Each option uses its key as the option value, and its label is either the text given or the result of calling it when it is a function. [1]
- **S3** The dropdown shows the currently selected value passed in. [1]
- **S4** Shows an error state when hasError is set and a warning state when hasWarning is set. [1]
- **S5** Shows a disabled appearance when isDisabled is true. [1]

## Actions

- **A1** Choosing an option calls onChange with the input's name and the selected option's value. [1]
- **A2** Leaving the field calls onBlur. [1]

## Rules

- **R1** The dropdown is disabled when isDisabled is true; it defaults to false. [1]
- **R2** Autofocus defaults to false. [1]
- **R3** Any extra properties on an option (other than key and value) are passed through to the option element. [1]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Components/Form/SelectInput.tsx#SelectInput`

<sub>commit 0bffefe · inputs 5926088 · slice of 19 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
