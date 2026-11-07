# Table component

`ts|frontend/src/Components/Table/Table.tsx#Table` · component · sonarr

From the code: Table

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Renders an HTML table with a header row followed by whatever rows are passed in as children, inside a scroll container. [1][2]
- **S2** Shows one header cell per column that is visible; columns that are not visible are omitted. [1]
- **S3** Each normal header cell shows the column label, calling it first if the label is a function. [1]
- **S4** A sortable header cell that is the current sort column shows an ascending or descending sort icon according to the sort direction. [3]
- **S5** When select-all is on and a select-all handler is given, a first header cell with a checkbox labelled 'SelectAll' (translated) is shown. [1][4]
- **S6** The select-all checkbox is checked when all are selected, unchecked when all are unselected, and in an indeterminate (null) state otherwise. [4]
- **S7** For the 'actions' or 'details' column, when a table option change handler is given, the header shows a table options icon button titled 'TableOptions' (translated) instead of a label. [1][5]

## Actions

- **A1** Pressing a sortable header cell calls the sort handler with the column name, plus the column's fixed sort direction if it has one. [3]
- **A2** Pressing the table options button opens the table options modal; closing the modal hides it. [6]
- **A3** Changing the select-all checkbox calls the select-all change handler. [4]

## Rules

- **R1** Horizontal scrolling is on by default; when off, the container does not scroll horizontally. [1]
- **R2** Select-all is off by default and the select-all cell appears only when select-all is true and an onSelectAllChange handler is provided. [1]
- **R3** The options column header cell is never sortable. [1]
- **R4** A header cell is sortable only if the column is marked sortable (default false); non-sortable columns render a plain header cell. [3]
- **R5** A sortable header's aria-sort is 'none' when not the sorted column, otherwise 'ascending' or 'descending'. [3]
- **R6** A column is treated as the sorted column when it is sortable and its name equals the sort key. [3]
- **R7** Translation falls back to the key when no translation exists, and substitutes {tokens} in the text; the appName token is always 'Sonarr'. [7]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Components/Table/Table.tsx#Table`
2. `ts|frontend/src/Components/Table/TableHeader.tsx#TableHeader`
3. `ts|frontend/src/Components/Table/TableHeaderCell.tsx#TableHeaderCell`
4. `ts|frontend/src/Components/Table/TableSelectAllHeaderCell.tsx#TableSelectAllHeaderCell`
5. `ts|frontend/src/Components/Table/TableOptions/TableOptionsButton.tsx#TableOptionsButton`
6. `ts|frontend/src/Components/Table/TableOptions/TableOptionsModalWrapper.tsx#TableOptionsModalWrapper`
7. `ts|frontend/src/Utilities/String/translate.ts#translate`

<sub>commit 0bffefe · inputs 73fdae2 · slice of 91 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
